using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

/// <summary>Native Win32 tray, settings, and non-activating HUD. All UI work stays on the message thread.</summary>
public sealed partial class NativeDesktop : IDisposable
{
    private static NativeDesktop? current;
    private readonly ConcurrentQueue<Action> pending = new();
    private readonly Dictionary<string, nint> fields = [];
    private ThemeColors theme;
    private readonly Func<ThemeColors> themeProvider;
    private readonly Func<nint> foregroundWindow;
    private nint background, inputBackground, accent, surfaceBrush, borderBrush, selectionBrush;
    private readonly nint font = Win32.CreateFontW(-17, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
    private readonly nint headingFont = Win32.CreateFontW(-24, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
    private readonly nint hudTitleFont = Win32.CreateFontW(-16, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
    private readonly nint hudModelFont = Win32.CreateFontW(-10, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
    private readonly nint hudPreviewFont = Win32.CreateFontW(-13, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
    private readonly nint menuIconFont = Win32.CreateFontW(-16, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe MDL2 Assets");
    private GlobalKeyboardHook? hook;
    private nint messageWindow, settingsWindow, hudWindow, statusControl, previewControl;
    private Win32.NotifyIconData tray;
    private readonly RecordingFeedback feedback = new();
    private MicrophoneIcons? icons;
    private ProviderIcons? providerIcons;
    private SoundCues? sounds;
    private AppConfig config;
    private string status = "Starting…";
    private string hudPreview = "";
    private bool paused, active, disposed;
    private string lastText = "";
    private readonly float[] levels = new float[48];
    private int levelIndex;
    public Func<float>? ReadLevel { get; set; }
    public Action? Pressed { get; set; }
    public Action<VoiceMode>? VoicePressed { get; set; }
    public Action? Released { get; set; }
    public Action? CancelRequested { get; set; }
    public Action<bool>? PauseChanged { get; set; }
    public Action<AppConfig>? ConfigChanged { get; set; }
    public Action<PillPosition>? PillPositionChanged { get; set; }
    public Action? SignInRequested { get; set; }
    public Action? SignOutRequested { get; set; }
    public Action<bool>? GoogleAccountRequested { get; set; }
    public Action? ClaudeSignInRequested { get; set; }
    public Action? ClaudeSignOutRequested { get; set; }
    public Func<bool>? IsClaudeSignedIn { get; set; }
    public Func<bool>? IsChatGptSignedIn { get; set; }
    public Func<bool>? IsAuthenticationBusy { get; set; }
    public nint SettingsHandle => settingsWindow;
    public bool IsPaused => paused;

    public NativeDesktop(AppConfig config, Func<ThemeColors>? themeProvider = null, Func<nint>? foregroundWindowProvider = null, Action<string, nint>? copyDiagnostics = null, Action<string>? openDiagnosticsUrl = null)
    {
        this.config = config;
        this.copyDiagnostics = copyDiagnostics ?? TextInjector.SetClipboardText;
        this.openDiagnosticsUrl = openDiagnosticsUrl ?? OpenDiagnosticBrowser;
        this.themeProvider = themeProvider ?? ThemeColors.ReadSystem;
        foregroundWindow = foregroundWindowProvider ?? Win32.GetForegroundWindow;
        theme = this.themeProvider();
    }

    public void Initialize(bool showSettings = true, bool integrateShell = true, bool audioFeedback = true)
    {
        if (current is not null) throw new InvalidOperationException("Only one desktop UI can run.");
        current = this;
        icons = new MicrophoneIcons();
        providerIcons = new ProviderIcons();
        if (audioFeedback) sounds = new SoundCues();
        CreateThemeBrushes();
        RegisterWindowClass();
        var instance = Win32.GetModuleHandleW(null);
        messageWindow = Create(0, "SharpVoice.Native", "SharpVoice", 0, 0, 0, 0, 0, 0, 0);
        Win32.SystemParametersInfoW(0x30, 0, out var area, 0);
        settingsWindow = Create(0, "SharpVoice.Native", "SharpVoice · Settings", 0x00CA0000, Math.Max(area.Left, (area.Right - 740) / 2), Math.Max(area.Top, (area.Bottom - 800) / 2), 740, 800, 0, 0);
        var pill = ResolvePillPosition(config.PillPosition ?? new((area.Right + area.Left - PillLayout.Width) / 2, area.Bottom - 136));
        // WS_EX_LAYERED | TOPMOST | TOOLWINDOW | NOACTIVATE; the pill accepts dragging.
        hudWindow = Create(0x08080088, "SharpVoice.Native", "SharpVoice HUD", 0x80000000, pill.X, pill.Y, PillLayout.Width, PillLayout.Height, 0, 0);
        Win32.SetLayeredWindowAttributes(hudWindow, 0, PillLayout.Opacity, 2);
        ApplyPillRegion();
        BuildSettings();
        if (integrateShell) InstallTray();
        ApplyMicrophoneIcon();
        if (integrateShell) hook = new GlobalKeyboardHook(config.Hotkey, config.AskHotkey, mode => Post(() => { if (VoicePressed is not null) VoicePressed(mode); else Pressed?.Invoke(); }), () => Post(() => Released?.Invoke()), config.RecordingGesture == "toggle");
        ApplyTheme();
        if (showSettings) ShowSettings();
    }

    private unsafe void RegisterWindowClass()
    {
        fixed (char* name = "SharpVoice.Native")
        {
            var wc = new Win32.WindowClass
            {
                Size = (uint)sizeof(Win32.WindowClass), Instance = Win32.GetModuleHandleW(null), ClassName = name,
                Proc = &WindowProc, Cursor = Win32.LoadCursorW(0, 32512), Background = background,
                Icon = icons![MicrophoneState.Neutral].Large, SmallIcon = icons[MicrophoneState.Neutral].Small
            };
            if (Win32.RegisterClassExW(in wc) == 0) throw new InvalidOperationException("Could not register the SharpVoice window class.");
        }
    }

    private static nint Create(uint ex, string type, string title, uint style, int x, int y, int width, int height, nint parent, int id)
    {
        var result = Win32.CreateWindowExW(ex, type, title, style, x, y, width, height, parent, id, Win32.GetModuleHandleW(null), 0);
        if (result == 0) throw new InvalidOperationException($"Could not create the {type} window.");
        return result;
    }

    private nint Control(string type, string title, uint style, int x, int y, int width, int height, int id = 0)
    {
        var hwnd = Create(0, type, title, 0x50000000 | style, x, y, width, height, settingsWindow, id);
        Win32.SendMessageW(hwnd, 0x30, (nuint)font, 1);
        if (type == "EDIT") Win32.SendMessageW(hwnd, 0xD3, 3, (10 << 16) | 10);
        InstallControlHover(hwnd, type);
        if (buildingPage >= 0) pages[buildingPage].Add(hwnd);
        return hwnd;
    }

    public void Post(Action action)
    {
        if (disposed) return;
        pending.Enqueue(action);
        Win32.PostMessageW(messageWindow, Win32.WmApp, 0, 0);
    }

    public void SetStatus(string text, bool showHud = false, bool isError = false, bool warningSound = false)
    {
        status = text;
        if (setupWindow != 0 && !setupInSettings) Win32.SetWindowTextW(setupNotice, text);
        if (isError)
        {
            hudPasteCompleted = false;
            var cue = feedback.ReportError();
            if (warningSound) sounds?.Play(cue);
        }
        else feedback.ClearError();
        ApplyMicrophoneIcon();
        Win32.SetWindowTextW(statusControl, SettingsStatus(text, isError));
        UpdateHelp("Status", StatusHelp());
        RefreshAuthentication();
        UpdateTrayTip();
        if (showHud) ShowHud();
        Win32.InvalidateRect(hudWindow, 0, 0);
    }

    public void PlayFollowUpQuestion() => sounds?.Play(FeedbackCue.FollowUpQuestion);

    public void PlayInsertionSuccess()
    {
        sounds?.Play(FeedbackCue.InsertionSuccess);
        hudPasteCompleted = true;
        if (!active) ScheduleHudDismissal();
    }

    public void SetRecordingGestureActive(bool active) => hook?.SetRecordingActive(active);
    public void ResetRecordingGesture() => hook?.ResetRecordingGesture();
    public void SetExternalRecording(bool active) => hook?.SetExternalRecording(active);

    public void SetRecording(bool value)
    {
        SetRecordingGestureActive(value);
        var cue = value ? feedback.BeginRecording() : feedback.EndRecording();
        sounds?.Play(cue);
        ApplyMicrophoneIcon();
    }

    private void ApplyMicrophoneIcon()
    {
        if (icons is null) return;
        var pair = icons[feedback.State];
        foreach (var window in new[] { messageWindow, settingsWindow, hudWindow, toolsWindow, connectedWindow, setupWindow })
        {
            if (window == 0) continue;
            Win32.SendMessageW(window, 0x80, 1, pair.Large); // WM_SETICON, ICON_BIG
            Win32.SendMessageW(window, 0x80, 0, pair.Small);
        }
        if (tray.Window == 0 || tray.Icon == pair.Tray) return;
        tray.Icon = pair.Tray;
        Win32.Shell_NotifyIconW(1, ref tray);
    }

    public void SetPreview(string text, bool retain = false)
    {
        hudPreview = text;
        Win32.SetWindowTextW(previewControl, text);
        if (retain) lastText = text;
        Win32.InvalidateRect(hudWindow, 0, 0);
    }

    public void SetActive(bool value, nint targetWindow = 0)
    {
        if (value && !active) PositionPillForRecording(targetWindow);
        if (value) { hudPasteCompleted = false; CancelHistoryRetry(); CancelConnectionTest(); HideHelp(); StopInputPreview(); }
        active = value;
        RefreshInputMonitoring();
        RefreshHistoryRetryControls();
        RefreshConnectionTestControls();
        Win32.ShowWindow(cancelButton, active ? 5 : 0);
        Win32.KillTimer(hudWindow, 2);
        if (active)
        {
            Array.Clear(levels);
            Win32.SetTimer(hudWindow, 1, 40, 0);
            ShowHud();
        }
        else
        {
            Win32.KillTimer(hudWindow, 1);
            ScheduleHudDismissal();
        }
    }

    private void ShowSettings() { ApplyTheme(); RequestModelsRefresh(); Win32.ShowWindow(settingsWindow, 5); RefreshInputMonitoring(); Win32.SetTimer(settingsWindow, 3, 100, 0); Win32.SetForegroundWindow(settingsWindow); }
    private void CopyLast()
    {
        if (lastText.Length == 0) throw new InvalidOperationException("No completed transcript to copy yet.");
        TextInjector.SetClipboardText(lastText, messageWindow);
        SetStatus("Copied last text to the clipboard.");
    }

    private unsafe void InstallTray()
    {
        tray = new() { Size = (uint)sizeof(Win32.NotifyIconData), Window = messageWindow, Id = 1, Flags = 7, Callback = Win32.WmTray, Icon = icons![MicrophoneState.Neutral].Tray };
        fixed (char* tip = tray.Tip) $"SharpVoice · Dictate: {HotkeyDefinition.DisplayName(config.Hotkey)} · Ask AI: {HotkeyDefinition.DisplayName(config.AskHotkey)}".AsSpan().CopyTo(new Span<char>(tip, 128));
        if (Win32.Shell_NotifyIconW(0, ref tray) == 0) throw new InvalidOperationException("Could not add SharpVoice to the system tray.");
    }

    private unsafe void UpdateTrayTip()
    {
        fixed (char* tip = tray.Tip)
        {
            new Span<char>(tip, 128).Clear();
            var value = "SharpVoice · " + (config.WakePhrasesEnabled ? wakePreparationState.Message + " · " : "") + status;
            value.AsSpan(0, Math.Min(127, value.Length)).CopyTo(new Span<char>(tip, 128));
        }
        Win32.Shell_NotifyIconW(1, ref tray);
    }

    private void TrayMenu()
    {
        ApplyTheme();
        var menu = Win32.CreatePopupMenu();
        if (menu == 0) throw new InvalidOperationException("Could not create tray menu.");
        ClearMenuLabels();
        try
        {
            StyleMenu(menu);
            AddMenu(menu, 1, "Open settings");
            AddMenu(menu, 2, paused ? "Resume microphone" : "Pause microphone");
            AddMenu(menu, 6, "Copy last text");
            Win32.AppendMenuW(menu, 0x800, 0, "");
            AddMenu(menu, 7, "Exit SharpVoice");
            Win32.GetCursorPos(out var point);
            Win32.SetForegroundWindow(messageWindow);
            var command = Win32.TrackPopupMenu(menu, 0x102, point.X, point.Y, 0, messageWindow, 0);
            Win32.PostMessageW(messageWindow, 0, 0, 0);
            switch (command)
            {
                case 1: ShowSettings(); break;
                case 2: paused = !paused; if (paused) StopInputPreview(); PauseChanged?.Invoke(paused); RefreshInputMonitoring(); break;
                case 6: CopyLast(); break;
                case 7: Win32.PostQuitMessage(0); break;
            }
        }
        finally { Win32.DestroyMenu(menu); ClearMenuLabels(); }
    }

    private readonly Direct2DHud hudRenderer = new();

    private void PaintHud()
    {
        var dc = Win32.BeginPaint(hudWindow, out var paint);
        try
        {
            Win32.GetClientRect(hudWindow, out var rect);
            if (hudRenderer.Draw(dc, rect, theme, HudTitle, HudDetail, levels, levelIndex, ModelSummary, feedback.State == MicrophoneState.Recording)) return;
            Win32.FillRect(dc, rect, background);
            var oldFont = Win32.SelectObject(dc, hudTitleFont);
            Win32.SetBkMode(dc, 1);
            Win32.SetTextColor(dc, theme.Text);
            Rounded(dc, rect, borderBrush, 96);
            var fill = rect; fill.Left++; fill.Top++; fill.Right--; fill.Bottom--;
            Rounded(dc, fill, surfaceBrush, 94);
            var badge = new Win32.Rect { Left = 18, Top = 30, Right = 54, Bottom = 66 };
            Rounded(dc, badge, selectionBrush, 36);
            DrawMenuGlyph(dc, "\uE720", badge, feedback.State == MicrophoneState.Recording ? 0x005050EF : theme.Accent);
            var live = feedback.State == MicrophoneState.Recording && !string.IsNullOrWhiteSpace(liveDraft);
            for (var i = 0; i < (live ? 5 : 9); i++)
            {
                var value = levels[(levelIndex + i * Math.Max(1, levels.Length / 9)) % levels.Length];
                var height = 4 + (int)(Math.Clamp(value, 0, 1) * (live ? 18 : 27));
                var bar = new Win32.Rect { Left = (live ? 467 : 441) + i * 5, Top = 48 - height / 2, Right = (live ? 470 : 444) + i * 5, Bottom = 48 + (height + 1) / 2 };
                Rounded(dc, bar, accent, 3);
            }
            var title = new Win32.Rect { Left = 68, Top = 14, Right = 198, Bottom = 36 };
            Win32.DrawTextW(dc, HudTitle, -1, ref title, 0x8824);
            var model = new Win32.Rect { Left = 200, Top = 17, Right = 485, Bottom = 34 };
            Win32.SetTextColor(dc, theme.Muted);
            Win32.SelectObject(dc, hudModelFont);
            Win32.DrawTextW(dc, ModelSummary, -1, ref model, 0x8826);
            var preview = new Win32.Rect { Left = 68, Top = 40, Right = live ? 454 : 426, Bottom = 76 };
            Win32.SelectObject(dc, hudPreviewFont);
            Win32.SetTextColor(dc, theme.Text);
            Win32.DrawTextW(dc, HudDetail, -1, ref preview, 0xA810);
            Win32.SelectObject(dc, oldFont);
        }
        finally { Win32.EndPaint(hudWindow, paint); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        var self = current;
        if (self is null) return Win32.DefWindowProcW(hwnd, message, wParam, lParam);
        try
        {
            if (message == Win32.WmApp)
            {
                while (self.pending.TryDequeue(out var action)) action();
                return 0;
            }
            if (message == Win32.WmTray)
            {
                if (lParam == 0x205) self.TrayMenu();
                else if (lParam == 0x203) self.ShowSettings();
                else if (lParam == 0x405) { self.ShowSettings(); self.ShowSettingsPage(5); self.OpenUpdates(); }
                return 0;
            }
            if (self.HandleHelpMessage(hwnd, message, wParam, lParam, out var helpResult)) return helpResult;
            if (self.HandleSetupMessage(hwnd, message, wParam)) return 0;
            if (self.HandleConnectedMessage(hwnd, message, wParam)) return 0;
            if (self.HandleAnswerMessage(hwnd, message, wParam)) return 0;
            if (self.HandlePresentationMessage(hwnd, message, wParam, lParam, out var presentationResult)) return presentationResult;
            if (message is 0x133 or 0x134 or 0x138)
            {
                Win32.SetTextColor((nint)wParam, lParam == self.connectionStatus ? self.ConnectionStatusColor : lParam == self.answerModel || lParam == self.answerStatus ? self.theme.Muted : self.theme.Text);
                var inPage = self.pages.Any(page => page.Contains(lParam));
                var field = message is 0x133 or 0x134 || lParam == self.previewControl;
                var hot = field && self.ControlIsHovered(lParam);
                Win32.SetBkColor((nint)wParam, field ? hot ? self.theme.Selection : self.theme.Field : inPage ? self.theme.Surface : self.theme.Background);
                return field ? hot ? self.selectionBrush : self.inputBackground : inPage ? self.surfaceBrush : self.background;
            }
            if (self.HandleToolsMessage(hwnd, message, wParam, lParam)) return 0;
            if (hwnd == self.settingsWindow)
            {
                if (self.HandleSettingsMessage(message, wParam, lParam)) return 0;
                if (message == 0x10) { self.CloseSettings(); return 0; }
                if (message == 0x111 && (wParam >> 16) == 0)
                {
                    switch ((int)(wParam & 0xFFFF))
                    {
                        case 101: self.ApplySettings(); break;
                        case 102: if (self.SelectedChoice("Backend") == "gemini_api") self.OpenGeminiApiKey(); else self.RequestAuthentication(ChatGptAccountAction.SignIn, self.SelectedChoice("Backend")); break;
                        case 103: self.AccountMenu(); break;
                        case 104: self.CopyLast(); break;
                        case 114: self.OpenWritingTools("dictionary"); break;
                        case 115: self.OpenWritingTools("snippets"); break;
                        case 116: self.OpenWritingTools("profiles"); break;
                        case 117: self.OpenHistory(); break;
                        case 118: self.OpenPlugins(); break;
                        case 120: self.OpenConnectedSettings(); break;
                        case 121: self.OpenWakePhrases(); break;
                        case 122: self.OpenUpdates(); break;
                        case 119: self.OpenSmartAsk(); break;
                    }
                    return 0;
                }
            }
            if (hwnd == self.hudWindow)
            {
                if (self.HandleHudInteraction(message)) return 0;
                if (message == 0x84) return 2; // HTCAPTION: native borderless drag.
                if (message == 0xF) { self.PaintHud(); return 0; }
                if (message == 0x113)
                {
                    if (wParam == 2)
                    {
                        if (!self.active && !self.hudMoving && Environment.TickCount64 >= self.hudDismissAfter)
                        { Win32.KillTimer(hwnd, 2); Win32.ShowWindow(hwnd, 0); }
                    }
                    else
                    {
                        var reading = self.ReadLevel?.Invoke() ?? -100;
                        self.levels[self.levelIndex] = Math.Clamp(((float.IsFinite(reading) ? reading : -100) + 60) / 60, 0, 1);
                        self.levelIndex = (self.levelIndex + 1) % self.levels.Length;
                        Win32.InvalidateRect(hwnd, 0, 0);
                    }
                    return 0;
                }
                if (message == 0x21) return 3; // MA_NOACTIVATE
            }
        }
        catch (Exception ex)
        {
            self.SetStatus(ex.Message, isError: true, warningSound: true);
            Win32.MessageBoxW(self.settingsWindow, ex.Message, "SharpVoice", 0x10);
            return 0;
        }
        return Win32.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    public void Run()
    {
        int result;
        while ((result = Win32.GetMessageW(out var message, 0, 0, 0)) > 0)
        {
            if (HandleSetupInput(in message) || HandleConnectedInput(in message) || HandleAnswerInput(in message) || HandleToolsInput(in message) || HandleLocalDownloadInput(in message) || HandleConnectionInput(in message) || HandleHelpInput(in message)) continue;
            if (setupWindow != 0 && Win32.IsWindowVisible(setupWindow) != 0 && Win32.IsDialogMessageW(setupWindow, message) != 0) continue;
            if (connectedWindow != 0 && Win32.IsWindowVisible(connectedWindow) != 0 && Win32.IsDialogMessageW(connectedWindow, message) != 0) continue;
            if (answerWindow != 0 && Win32.IsWindowVisible(answerWindow) != 0 && Win32.IsDialogMessageW(answerWindow, message) != 0) continue;
            if (toolsWindow != 0 && Win32.IsDialogMessageW(toolsWindow, message) != 0) continue;
            if (Win32.IsDialogMessageW(settingsWindow, message) != 0) continue;
            Win32.TranslateMessage(message);
            Win32.DispatchMessageW(message);
        }
        if (result == -1) throw new InvalidOperationException("Windows message loop failed.");
    }

    public void Dispose()
    {
        disposed = true;
        StopInputPreview();
        CloseSetupCore();
        HideAnswer(); HideConnected(); CancelConnectedDiscovery();
        if (connectedWindow != 0) Win32.DestroyWindow(connectedWindow);
        if (answerWindow != 0) Win32.DestroyWindow(answerWindow);
        CloseTools();
        historyRetryCancellation?.Dispose(); historyRetryCancellation = null;
        CancelLocalDownloadCore(clear: false);
        localDownloadCancellation?.Dispose(); localDownloadCancellation = null;
        CancelConnectionTestCore(clearResult: false);
        connectionCancellation?.Dispose(); connectionCancellation = null;
        hook?.Dispose();
        DisposeHelp();
        Win32.Shell_NotifyIconW(2, ref tray);
        if (hudWindow != 0) { Win32.KillTimer(hudWindow, 1); Win32.KillTimer(hudWindow, 2); Win32.DestroyWindow(hudWindow); }
        if (settingsWindow != 0) Win32.DestroyWindow(settingsWindow);
        if (messageWindow != 0) Win32.DestroyWindow(messageWindow);
        Win32.KillTimer(settingsWindow, 3);
        hudRenderer.Dispose();
        sounds?.Dispose();
        icons?.Dispose();
        providerIcons?.Dispose();
        Win32.DeleteObject(hudTitleFont); Win32.DeleteObject(hudModelFont); Win32.DeleteObject(hudPreviewFont);
        Win32.DeleteObject(font); Win32.DeleteObject(headingFont); Win32.DeleteObject(menuIconFont);
        DeleteThemeBrushes();
        current = null;
    }
}
