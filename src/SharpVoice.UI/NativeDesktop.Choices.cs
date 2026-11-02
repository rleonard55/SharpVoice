namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    // Display copy is independent of persisted IDs and prompt/provider routing.
    private static readonly Dictionary<string, (string Id, string Label)[]> choices = new()
    {
        ["SpeechProcessing"] = [("standard", "Standard (Full Context)"), ("fast", "Fast (Short Dictation)")],
        ["LiveTranscription"] = [("on", "On (Local Draft)"), ("off", "Off (Waveform Only)")],
        ["MicrophoneActivation"] = [("on_hotkey", "On Hotkey (Default)"), ("keep_ready", "Keep Ready (Faster Start)")],
        ["SpeechModel"] = [
            ("tiny.en", "Tiny (English, Fastest)"), ("base.en", "Base (English, Balanced)"), ("small.en", "Small (English, More Accurate)"),
            ("tiny.en-q5_1", "Tiny Q5 (English, Smaller Download)"), ("base.en-q5_1", "Base Q5 (English, Smaller Download)"), ("small.en-q5_1", "Small Q5 (English, Smaller Download)")],
        ["Backend"] = [("none", "None (Transcription Only)"), ("chatgpt_oauth", "ChatGPT (Account Sign-In)"), ("embedded", "Local AI (Offline Cleanup)"),
            ("claude_code", "Claude (Claude Code Sign-In)"), ("gemini_antigravity", "Gemini (Antigravity Sign-In)"), ("gemini_api", "Gemini (AI Studio API Key)")],
        ["Mode"] = [("auto", "Auto (Match the App)"), ("prose", "Prose (General Writing)"), ("email", "Email (Professional Messages)"),
            ("code", "Code (Technical Writing)"), ("casual", "Casual (Chat Messages)")],
        ["PromptMode"] = [("prose", "Prose (General Writing)"), ("email", "Email (Professional Messages)"),
            ("code", "Code (Technical Writing)"), ("casual", "Casual (Chat Messages)")],
        ["Injection"] = [("typing", "Typing (Compatibility Mode)"), ("clipboard", "Clipboard (Recommended)")],
        ["RecordingGesture"] = [("hold", "Hold to Talk (Default)"), ("toggle", "Toggle (Press to Start / Stop)")],
        ["PersistHistory"] = [("memory", "In Memory (Until Exit)"), ("saved", "Save Locally (Encrypted)")],
        ["StartOnLogin"] = [("Off", "Off (Launch Manually)"), ("On", "On (Start in Tray)")]
    };

    private void AddChoice(string label, string key, int y, string value, int id) =>
        AddCombo(label, key, y, choices[key].Select(x => x.Label).ToArray(), ChoiceLabel(key, value), id);

    private static string ChoiceLabel(string key, string id) => choices[key].FirstOrDefault(x => x.Id == id).Label ?? id;
    private string SelectedChoice(string key)
    {
        var text = Text(fields[key]);
        return choices[key].FirstOrDefault(x => x.Label == text).Id ?? text;
    }
}
