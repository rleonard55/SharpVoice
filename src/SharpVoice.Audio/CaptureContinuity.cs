namespace SharpVoice.Audio;

// Shared-mode resampling can leave device positions in endpoint-clock frames,
// while GetBuffer returns PCM frame counts in the requested client format.
internal sealed class CaptureContinuity
{
    private ulong? expected;
    private readonly uint positionRate, captureRate;
    private bool seen;
    public CaptureContinuity(uint positionSampleRate = WasapiAudioRecorder.SampleRate, uint captureSampleRate = WasapiAudioRecorder.SampleRate)
    {
        if (positionSampleRate == 0 || captureSampleRate == 0) throw new ArgumentOutOfRangeException(nameof(positionSampleRate), "Audio clock rates must be positive.");
        positionRate = positionSampleRate; captureRate = captureSampleRate;
    }
    public bool Observe(ulong position, uint frames, uint flags)
    {
        if (frames == 0) return false;
        var distance = expected is ulong previous ? position > previous ? position - previous : previous - position : 0;
        var tolerance = positionRate % captureRate == 0 ? 0u : 1u;
        var gap = seen && ((flags & 5) != 0 || (expected.HasValue && distance > tolerance));
        var advance = (ulong)frames * positionRate / captureRate;
        // An initial discontinuity can include a shortened converter-priming
        // packet. Start position prediction from the following complete packet.
        expected = !seen && (flags & 1) != 0 || position > ulong.MaxValue - advance ? null : position + advance;
        seen = true;
        return gap;
    }
}
