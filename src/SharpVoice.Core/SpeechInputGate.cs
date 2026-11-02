namespace SharpVoice.Core;

/// <summary>Preserves ordinary pre-roll, while requiring microphone suspension throughout voice playback.</summary>
public sealed class SpeechInputGate
{
    public bool Suspended { get; private set; }
    public void Suspend() => Suspended = true;
    public bool ShouldPause(bool userPaused) => userPaused || Suspended;
    public bool Release()
    {
        if (!Suspended) return false;
        Suspended = false; return true;
    }
}
