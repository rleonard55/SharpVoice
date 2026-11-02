namespace SharpVoice.Core;

public static class TranscriptDraft
{
    public static string Tail(string text, int length)
    {
        if (length < 1) return "";
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        if (text.Length <= length) return text;
        var start = text.Length - length;
        if (char.IsLowSurrogate(text[start]) && start > 0 && char.IsHighSurrogate(text[start - 1])) start++;
        return "…" + text[start..];
    }
}
