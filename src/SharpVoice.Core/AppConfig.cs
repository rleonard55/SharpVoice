using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpVoice.Core;

public sealed record AppConfig
{
    public const int MaximumConnectedTools = 4096;
    public DictionaryEntry[] Dictionary { get; init; } = [];
    public VoiceSnippet[] Snippets { get; init; } = [];
    public AppProfile[] Profiles { get; init; } = [];
    public string RecordingGesture { get; init; } = "hold";
    public int SilenceStopSeconds { get; init; }
    public bool PersistHistory { get; init; }
    public bool ConnectedAsk { get; init; }
    public string ConnectedCodexPath { get; init; } = "";
    public string[] ConnectedTools { get; init; } = [];
    public bool FastTranscription { get; init; }
    public bool LiveTranscription { get; init; } = true;
    public bool SmartAsk { get; init; }
    public string AnswerVoice { get; init; } = "en-US-AriaNeural";
    public string SpeechModel { get; init; } = "tiny.en-q5_1";
    public string Language { get; init; } = "en";
    public float NoiseGateDb { get; init; } = -65;
    public string AudioDeviceId { get; init; } = "";
    public string MicrophoneActivation { get; init; } = "on_hotkey";
    public bool WakePhrasesEnabled { get; init; }
    public string DictationWakePhrase { get; init; } = "Hey SharpVoice Dictate";
    public string AskWakePhrase { get; init; } = "Hey SharpVoice";
    public int WakeSilenceSeconds { get; init; } = 2;
    public int Hotkey { get; init; } = HotkeyDefinition.ControlAlt;
    public int AskHotkey { get; init; } = HotkeyDefinition.ControlAltShift;
    public string Backend { get; init; } = "none";
    public string RefinementModel { get; init; } = "auto";
    public int MinimumAiWords { get; init; } = 8;
    public string Mode { get; init; } = "auto";
    public bool StartOnLogin { get; init; }
    public bool AutomaticUpdates { get; init; } = true;
    public string UpdateAction { get; init; } = "install";
    public string Injection { get; init; } = "clipboard";
    public int PasteDelayMs { get; init; } = 100;
    public int MaxRecordingSeconds { get; init; } = 120;
    public string CustomPrompt { get; init; } = "";
    public string ProsePrompt { get; init; } = "";
    public string EmailPrompt { get; init; } = "";
    public string CodePrompt { get; init; } = "";
    public string CasualPrompt { get; init; } = "";

    public bool Equals(AppConfig? other) => other is not null
        && SpeechModel == other.SpeechModel
        && Language == other.Language
        && NoiseGateDb == other.NoiseGateDb
        && AudioDeviceId == other.AudioDeviceId
        && MicrophoneActivation == other.MicrophoneActivation
        && WakePhrasesEnabled == other.WakePhrasesEnabled && DictationWakePhrase == other.DictationWakePhrase
        && AskWakePhrase == other.AskWakePhrase && WakeSilenceSeconds == other.WakeSilenceSeconds
        && Hotkey == other.Hotkey
        && AskHotkey == other.AskHotkey
        && Backend == other.Backend
        && RefinementModel == other.RefinementModel
        && MinimumAiWords == other.MinimumAiWords
        && Mode == other.Mode
        && StartOnLogin == other.StartOnLogin
        && AutomaticUpdates == other.AutomaticUpdates && UpdateAction == other.UpdateAction
        && Injection == other.Injection
        && PasteDelayMs == other.PasteDelayMs
        && MaxRecordingSeconds == other.MaxRecordingSeconds
        && CustomPrompt == other.CustomPrompt
        && ProsePrompt == other.ProsePrompt
        && EmailPrompt == other.EmailPrompt
        && CodePrompt == other.CodePrompt
        && CasualPrompt == other.CasualPrompt
        && RecordingGesture == other.RecordingGesture
        && SilenceStopSeconds == other.SilenceStopSeconds
        && PersistHistory == other.PersistHistory
        && ConnectedAsk == other.ConnectedAsk && ConnectedCodexPath == other.ConnectedCodexPath && ConnectedTools.SequenceEqual(other.ConnectedTools)
        && FastTranscription == other.FastTranscription
        && LiveTranscription == other.LiveTranscription
        && SmartAsk == other.SmartAsk && AnswerVoice == other.AnswerVoice
        && Dictionary.SequenceEqual(other.Dictionary) && Snippets.SequenceEqual(other.Snippets) && Profiles.SequenceEqual(other.Profiles);
    public override int GetHashCode() => HashCode.Combine(SpeechModel, Backend, RefinementModel, Hotkey, AskHotkey, Mode, RecordingGesture, PersistHistory);

