using System.Text;

namespace SharpVoice.Core;

public static class RefinementPolicy
{
    public static bool ShouldSkipCleanup(string transcript, AppConfig config, bool askAi = false, bool hasSelection = false) =>
        config.Backend != "none" && !askAi && !hasSelection && config.MinimumAiWords > 0 && CountWords(transcript) < config.MinimumAiWords;

    // Count whitespace-delimited tokens containing a letter or digit. Hyphens and apostrophes stay within a word.
    public static int CountWords(string text)
    {
        var count = 0;
        var counted = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune)) counted = false;
            else if (!counted && Rune.IsLetterOrDigit(rune)) { count++; counted = true; }
        }
        return count;
    }
}
