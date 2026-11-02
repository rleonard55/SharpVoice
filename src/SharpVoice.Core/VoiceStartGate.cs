namespace SharpVoice.Core;

/// <summary>UI-thread hold/toggle intent while microphone startup waits for speech playback to stop.</summary>
public sealed class VoiceStartGate
{
    private long generation;
    private VoiceMode mode;
    public bool Pending { get; private set; }
    public long Begin(VoiceMode value) { mode = value; Pending = true; return ++generation; }
    public void Promote(VoiceMode value) { if (Pending && value == VoiceMode.AskAi) mode = value; }
    public void Cancel() { Pending = false; ++generation; }
    public bool TryComplete(long id, out VoiceMode result)
    {
        result = mode;
        if (!Pending || id != generation) return false;
        Pending = false; return true;
    }
}