    public void Validate()
    {
        if (UpdateAction is not ("notify" or "download" or "install")) throw new ArgumentException("Choose a supported automatic update action.");
        WritingTools.Validate(this);
        WakePhrases.Validate(this);
        if (ConnectedCodexPath is null || ConnectedCodexPath.Length > 4096 || ConnectedTools is null || ConnectedTools.Length > MaximumConnectedTools || ConnectedTools.Any(x => x is null || x.Length > 512 || !x.Contains("::", StringComparison.Ordinal)) || ConnectedTools.Distinct(StringComparer.Ordinal).Count() != ConnectedTools.Length) throw new ArgumentException($"Choose at most {MaximumConnectedTools} distinct connected tools and a valid Codex location.");
        if (!AnswerVoices.All.Contains(AnswerVoice)) throw new ArgumentException("Choose a supported spoken-answer voice.");
        if (!SpeechModels.All.Contains(SpeechModel)) throw new ArgumentException("Choose a supported Whisper model from the list.");
        if (string.IsNullOrWhiteSpace(Language)) throw new ArgumentException("Language cannot be empty.");
        if (NoiseGateDb is < -90 or > 0 || !float.IsFinite(NoiseGateDb)) throw new ArgumentException("Noise gate must be between -90 and 0 dB.");
        if (!HotkeyDefinition.IsSupported(Hotkey)) throw new ArgumentException("Choose a function key or a shortcut with Ctrl, Alt, Shift or Win.");
        if (!HotkeyDefinition.IsSupported(AskHotkey)) throw new ArgumentException("Choose a supported Ask AI shortcut.");
        if (HotkeyDefinition.AreEquivalent(Hotkey, AskHotkey)) throw new ArgumentException("Dictation and Ask AI must use different shortcuts.");
        if (HotkeyDefinition.IsStrictSubset(AskHotkey, Hotkey)) throw new ArgumentException("Ask AI must include a different or an extra key; otherwise the dictation shortcut cannot start.");
        if (MicrophoneActivation is not ("on_hotkey" or "keep_ready")) throw new ArgumentException("Choose On Hotkey or Keep Ready microphone activation.");
        if (AudioDeviceId is null || AudioDeviceId.Length > 4096) throw new ArgumentException("Invalid microphone selection.");
        if (new[] { CustomPrompt, ProsePrompt, EmailPrompt, CodePrompt, CasualPrompt }.Any(x => x is null || x.Length > 32000)) throw new ArgumentException("Each prompt must be at most 32,000 characters.");
        if (Backend is not ("none" or "chatgpt_oauth" or "claude_code" or "gemini_antigravity" or "gemini_api" or "embedded")) throw new ArgumentException("Unknown refinement backend.");
        if (Backend == "embedded" && LocalAiModels.Find(RefinementModel) is null) throw new ArgumentException("Choose a supported local AI model from the list.");
        if (MinimumAiWords is < 0 or > 1000) throw new ArgumentException("Minimum words for AI must be between 0 and 1000. Use 0 to always refine.");
        if (Mode is not ("auto" or "prose" or "email" or "code" or "casual")) throw new ArgumentException("Unknown writing mode.");
        if (Injection is not ("typing" or "clipboard")) throw new ArgumentException("Choose typing or clipboard injection.");
        if (PasteDelayMs is < 0 or > 2000) throw new ArgumentException("Paste delay must be between 0 and 2000 ms.");
        if (MaxRecordingSeconds is < 1 or > 600) throw new ArgumentException("Recording limit must be between 1 and 600 seconds.");
        if (string.IsNullOrWhiteSpace(RefinementModel)) throw new ArgumentException("Refinement model cannot be empty.");
    }
}

public static class SpeechModels
{
    public static string[] All => ["tiny.en", "base.en", "small.en", "tiny.en-q5_1", "base.en-q5_1", "small.en-q5_1"];
}

public static class AppPaths
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sharpvoice");
    public static string Models => Path.Combine(Root, "models");
    public static string Config => Path.Combine(Root, "config.json");
    public static string Session => Path.Combine(Root, "session.json");
}

