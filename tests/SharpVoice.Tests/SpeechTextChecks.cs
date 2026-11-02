using SharpVoice.Core;
internal static class SpeechTextChecks
{
    public static Task Markers()
    {
        foreach(var value in new[]{"[BLANK_AUDIO]", " [blank_audio] ", "[SILENCE]", "[MUSIC]", "(silence)", "[NO_SPEECH]", "[BLANK_AUDIO] [MUSIC]"})
            if(TranscriptionText.Clean(value)!="")throw new Exception("Non-speech marker escaped: "+value);
        if(TranscriptionText.Clean(" Hello [BLANK_AUDIO] world. ")!="Hello world.")throw new Exception("Mixed marker contaminated recognized speech.");
        foreach(var text in new[]{"Keep [status] unchanged.","The word music is important.","[HttpPost]", "你好。", "..."})
            if(TranscriptionText.Clean(text)!=text)throw new Exception("Valid speech text was changed.");
        return Task.CompletedTask;
    }
}
