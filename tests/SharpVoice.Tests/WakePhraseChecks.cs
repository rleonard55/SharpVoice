using SharpVoice.Core;

internal static class WakePhraseChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static Task Configuration()
    {
        var config = new AppConfig();
        Check(!config.WakePhrasesEnabled && config.AskWakePhrase == "Hey SharpVoice" && config.DictationWakePhrase == "Hey SharpVoice Dictate" && config.WakeSilenceSeconds == 2, "New installation wake defaults changed.");
        var path = Path.Combine(Path.GetTempPath(), "sharpvoice-wake-config-" + Guid.NewGuid() + ".json");
        try
        {
            var custom = config with { WakePhrasesEnabled = true, AskWakePhrase = "Hello Computer", DictationWakePhrase = "Hello Computer Write", WakeSilenceSeconds = 3 };
            ConfigManager.Save(custom, path); Check(ConfigManager.Load(path) == custom, "Explicit wake choices were lost.");
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            foreach (var name in new[] { "wake_phrases_enabled", "ask_wake_phrase", "dictation_wake_phrase", "wake_silence_seconds" }) json.Remove(name);
            File.WriteAllText(path, json.ToJsonString()); var legacy = ConfigManager.Load(path);
            Check(!legacy.WakePhrasesEnabled && legacy.AskWakePhrase == "Hey SharpVoice" && legacy.WakeSilenceSeconds == 2, "Legacy configuration enabled wake or lost defaults.");
            foreach (var invalid in new[] { config with { AskWakePhrase = "" }, config with { AskWakePhrase = "one" }, config with { AskWakePhrase = null! }, config with { AskWakePhrase = "Hey 世界" }, config with { AskWakePhrase = "Hey Sharp Voice Dictate" }, config with { WakeSilenceSeconds = 0 }, config with { WakeSilenceSeconds = 11 } })
            {
                try { invalid.Validate(); throw new Exception("Invalid wake configuration accepted."); } catch (ArgumentException) { }
            }
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }
    public static Task Prefixes()
    {
        var c = new AppConfig();
        foreach (var row in new (string Input, VoiceMode Detected, VoiceMode? Confirmed, VoiceMode Mode, string Text)[]
        {
            ("“Hey SharpVoice Dictate: Hello there.”", VoiceMode.AskAi, null, VoiceMode.Dictation, "Hello there."),
            ("Hey, Sharp Voice dictate: Hello there.", VoiceMode.AskAi, null, VoiceMode.Dictation, "Hello there."),
            ("Hey SharpVoice, what is two plus two?", VoiceMode.AskAi, null, VoiceMode.AskAi, "what is two plus two?"),
            ("Hey SharpVoice", VoiceMode.AskAi, null, VoiceMode.AskAi, ""),
            ("[BLANK_AUDIO]", VoiceMode.AskAi, null, VoiceMode.AskAi, ""),
            ("Hello there.", VoiceMode.AskAi, VoiceMode.Dictation, VoiceMode.Dictation, "Hello there."),
            ("Explain the phrase hey sharpvoice dictate.", VoiceMode.AskAi, null, VoiceMode.AskAi, "Explain the phrase hey sharpvoice dictate."),
            ("Hey SharpVoice Dictate [BLANK_AUDIO]", VoiceMode.AskAi, null, VoiceMode.Dictation, "")
        })
        {
            var result = WakePhrases.Resolve(c, row.Input, row.Detected, row.Confirmed);
            Check(result.Mode == row.Mode && result.Text == row.Text, "Incorrect wake prefix resolution: " + row.Input);
        }
        var reverse = c with { AskWakePhrase = "Hello Computer Answer", DictationWakePhrase = "Hello Computer" };
        Check(WakePhrases.Resolve(reverse, "Hello Computer Answer, test", VoiceMode.Dictation, null) == new WakeTranscript(VoiceMode.AskAi, "test"), "Longest-prefix resolution assumed dictation is always longer.");
        var session = new WakeSession(new(VoiceMode.AskAi, 16000, 1), c);
        Check(!session.Confirm(new(VoiceMode.Dictation, 16000, 2)), "Old device confirmed mode.");
        Check(!session.Confirm(new(VoiceMode.Dictation, 40000, 1)), "Later request phrase changed mode.");
        Check(session.Confirm(new(VoiceMode.Dictation, 17000, 1)), "Same-onset longer phrase did not confirm.");
        Check(session.Resolve("Hello there").Mode == VoiceMode.Dictation, "Native confirmation lost when Whisper omits phrase.");
        Check(!session.HandlePress(VoiceMode.AskAi) && session.HandlePress(VoiceMode.Dictation) && !session.HandlePress(VoiceMode.Dictation), "Wake recording stop was duplicated or promoted.");
        Check(session.ConsumeRelease() && !session.ConsumeRelease(), "Wake stop did not consume exactly one release.");
        return Task.CompletedTask;
    }
    public static Task Lifecycle()
    {
        var allowed = new WakeConditions(true, true, false, false, false, false, true, false, 1000, 0);
        Check(allowed.CanListen && allowed.CanActivate, "Ready wake listener did not arm.");
        foreach (var suspended in new[] { allowed with { Enabled = false }, allowed with { Ready = false }, allowed with { Paused = true }, allowed with { SpeechSuspended = true }, allowed with { AuthBusy = true }, allowed with { EditorBusy = true }, allowed with { Idle = false }, allowed with { RearmAt = 1750 } })
            Check(!suspended.CanListen && !suspended.CanActivate, "Busy, paused or stale wake activation accepted.");
        var confirming = allowed with { Idle = false, Confirming = true };
        Check(confirming.CanListen && !confirming.CanActivate, "Longer phrase confirmation enabled a second operation.");
        return Task.CompletedTask;
    }
}
