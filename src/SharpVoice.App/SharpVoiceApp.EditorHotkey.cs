using SharpVoice.Core;
using SharpVoice.Platform.Windows;

namespace SharpVoice.App;

internal sealed partial class SharpVoiceApp
{
    private readonly EditorHotkeyGate editorHotkey = new();
    private WindowTarget? editorHotkeyTarget;
    private string? editorHotkeyClient;

    private bool EditorHotkeyFocused() => editorHotkeyTarget is { } expected && environment.CaptureTarget() is var current
        && current.Handle == expected.Handle && current.ProcessId == expected.ProcessId;

    private bool TryEditorHotkey(VoiceMode mode)
    {
        if (mode == VoiceMode.AskAi)
        {
            if (editorHotkey.Pending && editorHotkey.Mode != VoiceMode.AskAi) CancelEditorHotkey();
            else if (editorHotkey.Active && editorHotkey.Mode == VoiceMode.Dictation && editorSession.Busy && phase is Phase.Arming or Phase.Recording)
            {
                var expected = editorHotkeyTarget!;
                Cancel(); var stopped = work; var id = voiceStart.Begin(VoiceMode.AskAi);
                desktop.SetRecordingGestureActive(true);
                preloads.Add(ResumeAskAfterEditorAsync(stopped, id, expected));
                return true;
            }
        }
        if (editorHotkey.Pending || editorHotkey.Active && workspaceAsking && phase is Phase.Arming or Phase.Recording) return true;
        if (editorSession.Busy || phase != Phase.Idle || desktop.IsPaused || !environment.EditorIntegration) return false;
        var window = environment.CaptureTarget();
        if (!EditorPlugins.IsSupportedEditor(window.ProcessName)) return false;
        if (!microphoneReady || authBusy || WritingTools.Resolve(config, window.ProcessName).Backend == "none")
        {
            desktop.SetStatus("Editor voice shortcuts need a ready microphone and an AI provider. Review Audio and AI & Models.", true, isError: true);
            desktop.ResetRecordingGesture(); return true;
        }
        var client = editorBridge?.ConnectedClientId;
        if (client is null)
        {
            desktop.SetStatus($"Open or update the SharpVoice plugin in the focused editor, then retry {(mode == VoiceMode.AskAi ? "Ask AI" : "Dictation")}. No text was inserted.", true, isError: true);
            desktop.ResetRecordingGesture(); return true;
        }
        StopAnswerSpeech(); desktop.HideAnswer();
        var request = editorHotkey.Begin(DateTimeOffset.UtcNow, mode); editorHotkeyTarget = window; editorHotkeyClient = client;
        LastUpdateActivity = Environment.TickCount64;
        desktop.SetActive(true); desktop.SetStatus("Connecting to the editor · Wait for the start tone…", true);
        preloads.Add(SendEditorHotkeyAsync(request, client, mode));
        return true;
    }
    private async Task SendEditorHotkeyAsync(string request, string client, VoiceMode mode)
    {
        try { await editorBridge!.SendAsync(new("hotkey_start", request, Mode: mode == VoiceMode.AskAi ? "ask" : "dictate", ClientId: client)); }
        catch (Exception)
        {
            desktop.Post(() =>
            {
                if (!editorHotkey.Pending || editorHotkey.Id != request) return;
                CancelEditorHotkey(); desktop.ResetRecordingGesture();
                desktop.SetStatus("Editor connection unavailable · Reopen the SharpVoice plugin and retry. No text was inserted.", true, isError: true);
            });
        }
    }
    private void CancelEditorHotkey()
    {
        var active = editorHotkey.Active; var id = editorHotkey.Id; var pending = editorHotkey.Pending; var client = editorHotkeyClient;
        editorHotkey.Cancel(); editorHotkeyTarget = null; editorHotkeyClient = null;
        if (active) desktop.SetActive(false);
        if (pending)
        {
            SendEditor(new("hotkey_cancel", id, ClientId: client));
            desktop.SetActive(false);
        }
    }
    private async Task ResumeAskAfterEditorAsync(Task stopped, long id, WindowTarget expected)
    {
        try { await stopped.ConfigureAwait(false); } catch (Exception) { }
        desktop.Post(() =>
        {
            if (lifetime.IsCancellationRequested || !voiceStart.TryComplete(id, out var mode)) return;
            var current = environment.CaptureTarget();
            if (current.Handle != expected.Handle || current.ProcessId != expected.ProcessId || phase != Phase.Idle)
            { desktop.SetStatus("Editor focus changed while recording stopped. Press Ask AI again."); desktop.ResetRecordingGesture(); return; }
            if (!TryEditorHotkey(mode)) RequestVoicePressed(mode);
        });
    }
}
