using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private bool hudMoving, hudPasteCompleted;
    private long hudDismissAfter;

    private void ShowHud()
    {
        ClampPillToWorkArea();
        // Reassert HWND_TOPMOST on every show; preserve geometry and typing focus.
        // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW.
        SetWindowPos(hudWindow, -1, 0, 0, 0, 0, 0x53);
        if (!active) ScheduleHudDismissal();
    }

    private void ScheduleHudDismissal()
    {
        Win32.KillTimer(hudWindow, 2);
        if (!active && !hudMoving)
        {
            var delay = hudPasteCompleted ? PillLayout.PastedDismissMs : PillLayout.OtherDismissMs;
            hudDismissAfter = Environment.TickCount64 + delay;
            Win32.SetTimer(hudWindow, 2, delay, 0);
        }
    }

    private bool HandleHudInteraction(uint message)
    {
        if (message == 0x231) { hudMoving = true; Win32.KillTimer(hudWindow, 2); return true; } // WM_ENTERSIZEMOVE
        if (message == 0x232) // WM_EXITSIZEMOVE
        {
            if (!hudMoving) return true;
            hudMoving = false;
            ClampPillToWorkArea();
            Win32.GetWindowRect(hudWindow, out var rect);
            var position = new PillPosition(rect.Left, rect.Top);
            if (config.PillPosition != position)
            {
                try { PillPositionChanged?.Invoke(position); config = config with { PillPosition = position }; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { hudPasteCompleted = false; SetStatus("Pill position could not be saved. Move it again to retry.", isError: true); }
            }
            ScheduleHudDismissal();
            return true;
        }
        if (message == 0x7E && !hudMoving) { ClampPillToWorkArea(); return true; } // WM_DISPLAYCHANGE
        return message == 0xA3; // Suppress caption double-click maximize.
    }

    private unsafe PillPosition ResolvePillPosition(PillPosition position)
    {
        var area = PillWorkAreaForPosition(position);
        return PillLayout.Clamp(position, area.Left, area.Top, area.Right, area.Bottom);
    }

    private unsafe PillWorkArea PillWorkAreaForPosition(PillPosition position)
    {
        var candidate = new Win32.Rect { Left = position.X, Top = position.Y, Right = position.X, Bottom = position.Y };
        var monitor = MonitorFromRect(&candidate, 2); // MONITOR_DEFAULTTONEAREST
        var info = new HelpMonitorInfo { Size = (uint)sizeof(HelpMonitorInfo) };
        Win32.Rect area;
        if (monitor != 0 && GetMonitorInfoW(monitor, &info) != 0) area = info.Work;
        else Win32.SystemParametersInfoW(0x30, 0, out area, 0);
        return new(area.Left, area.Top, area.Right, area.Bottom);
    }

    private unsafe void PositionPillForRecording(nint targetWindow)
    {
        if (hudMoving) return;
        if (targetWindow == 0) targetWindow = Win32.GetForegroundWindow();
        if (targetWindow == 0) return;
        var monitor = MonitorFromWindow(targetWindow, 2);
        var info = new HelpMonitorInfo { Size = (uint)sizeof(HelpMonitorInfo) };
        if (monitor == 0 || GetMonitorInfoW(monitor, &info) == 0) return;
        var area = new PillWorkArea(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
        var source = config.PillPosition is PillPosition saved ? PillWorkAreaForPosition(saved) : area;
        var position = PillLayout.OnMonitor(config.PillPosition, source, area);
        SetWindowPos(hudWindow, -1, position.X, position.Y, 0, 0, 0x11); // NOSIZE | NOACTIVATE; showing happens separately.
    }

    private void ClampPillToWorkArea()
    {
        if (hudMoving) return;
        Win32.GetWindowRect(hudWindow, out var rect);
        var position = ResolvePillPosition(new(rect.Left, rect.Top));
        if (position.X != rect.Left || position.Y != rect.Top)
            SetWindowPos(hudWindow, -1, position.X, position.Y, 0, 0, 0x11); // NOSIZE | NOACTIVATE
    }

    [LibraryImport("user32.dll")] private static unsafe partial nint MonitorFromRect(Win32.Rect* rect, uint flags);

    private bool editingSelection, cleanupSkipped;
    private string? liveDraft;
    public void SetLiveDraft(string? text) { liveDraft = text; Win32.InvalidateRect(hudWindow, 0, 0); }
    private AppConfig? dictationConfig;
    private string dictationProfile = "";
    private AppConfig HudConfig => dictationConfig ?? config;
    public void SetDictationConfig(AppConfig value, string profile = "")
    {
        if (value.Backend != HudConfig.Backend || value.RefinementModel != HudConfig.RefinementModel)
        { resolvedBackend = resolvedModel = ""; modelWasUsed = false; }
        dictationConfig = value; dictationProfile = profile; Win32.InvalidateRect(hudWindow, 0, 0);
    }
    public void SetCleanupSkipped(bool value) { cleanupSkipped = value; Win32.InvalidateRect(hudWindow, 0, 0); }
    private VoiceMode voiceMode;
    public void SetVoiceMode(VoiceMode mode)
    {
        voiceMode = mode;
        cleanupSkipped = false;
        if (cancelButton != 0) Win32.SetWindowTextW(cancelButton, mode == VoiceMode.AskAi ? "Cancel request" : "Cancel dictation");
        Win32.InvalidateRect(hudWindow, 0, 0);
    }
    public void SetEditingSelection(bool value) { editingSelection = value; Win32.InvalidateRect(hudWindow, 0, 0); }
    private string HudTitle => feedback.State == MicrophoneState.Error ? "Needs attention"
        : feedback.State == MicrophoneState.Recording ? voiceMode == VoiceMode.AskAi ? "Listening" : editingSelection ? "Listening" : "Listening"
        : status.StartsWith("Finalizing", StringComparison.Ordinal) ? "Finalizing"
        : status.StartsWith("Transcribing", StringComparison.Ordinal) ? "Transcribing"
        : status.StartsWith("Answering", StringComparison.Ordinal) ? "Thinking"
        : status.StartsWith("Refining", StringComparison.Ordinal) ? editingSelection ? "Editing selection" : "Polishing"
        : status.StartsWith("Finishing", StringComparison.Ordinal) ? "Finishing"
        : status.StartsWith("Inserted", StringComparison.Ordinal) ? voiceMode == VoiceMode.AskAi ? "Done" : editingSelection ? "Done" : "Done"
        : status.Contains("ancell", StringComparison.OrdinalIgnoreCase) ? "Cancelled"
        : status.Contains("Ready", StringComparison.Ordinal) ? "Ready" : Short(status, 42);
    private string HudDetail => feedback.State == MicrophoneState.Error ? Short(status, 500)
        : feedback.State == MicrophoneState.Recording && !string.IsNullOrWhiteSpace(liveDraft) ? "Live Draft · " + TranscriptDraft.Tail(liveDraft, 88)
        : string.IsNullOrWhiteSpace(hudPreview) ? feedback.State == MicrophoneState.Recording ? voiceMode == VoiceMode.AskAi ? HudConfig.RecordingGesture == "toggle" ? "Speak a request · press again for the answer" : "Speak a request · release for the answer" : editingSelection ? HudConfig.RecordingGesture == "toggle" ? "Describe the changes · press again to apply" : "Describe the changes · release to apply" : HudConfig.RecordingGesture == "toggle" ? "Press the shortcut again when you’re finished" : "Release the keys when you’re finished" : "Your voice. Your words." : Short(hudPreview, 500);
    private static string Short(string text, int length)
    {
        if (text.Length <= length) return text;
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length] + "…";
    }
    private void ApplyPillRegion()
    {
        var region = CreateRoundRectRgn(0, 0, 521, 97, 96, 96);
        if (region != 0 && SetWindowRgn(hudWindow, region, 1) == 0) Win32.DeleteObject(region);
    }
    [LibraryImport("gdi32.dll")] private static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
    [LibraryImport("user32.dll")] private static partial int SetWindowRgn(nint window, nint region, int redraw);
}
