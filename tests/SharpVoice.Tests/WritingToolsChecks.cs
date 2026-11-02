using SharpVoice.Core;

internal static class WritingToolsChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static Task TextRules()
    {
        var config = new AppConfig { Dictionary = [new("sharp voice", "SharpVoice"), new("bob", "Robert"), new("sharp", "Sharp")], Snippets = [new("insert signature", "Regards,\nLeon")] };
        Check(WritingTools.Correct("SHARP VOICE, bob! bobcat _bob bob2", config.Dictionary) == "SharpVoice, Robert! bobcat _bob bob2", "Literal whole-word longest matching failed.");
        Check(WritingTools.Correct("a b", [new("a", "b"), new("b", "c")]) == "b c", "Dictionary recursively rewrote replacements.");
        Check(WritingTools.Correct("ébob bobé bob", [new("bob", "x")]) == "ébob bobé x", "Unicode boundaries failed.");
        Check(WritingTools.Prepare(" Insert signature! ", config, false, false) == new PreparedDictation("Regards,\nLeon", true), "Exact snippet was not expanded verbatim.");
        Check(WritingTools.Prepare("sharp voice", config, false, false).Text == "sharp voice", "Preparation applied dictionary rules before the single final correction pass.");
        Check(!WritingTools.Prepare("please insert signature", config, false, false).IsSnippet, "Partial phrase triggered a snippet.");
        Check(WritingTools.Prepare("sharp voice", config, true, false).Text == "sharp voice" && !WritingTools.Prepare("insert signature", config, false, true).IsSnippet, "Rules changed Ask AI or selected editing instructions.");
        Check(WritingTools.VocabularyPrompt(config.Dictionary).Contains("SharpVoice"), "Preferred vocabulary is missing.");
        Check(WritingTools.VocabularyPrompt(Enumerable.Range(0, 500).Select(i => new DictionaryEntry("word" + i, new string('x', 20) + i)).ToArray()).Length <= 512, "Whisper vocabulary prompt is unbounded.");
        var overflow = false;
        try { WritingTools.Correct(string.Join(' ', Enumerable.Repeat("a", 500)), [new("a", new string('x', 512))]); }
        catch (InvalidOperationException) { overflow = true; }
        Check(overflow, "Unbounded dictionary expansion was accepted.");
        return Task.CompletedTask;
    }
    public static Task ProfilesAndPersistence()
    {
        var config = new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "global-model", Dictionary = [new("sharp voice", "SharpVoice")], Snippets = [new("signature", "Leon")], RecordingGesture = "toggle", SilenceStopSeconds = 3, PersistHistory = true,
            Profiles = [new("Work Mail", "OUTLOOK.exe", "claude_code", "haiku", "email", 0, "Keep it brief.")] };
        config.Validate();
        var mail = WritingTools.Resolve(config, "outlook");
        Check(mail.Backend == "claude_code" && mail.RefinementModel == "haiku" && mail.Mode == "email" && mail.MinimumAiWords == 0 && mail.CustomPrompt.Contains("Keep it brief."), "Profile overrides missing.");
        Check(WritingTools.Resolve(config, "notoutlook") == config && config.Backend == "chatgpt_oauth", "Profile matched partially or mutated global settings.");
        var inherited = WritingTools.Resolve(config with { Profiles = [new("Editor", "code")] }, "Code.exe");
        Check(inherited.Backend == config.Backend && inherited.RefinementModel == "global-model", "Profile did not inherit global model.");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            ConfigManager.Save(config, path); Check(ConfigManager.Load(path) == config, "New writing preferences did not round-trip.");
            File.WriteAllText(path, "{\"speech_model\":\"tiny.en-q5_1\",\"refinement_model\":\"auto\",\"language\":\"en\",\"mode\":\"auto\",\"injection\":\"clipboard\",\"noise_gate_db\":-65,\"max_recording_seconds\":120,\"paste_delay_ms\":100}");
            var old = ConfigManager.Load(path);
            Check(old.Dictionary.Length == 0 && old.Profiles.Length == 0 && old.RecordingGesture == "hold" && old.SilenceStopSeconds == 0 && !old.PersistHistory, "Legacy configuration changed default behavior.");
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }
    public static Task Validation()
    {
        var invalid = new[] {
            new AppConfig { Dictionary = [new("bob", "a"), new("BOB", "b")] },
            new AppConfig { Snippets = [new("hello!", "a"), new("hello", "b")] },
            new AppConfig { Profiles = [new("a", "code.exe"), new("b", "CODE")] },
            new AppConfig { Profiles = [new("a", "C:\\code.exe")] },
            new AppConfig { Profiles = [new("a", "code", "embedded", "bad-model")] },
            new AppConfig { RecordingGesture = "bad" }, new AppConfig { SilenceStopSeconds = -1 }
        };
        foreach (var value in invalid)
        {
            try { value.Validate(); throw new Exception("Invalid writing preference was accepted."); }
            catch (ArgumentException) { }
        }
        return Task.CompletedTask;
    }
}