public static class ConfigManager
{
    public static AppConfig Load(string? path = null)
    {
        path ??= AppPaths.Config;
        if (!File.Exists(path)) return new AppConfig();
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        var hasBackend = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("backend", out _);
        var hasMicrophoneActivation = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("microphone_activation", out _);
        var hasAskHotkey = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("ask_hotkey", out _);
        var hasMinimumAiWords = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("minimum_ai_words", out _);
        var config = JsonSerializer.Deserialize(json, CoreJsonContext.Default.AppConfig)
            ?? throw new InvalidDataException("Configuration is empty.");
        var defaultAskHotkey = HotkeyDefinition.ControlAltShift;
        if (HotkeyDefinition.AreEquivalent(defaultAskHotkey, config.Hotkey) || HotkeyDefinition.IsStrictSubset(defaultAskHotkey, config.Hotkey))
            defaultAskHotkey = HotkeyDefinition.PrimaryKey(config.Hotkey) == 0x78 ? 0x79 : 0x78;
        // Generated init-only deserialization can supply default(T) for fields absent in older files.
        // Test JSON presence for AskHotkey: zero is also an explicitly supported Ctrl+Alt shortcut.
        // Optional strings use an empty value to mean default microphone / no extra instructions.
        config = config with
        {
            // Preserve old dictation choices that contain the new default Ask chord.
            Dictionary = config.Dictionary ?? [], Snippets = config.Snippets ?? [], Profiles = config.Profiles ?? [],
            ConnectedTools = config.ConnectedTools ?? [], ConnectedCodexPath = config.ConnectedCodexPath ?? "",
            AnswerVoice = string.IsNullOrWhiteSpace(config.AnswerVoice) ? "en-US-AriaNeural" : config.AnswerVoice,
            RecordingGesture = document.RootElement.TryGetProperty("recording_gesture", out _) ? config.RecordingGesture : "hold",
            LiveTranscription = document.RootElement.TryGetProperty("live_transcription", out _) ? config.LiveTranscription : true,
            AutomaticUpdates = document.RootElement.TryGetProperty("automatic_updates", out _) ? config.AutomaticUpdates : true,
            UpdateAction = document.RootElement.TryGetProperty("update_action", out _) ? config.UpdateAction : "install",
            Backend = hasBackend ? config.Backend : "none",
            AskHotkey = hasAskHotkey ? config.AskHotkey : defaultAskHotkey,
            MinimumAiWords = hasMinimumAiWords ? config.MinimumAiWords : 8,
            MicrophoneActivation = hasMicrophoneActivation ? config.MicrophoneActivation : "on_hotkey",
            DictationWakePhrase = document.RootElement.TryGetProperty("dictation_wake_phrase", out _) ? config.DictationWakePhrase : "Hey SharpVoice Dictate",
            AskWakePhrase = document.RootElement.TryGetProperty("ask_wake_phrase", out _) ? config.AskWakePhrase : "Hey SharpVoice",
            WakeSilenceSeconds = document.RootElement.TryGetProperty("wake_silence_seconds", out _) ? config.WakeSilenceSeconds : 2,
            AudioDeviceId = config.AudioDeviceId ?? "",
            CustomPrompt = config.CustomPrompt ?? "",
            ProsePrompt = config.ProsePrompt ?? "",
            EmailPrompt = config.EmailPrompt ?? "",
            CodePrompt = config.CodePrompt ?? "",
            CasualPrompt = config.CasualPrompt ?? ""
        };
        // Retired Groq profiles remain usable without carrying their model ID to ChatGPT.
        if (config.Backend == "groq") config = config with { Backend = "chatgpt_oauth", RefinementModel = "auto" };
        // Migrate the prototype's original default; preserve explicitly chosen other models.
        if (config.Backend is "none" or "chatgpt_oauth" && config.RefinementModel == "gpt-5.1-codex")
            config = config with { RefinementModel = "auto" };
        config.Validate();
        return config;
    }

    public static void Save(AppConfig config, string? path = null)
    {
        config.Validate();
        path ??= AppPaths.Config;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(config, CoreJsonContext.Default.AppConfig));
        File.Move(temporary, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(AppConfig))]
public partial class CoreJsonContext : JsonSerializerContext;
