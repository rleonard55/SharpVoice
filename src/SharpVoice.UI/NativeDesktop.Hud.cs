using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private void ShowHud()
    {
        // Reassert HWND_TOPMOST on every show; preserve geometry and typing focus.
        // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW.
        SetWindowPos(hudWindow, -1, 0, 0, 0, 0, 0x53);
    }

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
