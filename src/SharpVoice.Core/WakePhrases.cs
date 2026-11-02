using System.Text.RegularExpressions;

namespace SharpVoice.Core;

public readonly record struct WakeDetection(VoiceMode Mode, long StartSample, long Epoch);
public sealed record WakePreparation(string State, long DownloadedBytes, long? TotalBytes, string Message);
public readonly record struct WakeTranscript(VoiceMode Mode, string Text);

public static partial class WakePhrases
{
    public static string VocabularyPrompt(AppConfig config) => $"{config.AskWakePhrase}. {config.DictationWakePhrase}.";
    public static bool TryDetect(AppConfig config, string transcript, out VoiceMode mode)
    {
        var text = TranscriptionText.Clean(transcript);
        foreach (var candidate in new[] { VoiceMode.Dictation, VoiceMode.AskAi }.OrderByDescending(m => Normalize(Phrase(config, m)).Length))
            if (Prefix(config, candidate, text).Success) { mode = candidate; return true; }
        mode = default; return false;
    }
    private static Match Prefix(AppConfig config, VoiceMode mode, string text)
    {
        var words = Normalize(Phrase(config, mode)).Split(' ');
        var pattern = "^[\\s\"“‘']*";
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0) pattern += words[i - 1] == "sharp" && words[i] == "voice" ? @"[\s\p{P}]*" : @"[\s\p{P}]+";
            pattern += Regex.Escape(words[i]);
        }
        return Regex.Match(text, pattern + @"(?![a-z'])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
    public static string Normalize(string phrase) => string.Join(' ', Brand().Replace(phrase, "sharp voice").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    public static string Phrase(AppConfig config, VoiceMode mode) => mode == VoiceMode.Dictation ? config.DictationWakePhrase : config.AskWakePhrase;
    public static bool IsLonger(AppConfig config, VoiceMode shorter, VoiceMode longer)
        => Normalize(Phrase(config, longer)).StartsWith(Normalize(Phrase(config, shorter)) + " ", StringComparison.Ordinal);
    public static void Validate(AppConfig config)
    {
        foreach (var phrase in new[] { config.DictationWakePhrase, config.AskWakePhrase })
            if (phrase is null || phrase.Length > 80 || !ValidPhrase().IsMatch(phrase.Trim()) || phrase.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is < 2 or > 6)
                throw new ArgumentException("Use 2–6 English words, up to 80 characters, for each wake phrase.");
        if (Normalize(config.DictationWakePhrase) == Normalize(config.AskWakePhrase)) throw new ArgumentException("Choose different dictation and Ask AI wake phrases.");
        if (config.WakeSilenceSeconds is < 1 or > 10) throw new ArgumentException("Wake silence stop must be 1–10 seconds.");
    }
    public static WakeTranscript Resolve(AppConfig config, string transcript, VoiceMode detectedMode, VoiceMode? confirmedMode)
    {
        var text = TranscriptionText.Clean(transcript);
        var order = new[] { VoiceMode.Dictation, VoiceMode.AskAi }.OrderByDescending(m => Normalize(Phrase(config, m)).Length);
        foreach (var mode in order)
        {
            var match = Prefix(config, mode, text);
            if (!match.Success) continue;
            var resolved = confirmedMode is VoiceMode longer && IsLonger(config, mode, longer) ? longer : mode;
            var body = text[match.Length..].TrimStart(' ', ',', '.', ':', ';', '!', '?', '—', '–').Trim();
            var first = text.TrimStart().FirstOrDefault();
            var closing = first switch { '“' => '”', '‘' => '’', '"' => '"', '\'' => '\'', _ => '\0' };
            if (closing != '\0' && body.EndsWith(closing)) body = body[..^1].TrimEnd();
            return new(resolved, TranscriptionText.Clean(body));
        }
        return new(confirmedMode ?? detectedMode, text);
    }
    [GeneratedRegex(@"^[A-Za-z]+(?:'[A-Za-z]+)?(?: +[A-Za-z]+(?:'[A-Za-z]+)?)+$", RegexOptions.CultureInvariant)] private static partial Regex ValidPhrase();
    [GeneratedRegex(@"\bsharpvoice\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex Brand();
}
