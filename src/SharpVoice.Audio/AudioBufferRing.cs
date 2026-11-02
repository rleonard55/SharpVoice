using System.Buffers;

namespace SharpVoice.Audio;

// Single writer; readers retry a snapshot if the producer changed it while copying.
// No locks or allocations on steady-state writes or CopyTo. Dispose after the writer stops.
public sealed class AudioBufferRing(int capacity) : IDisposable
{
    private readonly float[] data = capacity > 0 ? ArrayPool<float>.Shared.Rent(capacity) : throw new ArgumentOutOfRangeException(nameof(capacity));
    private int position;
    private int count;
    private int version;
    private int disposed;

    public void Write(ReadOnlySpan<float> samples)
    {
        Interlocked.Increment(ref version);
        foreach (var sample in samples)
        {
            data[position] = sample;
            position = (position + 1) % capacity;
            count = Math.Min(count + 1, capacity);
        }
        Interlocked.Increment(ref version);
    }

    public int CopyTo(Span<float> destination)
    {
        var spin = new SpinWait();
        while (true)
        {
            var before = Volatile.Read(ref version);
            if ((before & 1) != 0) { spin.SpinOnce(); continue; }
            var length = Math.Min(count, destination.Length);
            var start = (position - length + capacity) % capacity;
            var first = Math.Min(length, capacity - start);
            data.AsSpan(start, first).CopyTo(destination);
            data.AsSpan(0, length - first).CopyTo(destination[first..]);
            Thread.MemoryBarrier();
            if (before == Volatile.Read(ref version)) return length;
            spin.SpinOnce();
        }
    }

    public float[] Snapshot()
    {
        var result = new float[count];
        var copied = CopyTo(result);
        return copied == result.Length ? result : result[..copied];
    }

    public void Clear()
    {
        Interlocked.Increment(ref version);
        position = 0; count = 0; Array.Clear(data);
        Interlocked.Increment(ref version);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) ArrayPool<float>.Shared.Return(data, clearArray: true);
    }

}

public sealed class AudioFilter
{
    private float previousInput;
    private float previousOutput;
    private const float Alpha = 0.96954f; // First-order 80 Hz high-pass at 16 kHz.

    public float Process(Span<float> samples, float gateDb)
    {
        double energy = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var value = Alpha * (previousOutput + samples[i] - previousInput);
            previousInput = samples[i];
            previousOutput = value;
            samples[i] = value;
            energy += value * value;
        }
        var db = samples.IsEmpty ? -100 : (float)(20 * Math.Log10(Math.Max(1e-5, Math.Sqrt(energy / samples.Length))));
        if (db < gateDb) samples.Clear();
        return db;
    }
}

/// <summary>Owns a pooled capture buffer until transcription completes. Do not retain Samples after disposal.</summary>
public sealed class AudioRecording : IDisposable
{
    private float[]? buffer;
    private readonly int count;
    private readonly ArrayPool<float> pool;
    public bool HasSpeech { get; }
    public ReadOnlyMemory<float> Samples => (buffer ?? throw new ObjectDisposedException(nameof(AudioRecording))).AsMemory(0, count);

    public AudioRecording(float[] buffer, int count, bool hasSpeech, ArrayPool<float>? pool = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));
        this.buffer = buffer; this.count = count; this.pool = pool ?? ArrayPool<float>.Shared; HasSpeech = hasSpeech;
    }
    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref buffer, null);
        if (owned is not null) pool.Return(owned, clearArray: true);
    }
}
