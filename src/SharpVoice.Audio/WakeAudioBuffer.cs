namespace SharpVoice.Audio;

/// <summary>Recorder-lock-owned ring addressed by absolute sample cursor, not relative snapshot length.</summary>
internal sealed class WakeAudioBuffer(int capacity)
{
    private readonly float[] data = new float[capacity];
    private long end;
    private int count;
    public void Write(ReadOnlySpan<float> samples, long first)
    {
        if (end != first) Clear();
        var skip = Math.Max(0, samples.Length - capacity);
        for (var i = skip; i < samples.Length; i++) data[(int)((first + i) % capacity)] = samples[i];
        end = first + samples.Length; count = Math.Min(capacity, count + samples.Length);
    }
    public int CopyFrom(long onset, int padding, Span<float> destination)
    {
        if (onset < end - count || onset >= end) throw new InvalidOperationException("Wake speech is no longer available. Please say the phrase again.");
        var start = Math.Max(end - count, Math.Max(0, onset - padding));
        var length = checked((int)(end - start));
        if (length >= destination.Length) throw new InvalidOperationException("Wake speech exceeded the recording limit. Increase Recording Limit or try again.");
        for (var i = 0; i < length; i++) destination[i] = data[(int)((start + i) % capacity)];
        return length;
    }
    public void Clear() { Array.Clear(data); count = 0; end = 0; }
}
