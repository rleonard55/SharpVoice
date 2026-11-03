using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private sealed record HelpEntry(string Key, string Title, nint Button, string Content);
    private readonly Dictionary<int, HelpEntry> helpEntries = [];
    private nint helpWindow, helpTitle, helpBody, retentionNotice;
    private int openHelpId, shownHelpId, hoveredHelpId;
    private nuint hoverTimerId, nextHelpTimerId = 0x7000;
    internal nint HelpWindow => helpWindow;
    internal nint HelpBody => helpBody;
    internal int OpenHelpId => openHelpId;
    internal nuint PendingHelpTimerId => hoverTimerId;

    private string StatusHelp() => status + (dictationProfile.Length == 0 ? "" : "\nApp Profile: " + dictationProfile) + "\n\n" + (config.RecordingGesture == "toggle" ? "Press " : "Hold ") + HotkeyDefinition.DisplayName(config.Hotkey) +
        " to dictate. " + (config.RecordingGesture == "toggle" ? "Press " : "Hold ") + HotkeyDefinition.DisplayName(config.AskHotkey) +
        " to ask AI. " + (config.RecordingGesture == "toggle" ? "Press the shortcut again to finish. " : "Release to finish. ") + "Settings changes take effect after Apply Settings.";

    private string HelpText(string key) => key switch
    {
        "RecordingGesture" => "Hold to Talk records while the shortcut is held. Toggle starts with one press and stops with the next. Both dictation and Ask AI support Toggle; adding Shift to the first Ctrl+Alt gesture still promotes it to Ask AI. Release all keys between gestures. Pause and Cancel always stop recording.",
        "SilenceStopSeconds" => "Optional Toggle-only auto-stop: finish recording after the input stays below the applied noise gate for this many seconds. 0 disables it (default); use 1–30 seconds. This is a level-based timeout, not speech recognition. A low gate or loud background may prevent it firing. The recording limit still applies.",
        "PersistHistory" => "In Memory (default) keeps the 50 most recent text transcripts/results only until exit. Save Locally encrypts them for your Windows user and restores them next launch. No audio is stored. Turning saving off deletes disk history; Delete/Clear in Recent Dictations removes entries. Retried text/context is sent only to the provider you explicitly choose. Retry never pastes into the old app.",
        "Status" => StatusHelp(),
        "ConnectionTest" => ConnectionGuidance,
        "LocalDownload" => LocalDownloadGuidance,
        "AudioDevice" => "Windows Default follows your system microphone. Choose a specific device to keep using it. Refresh Microphones updates this list. Preview Applied Mic briefly opens the microphone using your saved settings; stop Preview or leave Audio to release it.",
        "LiveTranscription" => "On: shows a provisional local Whisper draft while you speak. Recent words may change. After Stop, the complete recording is transcribed for the final result. Drafts are never pasted or sent to AI.\n\nOff: uses only the waveform while recording and reduces CPU use. Slower models may update less often or pause live drafts.",
        "MicrophoneActivation" => "On Hotkey (default): the microphone is off while idle unless you enable Wake Phrases. Hold your shortcut and wait for the start tone before speaking.\n\nKeep Ready: keeps the microphone active for faster starts and a short pre-roll. Idle audio is not saved or sent to AI. Pause Microphone releases it in either mode. Preview temporarily opens the microphone independently.",
        "WakePhrases" => "Off by default. Enabling keeps your microphone listening locally. A lightweight speech detector triggers checks using the same Whisper model selected in AI & Models. Whisper does not run during silence; speech checks use more CPU and activation can take a moment, especially with larger models. No separate keyword model or account is needed.\n\nSay Hey SharpVoice for Ask AI, or Hey SharpVoice Dictate for dictation, then continue speaking immediately. Buffered audio preserves the beginning while Whisper checks the phrase. The longest matching phrase wins. Both phrases are editable (2–6 English words).\n\nRecording stops after 2 seconds of silence by default (adjustable 1–10). The matching hotkey stops it too; Escape cancels. The microphone pauses during readout and while SharpVoice is busy. Pause Microphone in the tray releases it. Silence and blank audio insert nothing.",
        "NoiseGateDb" => "Lower the gate (more negative) for quiet speech; raise it to reject room noise. Default: −65 dB. The meter shows your current input: blue passes the gate; gray does not. Start Preview to test the applied microphone. Apply Settings saves the new gate.",
        "SpeechModel" => "Whisper transcribes locally in English. Tiny is fastest; Base and Small favor accuracy with larger downloads and more processing. Q5 reduces download size with an accuracy tradeoff. Tiny Q5 is the default.",
        "SpeechProcessing" => "Standard uses the full Whisper encoder context. Fast processes recordings up to 15 seconds with a shorter encoder window, using the same model and the complete recording. It can improve turnaround but may change accuracy. Longer recordings and wake-phrase activation/resolution keep standard processing. Live local drafts follow this setting for hotkey recordings. AI cleanup settings and insertion delay are unchanged; network cleanup can still take over a second.",
        "Turnaround" => "Complete a dictation to see the last successful release-to-insertion dispatch duration and its preparation, transcription, AI/rules and insertion breakdown. This information stays only in memory. Clipboard insertion is faster than character-by-character typing. Lower the Insertion Delay if your target application allows it; AI requests and larger speech models add latency.",
        "Backend" => "Transcription Only skips AI cleanup and needs no account. ChatGPT uses your account sign-in. Claude requires Claude Code; Gemini supports Antigravity Google sign-in or a Google AI Studio API key. The API-key option has its own project quota/billing. Account plan limits apply. Antigravity may retain history. Local AI works offline after its model download.",
        "RefinementModel" => "Choose Auto for the recommended economical model, or type an exact model ID from your account. Refresh Models updates ChatGPT choices. The model actually used appears below after a request.",
        "MinimumAiWords" => "Dictation shorter than this word count is transcribed without AI cleanup. Default: 8 words. Set 0 to always refine. Ask AI and selected-text edits always use AI, regardless of this threshold.",
        "Mode" => "Auto matches supported apps; unrecognized apps use Prose. Choose a specific mode to keep the same writing style everywhere. Writing modes apply to normal dictation cleanup.",
        "CustomPrompt" => "The default dictation cleanup instructions are already built into SharpVoice; new installs pre-fill this field with them, and leaving it blank still uses them. Existing saved instructions are preserved. On fresh installs this is the editable cleanup template, used only for dictation cleanup; existing profiles retain additional-preference behavior. Add optional preferences here, such as punctuation, spelling or tone. Mode instructions add to these preferences. AI instructions do not apply when short dictation skips cleanup.",
        "PromptMode" => "Choose the writing mode whose instructions you want to edit below. This does not change the active Writing Mode. Blank instructions keep the default. Switching modes preserves your drafts; Apply Settings saves them.",
        "Hotkey" => "Hold to speak, release to transcribe and insert. If text is highlighted when you start, describe the change you want and AI edits that selection. Choose a preset or Record Shortcut, then press and release your preferred keys. Escape cancels shortcut recording. Apply Settings saves it.",
        "AskHotkey" => "Hold to speak a request; release for the AI result. By default every result inserts into the original field. General → Smart Ask AI optionally shows and speaks question answers; writing/edit requests still insert. Highlighted text supplies context. Ask AI always uses your configured AI provider. Choose a preset or record a separate shortcut; Escape cancels shortcut recording. Apply Settings saves it.",
        "Injection" => "Clipboard inserts the complete result at once (recommended). If an app blocks paste, try Typing, which sends paced Unicode keystrokes and is slower for long text. Keep the original text field focused until insertion finishes.",
        "PasteDelayMs" => "Wait before inserting text into the original app. Start with 100 milliseconds; increase this if the target app misses text. Keep the original text field focused until insertion finishes.",
        "MaxRecordingSeconds" => "Maximum length of one recording. Default: 120 seconds. Release your shortcut earlier to finish. Higher limits use more audio memory.",
        "StartOnLogin" => "On launches SharpVoice quietly in the tray when you sign in to Windows. Off means you launch it yourself. Apply Settings saves this choice. If you move the app, apply this setting again from its new location.",
        _ => throw new ArgumentException("Unknown Settings help key: " + key)
    };

    private unsafe void AddHelp(string key, string title, int sourceId, int y, int x = 647)
    {
        if (helpWindow == 0)
        {
            // Owned, nonactivating native window: no focus changes or credential/mic work.
            helpWindow = Create(0x08000088, "SharpVoice.Native", "Settings Help", 0x80000000, 0, 0, 0, 0, settingsWindow, 0);
            helpTitle = Create(0, "STATIC", "", 0x50000080, 18, 14, 380, 25, helpWindow, 1);
            helpBody = Create(0, "STATIC", "", 0x50000080, 18, 47, 380, 200, helpWindow, 2);
            Win32.SendMessageW(helpTitle, 0x30, (nuint)font, 0);
            Win32.SendMessageW(helpBody, 0x30, (nuint)font, 0);
        }
        var id = sourceId + 2000;
        var button = Control("BUTTON", "Help: " + title, 0x1000B, x, y + 1, 28, 28, id);
        helpEntries.Add(id, new(key, title, button, HelpText(key)));
        if (Win32.SetWindowSubclass(button, &HelpProcedure, 1, (nuint)id) == 0) throw new InvalidOperationException("Could not initialize Settings help button.");
    }
    private void ToggleHelp(int id)
    {
        if (!helpEntries.TryGetValue(id, out var entry) || Win32.IsWindowVisible(entry.Button) == 0) return;
        if (openHelpId == id) HideHelp();
        else ShowHelp(id, pinned: true);
    }
    private unsafe void ShowHelp(int id, bool pinned)
    {
        if (!helpEntries.TryGetValue(id, out var entry) || Win32.IsWindowVisible(entry.Button) == 0 || (!pinned && openHelpId != 0)) return;
        HideHelp(resetHover: false);
        Win32.GetWindowRect(entry.Button, out var anchor);
        var monitor = new HelpMonitorInfo { Size = (uint)sizeof(HelpMonitorInfo) };
        var handle = MonitorFromWindow(entry.Button, 2);
        if (GetMonitorInfoW(handle, &monitor) == 0) Win32.SystemParametersInfoW(0x30, 0, out monitor.Work, 0);
        var work = monitor.Work;
        var width = Math.Min(420, Math.Max(160, work.Right - work.Left - 16));
        var textRect = new Win32.Rect { Right = width - 36 };
        var dc = GetDC(helpWindow);
        if (dc == 0) throw new InvalidOperationException("Could not measure Settings help.");
        var previous = Win32.SelectObject(dc, font);
        try { Win32.DrawTextW(dc, entry.Content, -1, ref textRect, 0xC10); } // word wrap, calculate, literal ampersands
        finally { Win32.SelectObject(dc, previous); ReleaseDC(helpWindow, dc); }
        var height = textRect.Bottom + 65;
        var x = Math.Clamp(anchor.Right - width, work.Left + 8, Math.Max(work.Left + 8, work.Right - width - 8));
        var y = anchor.Bottom + 8;
        if (y + height > work.Bottom - 8) y = Math.Max(work.Top + 8, anchor.Top - height - 8);
        Win32.SetWindowTextW(helpWindow, "Help: " + entry.Title);
        Win32.SetWindowTextW(helpTitle, entry.Title);
        Win32.SetWindowTextW(helpBody, entry.Content);
        SetWindowPos(helpTitle, 0, 18, 14, width - 36, 25, 0x14);
        SetWindowPos(helpBody, 0, 18, 47, width - 36, textRect.Bottom, 0x14);
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, 14, 14);
        if (region != 0 && SetWindowRgn(helpWindow, region, 0) == 0) Win32.DeleteObject(region);
        shownHelpId = id;
        openHelpId = pinned ? id : 0;
        SetWindowPos(helpWindow, -1, x, y, width, height, 0x50); // TOPMOST, SHOWWINDOW, NOACTIVATE
        Win32.InvalidateRect(helpWindow, 0, 1);
    }
    private void CancelHelpTimer()
    {
        var previous = hoverTimerId; hoverTimerId = 0;
        if (previous != 0 && helpEntries.TryGetValue(hoveredHelpId, out var entry)) Win32.KillTimer(entry.Button, previous);
    }
    private void HideHelp(bool resetHover = true)
    {
        CancelHelpTimer();
        if (resetHover) hoveredHelpId = 0;
        openHelpId = shownHelpId = 0;
        if (helpWindow != 0) Win32.ShowWindow(helpWindow, 0);
    }
    private void UpdateHelp(string key, string text)
    {
        foreach (var pair in helpEntries)
        {
            if (pair.Value.Key != key || pair.Value.Content == text) continue;
            if (shownHelpId == pair.Key) HideHelp();
            helpEntries[pair.Key] = pair.Value with { Content = text };
            break;
        }
    }
    private void StyleHelp()
    {
        HideHelp();
        if (helpWindow == 0) return;
        var dark = theme.Dark ? 1 : 0;
        Win32.DwmSetWindowAttribute(helpWindow, 20, in dark, 4);
        Win32.InvalidateRect(helpWindow, 0, 1);
    }
    private bool HandleHelpMessage(nint hwnd, uint message, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (hwnd != helpWindow || helpWindow == 0) return false;
        if (message == 0x21) { result = 3; return true; } // MA_NOACTIVATE
        if (message == 0x138)
        {
            Win32.SetTextColor((nint)wParam, lParam == helpTitle ? theme.Accent : theme.Text);
            Win32.SetBkColor((nint)wParam, theme.Surface);
            result = surfaceBrush; return true;
        }
        if (message == 0x14)
        {
            Win32.GetClientRect(hwnd, out var area);
            Win32.FillRect((nint)wParam, area, borderBrush);
            area.Left++; area.Top++; area.Right--; area.Bottom--;
            Rounded((nint)wParam, area, surfaceBrush, 12);
            result = 1; return true;
        }
        return false;
    }
    internal bool HandleHelpInput(in Win32.Msg message)
    {
        var target = message.Hwnd;
        var helpButton = helpEntries.FirstOrDefault(x => x.Value.Button == target);
        if (message.Message is 0x100 or 0x101 && helpButton.Value is not null && message.WParam is 0xD or 0x20)
        {
            if (message.Message == 0x100 && ((long)message.LParam & (1L << 30)) == 0) ToggleHelp(helpButton.Key);
            return true;
        }
        if (message.Message == 0x100 && message.WParam == 0x1B && shownHelpId != 0) { HideHelp(); return true; }
        if ((message.Message == 0x100 && message.WParam == 9) || (message.Message is 0x201 or 0x204 && helpButton.Value is null && target != helpWindow && target != helpTitle && target != helpBody)) HideHelp();
        return false;
    }
    private void DisposeHelp()
    {
        HideHelp();
        if (helpWindow != 0) Win32.DestroyWindow(helpWindow);
        helpWindow = helpTitle = helpBody = 0;
        helpEntries.Clear();
    }
    private static string SettingsStatus(string text, bool isError)
    {
        if (isError) return text;
        var ready = text.IndexOf("Ready · ", StringComparison.Ordinal);
        return ready < 0 ? text : ready == 0 ? "Ready" : text[..ready].TrimEnd(' ', '·');
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint HelpProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var self = current;
        if (self is not null)
        {
            var input = new Win32.Msg { Hwnd = hwnd, Message = message, WParam = wParam, LParam = lParam };
            if (self.HandleHelpInput(in input)) return 0;
            if (message == 0x87 && wParam is 0xD or 0x20) return Win32.DefSubclassProc(hwnd, message, wParam, lParam) | 4;
            if (message == 8) self.HideHelp();
            if (message == 0x200 && self.hoveredHelpId != (int)data)
            {
                self.CancelHelpTimer();
                self.hoveredHelpId = (int)data;
                var tracking = new HelpMouseTracking { Size = (uint)sizeof(HelpMouseTracking), Flags = 2, Window = hwnd };
                if (TrackMouseEvent(&tracking) == 0) self.hoveredHelpId = 0;
                else if (self.openHelpId == 0) self.hoverTimerId = Win32.SetTimer(hwnd, ++self.nextHelpTimerId, 600, 0);
            }
            if (message == 0x113 && self.hoverTimerId != 0 && wParam == self.hoverTimerId)
            {
                self.CancelHelpTimer();
                if (self.hoveredHelpId == (int)data) self.ShowHelp((int)data, pinned: false);
                return 0;
            }
            if (message == 0x2A3 && self.hoveredHelpId == (int)data)
            {
                self.CancelHelpTimer(); self.hoveredHelpId = 0;
                if (self.openHelpId == 0) self.HideHelp();
            }
        }
        if (message == 0x82) Win32.RemoveWindowSubclass(hwnd, &HelpProcedure, id);
        return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
    }
    [StructLayout(LayoutKind.Sequential)] private struct HelpMonitorInfo { public uint Size; public Win32.Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct HelpMouseTracking { public uint Size, Flags; public nint Window; public uint HoverTime; }
    [LibraryImport("user32.dll")] private static partial nint MonitorFromWindow(nint window, uint flags);
    [LibraryImport("user32.dll")] private static unsafe partial int GetMonitorInfoW(nint monitor, HelpMonitorInfo* info);
    [LibraryImport("user32.dll")] private static unsafe partial int TrackMouseEvent(HelpMouseTracking* tracking);
    [LibraryImport("user32.dll")] private static partial int SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint window, nint dc);
}

