using System.Buffers;
using SharpVoice.Core;

namespace SharpVoice.Speech;

/// <summary>VAD runs independently of cancellable, speech-triggered Whisper prefix checks.</summary>
public sealed class WakeWhisperDetector : IAsyncDisposable
{
    private sealed record Block(float[] Data, int Count, long First, long Epoch, long Revision);
    private readonly object gate = new();
    private readonly Queue<Block> pending = new();
    private readonly SemaphoreSlim ready = new(0);
    private readonly CancellationTokenSource stop = new();
    private readonly IWakeSpeechDetector voice;
    private readonly Func<ReadOnlyMemory<float>, CancellationToken, Task<string>> recognize;
    private readonly AppConfig config;
    private readonly float[] history = new float[16000 * 12];
    private readonly Task worker, recognizer;
    private readonly TimeSpan interval;
    private readonly bool diagnostics;
    private CancellationTokenSource? inference;
    private WakeSpeechActivity? activity;
    private long revision, recognitionRevision, epoch = -1, end, utterance = -1, lastSpeech, analyzedEnd;
    private long enqueued, processed, dropped, resets, lastProcessed;
    private int queued, historyCount;
    private double lastMs, longestMs;
    private long checks;
    private double lastInferenceMs;
    private bool disposed, enabled = true, detected, pendingShort;
    public event Action<WakeDetection>? Detected;
    public event Action<Exception>? Failed;
    public WakeSpeechActivity? Activity => Volatile.Read(ref activity);
    public bool IsRecognizing { get { lock (gate) return inference is not null; } }
    public WakeDetectorDiagnostics? Diagnostics
    {
        get { lock (gate) return diagnostics ? new(enqueued, processed, dropped, resets, queued, lastMs, longestMs,
            lastProcessed == 0 ? -1 : Environment.TickCount64 - lastProcessed, worker.IsCompleted && recognizer.IsCompleted,
            checks, lastInferenceMs, inference is not null) : null; }
    }
    public WakeWhisperDetector(string speechDirectory, AppConfig config,
        Func<ReadOnlyMemory<float>, CancellationToken, Task<string>> recognize, bool diagnostics = false)
        : this(config, recognize, new WakeVoiceActivity(Path.Combine(speechDirectory, "silero_vad.onnx")), diagnostics) { }
    internal WakeWhisperDetector(AppConfig config, Func<ReadOnlyMemory<float>, CancellationToken, Task<string>> recognize,
        IWakeSpeechDetector voice, bool diagnostics = false, TimeSpan? interval = null)
    {
        WakePhrases.Validate(config); this.config = config; this.recognize = recognize; this.voice = voice;
        this.diagnostics = diagnostics; this.interval = interval ?? TimeSpan.FromMilliseconds(750);
        worker = Task.Run(AudioLoop); recognizer = Task.Run(RecognitionLoop);
    }
    public bool Enqueue(ReadOnlySpan<float> samples, long firstSample, long captureEpoch)
    {
        lock (gate)
        {
            if (disposed || worker.IsCompleted) return false;
            if (samples.Length + queued > 16000 * 4)
            {
                if (diagnostics) dropped += pending.Count + 1;
                ClearQueue(); ResetState(); return false;
            }
            if (samples.IsEmpty) return true;
            var copy = ArrayPool<float>.Shared.Rent(samples.Length); samples.CopyTo(copy);
            pending.Enqueue(new(copy, samples.Length, firstSample, captureEpoch, revision)); queued += samples.Length;
            if (diagnostics) enqueued++; ready.Release(); return true;
        }
    }
    public void Reset(long captureEpoch)
    {
        lock (gate) { ClearQueue(); ResetState(); }
    }
    public void SetRecognitionEnabled(bool value)
    {
        lock (gate)
        {
            if (enabled == value) return;
            enabled = value; recognitionRevision++; inference?.Cancel();
        }
    }
    private void ClearQueue()
    {
        while (pending.TryDequeue(out var block)) ArrayPool<float>.Shared.Return(block.Data, true);
        queued = 0;
    }
    private void ResetState()
    {
        revision++; recognitionRevision++; inference?.Cancel();
        historyCount = 0; Array.Clear(history); utterance = -1; detected = false; pendingShort = false; analyzedEnd = 0;
        Volatile.Write(ref activity, null); if (diagnostics) resets++;
    }
    private async Task AudioLoop()
    {
        long activeRevision = -1, activeEpoch = -1, next = -1;
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
                    lock (gate)
                    {
                        if (block.Revision != revision) continue;
                        if (activeRevision != block.Revision || activeEpoch != block.Epoch || next != block.First)
                        {
                            voice.Reset(block.First); historyCount = 0; utterance = -1; lastSpeech = block.First;
                            recognitionRevision++; inference?.Cancel(); detected = false; pendingShort = false; analyzedEnd = 0;
                            activeRevision = block.Revision; activeEpoch = block.Epoch;
                            if (diagnostics) resets++;
                        }
                    }
                    var observed = voice.Process(block.Data.AsSpan(0, block.Count), block.First, block.Epoch);
                    lock (gate)
                    {
                        if (block.Revision != revision) continue;
                        epoch = block.Epoch; end = block.First + block.Count; next = end;
                        for (var i = 0; i < block.Count; i++) history[(int)((block.First + i) % history.Length)] = block.Data[i];
                        historyCount = Math.Min(history.Length, historyCount + block.Count);
                        if (observed.LastSpeechSample > lastSpeech)
                        {
                            if (utterance < 0 || block.First - lastSpeech >= 16000 * Math.Max(2, config.WakeSilenceSeconds))
                            {
                                utterance = Math.Max(end - historyCount, Math.Max(0, block.First - 8000));
                                detected = false; pendingShort = false; analyzedEnd = 0; recognitionRevision++; inference?.Cancel();
                            }
                            lastSpeech = observed.LastSpeechSample;
                        }
                        Volatile.Write(ref activity, observed);
                        if (diagnostics)
                        {
                            processed++; lastProcessed = Environment.TickCount64;
                            lastMs = System.Diagnostics.Stopwatch.GetElapsedTime(began).TotalMilliseconds;
                            longestMs = Math.Max(longestMs, lastMs);
                        }
                    }
                }
                finally { ArrayPool<float>.Shared.Return(block.Data, true); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) { Failed?.Invoke(ex); stop.Cancel(); }
        finally { voice.Dispose(); lock (gate) ClearQueue(); }
    }
    private async Task RecognitionLoop()
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false))
            {
                float[] samples; long onset, observedEpoch, version; CancellationTokenSource owner;
                lock (gate)
                {
                    if (!enabled || detected || utterance < 0 || end - utterance < 16000 || utterance < end - historyCount
                        || end - utterance > 16000 * 10 || end - analyzedEnd < 8000
                        || (!pendingShort && analyzedEnd > lastSpeech + 4800)) continue;
                    onset = utterance; observedEpoch = epoch; version = recognitionRevision;
                    // Fixed progressive windows prevent slower inference/timer jitter
                    // from skipping the short prefix before the request dominates it.
                    var windowEnd = analyzedEnd == 0 ? onset + 16000 : analyzedEnd + 8000;
                    if (windowEnd > end) continue;
                    samples = new float[checked((int)(windowEnd - onset))];
                    for (var i = 0; i < samples.Length; i++) samples[i] = history[(int)((onset + i) % history.Length)];
                    analyzedEnd = windowEnd; owner = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); inference = owner;
                }
                try
                {
                    var began = System.Diagnostics.Stopwatch.GetTimestamp();
                    var text = await recognize(samples, owner.Token).ConfigureAwait(false);
                    lock (gate)
                    {
                        if (diagnostics) { checks++; lastInferenceMs = System.Diagnostics.Stopwatch.GetElapsedTime(began).TotalMilliseconds; }
                        if (!disposed && enabled && !owner.IsCancellationRequested && version == recognitionRevision && epoch == observedEpoch
                            && end - onset < 16000 * 30 && WakePhrases.TryDetect(config, text, out var mode))
                        {
                            var other = mode == VoiceMode.AskAi ? VoiceMode.Dictation : VoiceMode.AskAi;
                            // A partial "Hey Chat" must leave time for "Dictate".
                            if (samples.Length < 32000 && WakePhrases.IsLonger(config, mode, other)) pendingShort = true;
                            else
                            {
                                detected = true;
                                // Delivery is revision-fenced too: Reset cannot return
                                // before an older callback has been delivered/drained.
                                Detected?.Invoke(new(mode, onset, epoch));
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
                finally
                {
                    Array.Clear(samples);
                    lock (gate) { if (inference == owner) inference = null; owner.Dispose(); }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) { Failed?.Invoke(ex); stop.Cancel(); }
    }
    public async ValueTask DisposeAsync()
    {
        lock (gate) { if (disposed) return; disposed = true; enabled = false; ResetState(); ClearQueue(); }
        stop.Cancel(); await Task.WhenAll(worker, recognizer).ConfigureAwait(false);
        stop.Dispose(); ready.Dispose(); Array.Clear(history);
    }
}
