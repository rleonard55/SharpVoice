using System.Globalization;
using System.Text;

namespace SharpVoice.Core;

public sealed record DictionaryEntry(string Spoken, string Preferred);
public sealed record VoiceSnippet(string Trigger, string Text);
public sealed record AppProfile(string Name, string ProcessName, string Backend = "inherit", string Model = "", string Mode = "inherit", int? MinimumAiWords = null, string Instructions = "");
public sealed record PreparedDictation(string Text, bool IsSnippet);

public static class WritingTools
{
    public const int MaximumText = 200_000;
    public static string NormalizeTrigger(string text) => text.Trim().TrimEnd('.', '!', '?', ',', ';', ':', '…').TrimEnd();
    public static string NormalizeProcess(string text)
    {
        var name = text.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
    public static AppProfile? FindProfile(AppConfig config, string process) => config.Profiles.FirstOrDefault(p => NormalizeProcess(p.ProcessName).Equals(NormalizeProcess(process), StringComparison.OrdinalIgnoreCase));
    public static AppConfig Resolve(AppConfig config, string process)
    {
        var profile = FindProfile(config, process);
        if (profile is null) return config;
        var changedProvider = profile.Backend != "inherit" && profile.Backend != config.Backend;
        return config with
        {
            Backend = profile.Backend == "inherit" ? config.Backend : profile.Backend,
            RefinementModel = profile.Model.Length != 0 ? profile.Model : changedProvider ? "auto" : config.RefinementModel,
            Mode = profile.Mode == "inherit" ? config.Mode : profile.Mode,
            MinimumAiWords = profile.MinimumAiWords ?? config.MinimumAiWords,
            CustomPrompt = config.CustomPrompt + (profile.Instructions.Length == 0 ? "" : "\n\nApp preferences:\n" + profile.Instructions)
        };
    }
    public static PreparedDictation Prepare(string transcript, AppConfig config, bool askAi, bool hasSelection)
    {
        if (askAi || hasSelection) return new(transcript, false);
        var trigger = NormalizeTrigger(transcript);
        var snippet = config.Snippets.FirstOrDefault(s => NormalizeTrigger(s.Trigger).Equals(trigger, StringComparison.OrdinalIgnoreCase));
        return snippet is null ? new(transcript, false) : new(snippet.Text, true);
    }
    public static string Correct(string text, IReadOnlyList<DictionaryEntry> entries)
    {
        if (entries.Count == 0) return text;
        var groups = entries.Select(e => (Spoken: e.Spoken.Trim(), e.Preferred)).OrderByDescending(e => e.Spoken.Length)
            .GroupBy(e => char.ToUpperInvariant(e.Spoken[0])).ToDictionary(group => group.Key, group => group.ToArray());
        var output = new StringBuilder();
        for (var i = 0; i < text.Length;)
        {
            var matched = false;
            if (!WordBefore(text, i) && groups.TryGetValue(char.ToUpperInvariant(text[i]), out var candidates))
                foreach (var entry in candidates)
                    if (text.AsSpan(i).StartsWith(entry.Spoken, StringComparison.OrdinalIgnoreCase) && !WordAt(text, i + entry.Spoken.Length))
                    { output.Append(entry.Preferred); i += entry.Spoken.Length; matched = true; break; }
            if (!matched) output.Append(text[i++]);
            if (output.Length > MaximumText) throw new InvalidOperationException("Dictionary expansion is too long. The transcript is retained; nothing was inserted.");
        }
        return output.ToString();
    }
    private static bool WordBefore(string text, int index)
    {
        if (index == 0) return false;
        index--;
        if (index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1])) index--;
        return WordAt(text, index);
    }
    private static bool WordAt(string text, int index) => index < text.Length && Rune.TryGetRuneAt(text, index, out var rune)
        && (Rune.IsLetterOrDigit(rune) || rune.Value == '_' || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark);
    public static string VocabularyPrompt(IReadOnlyList<DictionaryEntry> entries)
    {
        var text = new StringBuilder();
        foreach (var word in entries.Select(e => e.Preferred).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (text.Length + word.Length + 2 > 512) break;
            if (text.Length != 0) text.Append(", ");
            text.Append(word);
        }
        return text.ToString();
    }
    public static void Validate(AppConfig config)
    {
        if (config.RecordingGesture is not ("hold" or "toggle")) throw new ArgumentException("Choose Hold to Talk or Toggle Recording.");
        if (config.SilenceStopSeconds is < 0 or > 30) throw new ArgumentException("Silence auto-stop must be 0–30 seconds; 0 disables it.");
        if (config.Dictionary is null || config.Snippets is null || config.Profiles is null) throw new ArgumentException("Writing rule lists cannot be null.");
        if (config.Dictionary.Length > 512 || config.Snippets.Length > 256 || config.Profiles.Length > 128) throw new ArgumentException("Use at most 512 dictionary entries, 256 snippets and 128 app profiles.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in config.Dictionary)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Spoken) || entry.Spoken.Length > 128 || string.IsNullOrWhiteSpace(entry.Preferred) || entry.Preferred.Length > 512) throw new ArgumentException("Dictionary entries need a spoken phrase (1–128 characters) and preferred spelling (1–512 characters).");
            if (!keys.Add(entry.Spoken.Trim())) throw new ArgumentException("Each dictionary phrase must be unique.");
        }
        keys.Clear();
        foreach (var snippet in config.Snippets)
        {
            if (snippet is null || string.IsNullOrWhiteSpace(snippet.Trigger) || snippet.Trigger.Length > 128 || NormalizeTrigger(snippet.Trigger).Length == 0 || string.IsNullOrWhiteSpace(snippet.Text) || snippet.Text.Length > 32_768) throw new ArgumentException("Snippets need a spoken trigger (1–128 characters) and text (1–32,768 characters).");
            if (!keys.Add(NormalizeTrigger(snippet.Trigger))) throw new ArgumentException("Each snippet trigger must be unique, ignoring casing and terminal punctuation.");
        }
        keys.Clear();
        foreach (var profile in config.Profiles)
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80 || string.IsNullOrWhiteSpace(profile.ProcessName) || profile.ProcessName.Length > 128 || profile.ProcessName.IndexOfAny(['/', '\\', ':', '*', '?']) >= 0 || NormalizeProcess(profile.ProcessName).Length == 0) throw new ArgumentException("App profiles need a name and an executable basename, such as Code.exe (no paths or wildcards).");
            if (config.CustomPrompt is not null && profile.Instructions is not null && profile.Instructions.Length != 0 && config.CustomPrompt.Length + profile.Instructions.Length + 22 > 32000) throw new ArgumentException("Combined global and app instructions must be at most 32,000 characters.");
            if (!keys.Add(NormalizeProcess(profile.ProcessName))) throw new ArgumentException("Each app can have only one profile.");
            if (profile.Backend is not ("inherit" or "none" or "chatgpt_oauth" or "embedded" or "claude_code" or "gemini_antigravity" or "gemini_api") || profile.Model is null || profile.Model.Length > 200 || profile.Model.Length > 0 && string.IsNullOrWhiteSpace(profile.Model) || profile.Mode is not ("inherit" or "auto" or "prose" or "email" or "code" or "casual") || profile.MinimumAiWords is < 0 or > 1000 || profile.Instructions is null || profile.Instructions.Length > 32000) throw new ArgumentException("Choose valid app profile provider, model, mode, threshold and instructions.");
            if ((profile.Backend == "embedded" || profile.Backend == "inherit" && config.Backend == "embedded") && profile.Model.Length != 0 && LocalAiModels.Find(profile.Model) is null) throw new ArgumentException("Choose a supported local model for this profile, or leave it blank for Auto.");
        }
    }
}
