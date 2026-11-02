using System.Text.RegularExpressions;

namespace SharpVoice.Core;

/// <summary>Recognition annotations are not spoken words and must never become an AI request.</summary>
public static partial class TranscriptionText
{
    public static string Clean(string text)
    {
        var cleaned = NonSpeech().Replace(text, "");
        return (cleaned == text ? text : ExtraSpaces().Replace(cleaned, " ")).Trim();
    }

    [GeneratedRegex(@"\[(?:BLANK_AUDIO|NO_SPEECH|SILENCE|MUSIC|APPLAUSE|LAUGHTER)\]|\((?:silence|music)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonSpeech();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex ExtraSpaces();
}
