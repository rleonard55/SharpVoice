namespace SharpVoice.Core;

/// <summary>Optional toggle-session stop based on input remaining below the applied noise gate.</summary>
public sealed class SilenceStopPolicy(int seconds)
{
    private TimeSpan lastSound;
    public bool Observe(float levelDb, float gateDb, TimeSpan elapsed)
    {
        if (seconds <= 0 || !float.IsFinite(levelDb)) return false;
        if (levelDb >= gateDb) lastSound = elapsed;
        return elapsed - lastSound >= TimeSpan.FromSeconds(seconds);
    }
}
