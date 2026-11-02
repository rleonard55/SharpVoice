using System.Buffers;
using System.Runtime.InteropServices;
using SharpVoice.Core;

namespace SharpVoice.Speech;

/// <summary>Owns all native inference on one worker; microphone callbacks only copy to a bounded queue.</summary>
public sealed partial class WakeKeywordDetector : IAsyncDisposable
{
    private sealed record Block(float[] Data, int Count, long First, long Epoch, long Revision);
    private readonly object gate = new();
    private readonly Queue<Block> pending = new();
    private readonly SemaphoreSlim ready = new(0);
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private readonly string[] keywords;
    private readonly nint[] spotters = new nint[2];
    private readonly WakeVoiceActivity voice;
    private WakeSpeechActivity? activity;
    public WakeSpeechActivity? Activity => Volatile.Read(ref activity);
    private int queued;
    private long revision;
    private bool disposed;
    public event Action<WakeDetection>? Detected;
    public event Action<Exception>? Failed;

    public WakeKeywordDetector(string modelDirectory, string dictationPhrase, string askPhrase, bool diagnostics = false)
    {
        this.diagnostics = diagnostics;
        var tokenizer = new WakeTokenizer(Path.Combine(modelDirectory, "bpe.model"), Path.Combine(modelDirectory, "tokens.txt"));
        keywords = [tokenizer.Encode(dictationPhrase) + " @DICTATION", tokenizer.Encode(askPhrase) + " @ASK"];
        var owned = new List<nint>();
        nint Text(string s) { var ptr = Marshal.StringToCoTaskMemUTF8(s); owned.Add(ptr); return ptr; }
        try
        {
            var config = new WakeNative.Config
            {
                SampleRate = 16000, Features = 80, Paths = 4, TrailingBlanks = 1, Score = 1.5f, Threshold = .35f,
                Model = new()
                {
                    Encoder = Text(Path.Combine(modelDirectory, "encoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx")),
                    Decoder = Text(Path.Combine(modelDirectory, "decoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx")),
                    Joiner = Text(Path.Combine(modelDirectory, "joiner-epoch-12-avg-2-chunk-16-left-64.int8.onnx")),
                    Tokens = Text(Path.Combine(modelDirectory, "tokens.txt")), Threads = 1, Provider = Text("cpu")
                },
            };
            for (var i = 0; i < spotters.Length; i++)
            {
                config.KeywordsBuffer = Text(keywords[i]); config.KeywordsSize = System.Text.Encoding.UTF8.GetByteCount(keywords[i]);
                spotters[i] = WakeNative.Create(in config);
                if (spotters[i] == 0) throw new InvalidOperationException("The local wake model could not be opened. Retry preparation in Wake Phrases settings.");
            }
            voice = new(Path.Combine(modelDirectory, "silero_vad.onnx"));
        }
        catch { foreach (var spotter in spotters) if (spotter != 0) WakeNative.Destroy(spotter); throw; }
        finally { foreach (var pointer in owned) Marshal.FreeCoTaskMem(pointer); }
        worker = Task.Run(Run);
    }

    public bool Enqueue(ReadOnlySpan<float> samples, long firstSample, long epoch)
    {
        lock (gate)
        {
            if (disposed || worker.IsCompleted) return false;
            if (queued + samples.Length > 16000)
            {
                if (diagnostics) diagnosticDropped += pending.Count + 1;
                Clear(); revision++; Volatile.Write(ref activity, null); return false;
            }
            if (samples.IsEmpty) return true;
            var copy = ArrayPool<float>.Shared.Rent(samples.Length); samples.CopyTo(copy);
            pending.Enqueue(new(copy, samples.Length, firstSample, epoch, revision)); queued += samples.Length;
            if (diagnostics) diagnosticEnqueued++;
            ready.Release(); return true;
        }
    }
    public void Reset(long epoch)
    {
        lock (gate) { Clear(); revision++; if (diagnostics) diagnosticResets++; Volatile.Write(ref activity, null); }
    }
    private void Clear()
    {
        while (pending.TryDequeue(out var block)) ArrayPool<float>.Shared.Return(block.Data, true);
        queued = 0;
    }
    private async Task Run()
    {
        nint[] streams = [0, 0]; long activeEpoch = -1, activeRevision = -1, next = -1, origin = 0;
        try
        {
            while (true)
            {
                await ready.WaitAsync(stop.Token).ConfigureAwait(false);
                Block? block;
                lock (gate) { if (!pending.TryDequeue(out block)) continue; queued -= block.Count; }
                try
                {
                    var began = diagnostics ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                    if (activeEpoch != block.Epoch || activeRevision != block.Revision || next != block.First)
                    {
                        if (diagnostics) lock (gate) diagnosticResets++;
                        for (var i = 0; i < streams.Length; i++)
                        {
                            if (streams[i] != 0) WakeNative.DestroyStream(streams[i]);
                            streams[i] = 0;
                            streams[i] = WakeNative.Stream(spotters[i]);
                            if (streams[i] == 0) throw new InvalidOperationException("Could not prepare a local wake phrase.");
                        }
                        activeEpoch = block.Epoch; activeRevision = block.Revision; origin = block.First;
                        voice.Reset(block.First);
                    }
                    next = block.First + block.Count;
                    var speech = voice.Process(block.Data.AsSpan(0, block.Count), block.First, block.Epoch);
                    lock (gate) { if (!disposed && block.Revision == revision) Volatile.Write(ref activity, speech); }
                    Process(block, streams, origin);
                    if (diagnostics) lock (gate)
                    {
                        diagnosticProcessed++; diagnosticLastProcessed = Environment.TickCount64;
                        diagnosticLastMs = System.Diagnostics.Stopwatch.GetElapsedTime(began).TotalMilliseconds;
                        diagnosticLongestMs = Math.Max(diagnosticLongestMs, diagnosticLastMs);
                    }
                }
                finally { ArrayPool<float>.Shared.Return(block.Data, true); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) { Failed?.Invoke(ex); }
        finally
        {
            foreach (var stream in streams) if (stream != 0) WakeNative.DestroyStream(stream);
            foreach (var spotter in spotters) WakeNative.Destroy(spotter);
            voice.Dispose();
            lock (gate) Clear();
        }
    }
    private unsafe void Process(Block block, nint[] streams, long origin)
    {
        for (var i = 0; i < streams.Length; i++)
        {
            fixed (float* samples = block.Data) WakeNative.Accept(streams[i], 16000, samples, block.Count);
            while (WakeNative.Ready(spotters[i], streams[i]) != 0)
            {
                WakeNative.Decode(spotters[i], streams[i]);
                var result = WakeNative.Get(spotters[i], streams[i]);
                try
                {
                    if (result == null || result->Keyword == 0 || Marshal.ReadByte(result->Keyword) == 0) continue;
                    var start = origin + (long)((result->StartTime + (result->Count > 0 ? result->Timestamps[0] : 0)) * 16000);
                    lock (gate)
                    {
                        if (!disposed && block.Revision == revision) Detected?.Invoke(new(i == 0 ? VoiceMode.Dictation : VoiceMode.AskAi, start, block.Epoch));
                    }
                    WakeNative.Reset(spotters[i], streams[i]);
                }
                finally { if (result != null) WakeNative.FreeResult(result); }
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        lock (gate) { if (disposed) return; disposed = true; revision++; Clear(); }
        stop.Cancel();
        await worker.ConfigureAwait(false);
        stop.Dispose(); ready.Dispose();
    }
}
