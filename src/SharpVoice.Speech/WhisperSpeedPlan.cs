namespace SharpVoice.Speech;

/// <summary>Opt-in partial encoder evaluation. Never cuts or substitutes the recording itself.</summary>
internal static class WhisperSpeedPlan
{
    internal static int AudioContext(int samples, bool fast)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(samples);
        if (!fast || samples == 0 || samples > 15 * 16000) return 0;
        // Whisper encodes 50 frames/second. Preserve 2.56 seconds of extra context,
        // use a conservative 15.36-second floor, and align to native 64-frame blocks.
        var frames = (samples + 319) / 320;
        return Math.Max(768, ((frames + 128 + 63) / 64) * 64);
    }
    internal static int Threads => Math.Clamp(Environment.ProcessorCount, 1, 6);
}
