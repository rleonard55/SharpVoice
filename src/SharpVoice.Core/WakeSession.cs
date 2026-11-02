namespace SharpVoice.Core;

/// <summary>One provisional wake operation; a shared-prefix detection never commits an early Ask request.</summary>
public sealed class WakeSession(WakeDetection initial, AppConfig config)
{
    private VoiceMode? confirmed = initial.Mode;
    private int stopped, consumeRelease;
    public WakeDetection Initial => initial;
    public VoiceMode Mode => confirmed ?? initial.Mode;
    public bool Confirm(WakeDetection candidate)
    {
        if (candidate.Epoch != initial.Epoch || Math.Abs(candidate.StartSample - initial.StartSample) > 6400 || !WakePhrases.IsLonger(config, initial.Mode, candidate.Mode)) return false;
        confirmed = candidate.Mode; return true;
    }
    public WakeTranscript Resolve(string transcript) => WakePhrases.Resolve(config, transcript, initial.Mode, confirmed);
    public bool HandlePress(VoiceMode mode)
    {
        if (mode != Mode || Interlocked.CompareExchange(ref stopped, 1, 0) != 0) return false;
        Interlocked.Exchange(ref consumeRelease, 1); return true;
    }
    public bool ConsumeRelease() => Interlocked.Exchange(ref consumeRelease, 0) != 0;
}
