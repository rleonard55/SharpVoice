using System.Buffers;


namespace SharpVoice.Audio;

/// <summary>Shared-mode, event-driven WASAPI. Native COM vtables avoid runtime COM marshalling.</summary>
public sealed partial class WasapiAudioRecorder : IDisposable
{
    public const int SampleRate = 16000;
    private readonly object gate = new();
    private readonly AudioBufferRing preRoll = new(SampleRate / 4);
    private AudioFilter filter = new();
    private readonly bool keepReady;
    private readonly Func<AutoResetEvent, string, IAudioCaptureSession> createSession;
    private readonly AudioSamplesHandler consumeSamples;
    private bool preview, captureRunning;
    private TaskCompletionSource? recordingReady;
    private Task captureClosed = Task.CompletedTask;
    private readonly ManualResetEvent shutdown = new(false);
    private readonly AutoResetEvent ready = new(false);
    private readonly AutoResetEvent configurationChanged = new(false);
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread worker;
    private readonly int maxSamples;
    private readonly string deviceId;
    private float[]? recording;
    private int recordingCount;
    private TaskCompletionSource<AudioRecording>? completion;
    private int postRollRemaining = -1;
    private int voicedSamples;
    private volatile bool paused;
    private volatile float noiseGateDb;
    private volatile float levelDb = float.NaN;
    private Exception? failure;
    private bool disposed;
    public bool IsAvailable { get { lock (gate) return !disposed && failure is null && started.Task.IsCompletedSuccessfully; } }
    public float LevelDb => levelDb;
    public event Action<Exception>? Failed;
    public event Action? RecordingLimitReached;
    public event Action? RecordingInterrupted;

    public WasapiAudioRecorder(float noiseGateDb, int maxRecordingSeconds, string deviceId = "", bool keepReady = true, bool diagnostics = false)
        : this(noiseGateDb, maxRecordingSeconds, deviceId, keepReady, static (ready, device) => new WasapiCaptureSession(ready, device), diagnostics) { }

    internal WasapiAudioRecorder(float noiseGateDb, int maxRecordingSeconds, string deviceId, bool keepReady,
        Func<AutoResetEvent, string, IAudioCaptureSession> createSession, bool diagnostics = false)
    {
        this.noiseGateDb = noiseGateDb;
        this.diagnostics = diagnostics;
        this.deviceId = deviceId;
        this.keepReady = keepReady;
        this.createSession = createSession;
        consumeSamples = Consume;
        maxSamples = checked(SampleRate * maxRecordingSeconds);
        worker = new Thread(CaptureLoop) { IsBackground = true, Name = "SharpVoice WASAPI" };
    }

    public Task StartAsync() { worker.Start(); return started.Task; }

    public void Configure(float gateDb, bool isPaused)
    {
        noiseGateDb = gateDb;
        paused = isPaused;
        lock (gate) { preRoll.Clear(); ResetWakeCapture(); if (isPaused) preview = false; }
        configurationChanged.Set();
    }

    public void SetPreview(bool enabled)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (enabled && paused) throw new InvalidOperationException("Resume the microphone before starting preview.");
            if (enabled && failure is not null) throw new InvalidOperationException("Microphone unavailable. Apply Audio settings to retry.", failure);
            preview = enabled;
        }
        configurationChanged.Set();
    }

    public void Begin() => BeginAsync().GetAwaiter().GetResult();
    public Task BeginAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (failure is not null) throw new InvalidOperationException("Microphone capture stopped. Restart SharpVoice.", failure);
            if (paused) throw new InvalidOperationException("Microphone capture is paused.");
            if (recording is not null) throw new InvalidOperationException("Already recording.");
            recording = ArrayPool<float>.Shared.Rent(maxSamples);
            wakeRecording = false;
            recordingCount = keepReady ? preRoll.CopyTo(recording.AsSpan(0, maxSamples)) : 0;
            // Pre-roll contains gated samples; include audible speech that started just before the key.
            voicedSamples = 0;
            for (var i = 0; i < recordingCount; i++) if (recording[i] != 0) voicedSamples++;
            postRollRemaining = -1;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            recordingReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (captureRunning) recordingReady.TrySetResult();
            configurationChanged.Set();
            return recordingReady.Task.WaitAsync(ct);
        }
    }

    public AudioRecording? Snapshot(int maxSeconds = 8)
    {
        if (maxSeconds is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(maxSeconds));
        lock (gate)
        {
            if (disposed || recording is null || recordingCount < SampleRate / 2) return null;
            var count = Math.Min(recordingCount, SampleRate * maxSeconds);
            var copy = ArrayPool<float>.Shared.Rent(count);
            recording.AsSpan(recordingCount - count, count).CopyTo(copy);
            var audible = 0; for (var i = 0; i < count; i++) if (copy[i] != 0) audible++;
            return new(copy, count, audible >= SampleRate / 20);
        }
    }

    public async Task<AudioRecording> EndAsync(CancellationToken cancellationToken)
    {
        Task<AudioRecording> result;
        lock (gate)
        {
            if (completion is null) throw new InvalidOperationException("No recording is active.");
            if (recording is not null) postRollRemaining = SampleRate / 20;
            result = completion.Task;
        }
        AudioRecording? audio = null;
        try
        {
            audio = await result.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            Task closed;
            lock (gate)
            {
                if (completion?.Task != result) throw new OperationCanceledException("Recording cancelled.");
                completion = null;
                closed = !paused && (keepReady || preview || wakeListening) ? Task.CompletedTask : captureClosed;
            }
            await closed.WaitAsync(cancellationToken);
            return audio;
        }
        catch
        {
            audio?.Dispose();
            await CancelAsync(); // Cancellation must not publish idle before native release.
            throw;
        }
    }

    public void Cancel() => _ = CancelAsync();
    public Task CancelAsync()
    {
        lock (gate)
        {
            if (recording is not null) ArrayPool<float>.Shared.Return(recording, clearArray: true);
            recording = null;
            recordingReady?.TrySetCanceled();
            if (completion?.Task.IsCompletedSuccessfully == true) completion.Task.Result.Dispose();
            completion?.TrySetCanceled();
            completion = null;
            preRoll.Clear();
            configurationChanged.Set();
            return !disposed && !paused && (keepReady || preview || wakeListening) ? Task.CompletedTask : captureClosed;
        }
    }

    private void Consume(Span<float> samples, bool discontinuity)
    {
        bool reachedLimit = false, interrupted = false;
        lock (gate)
        {
            if (diagnostics)
            {
                diagnosticPackets++; diagnosticFrames += samples.Length;
                if (discontinuity) diagnosticGaps++;
                diagnosticLastPacket = Environment.TickCount64;
            }
            if (paused) { levelDb = -100; return; }
            if (discontinuity)
            {
                ResetWakeCapture(); preRoll.Clear(); filter = new();
                if (recording is not null && wakeRecording)
                {
                    ArrayPool<float>.Shared.Return(recording, true); recording = null;
                    completion!.TrySetException(new InvalidOperationException("Microphone audio was interrupted. Please say the wake phrase and request again."));
                    interrupted = true; configurationChanged.Set();
                }
            }
            var first = sampleCursor;
            if (wakeListening) wakeReceiver?.Invoke(samples, first, captureEpoch);
            levelDb = filter.Process(samples, noiseGateDb);
            sampleCursor += samples.Length;
            if (levelDb >= noiseGateDb) lastAudibleSample = sampleCursor;
            if (wakeListening) wakeBuffer?.Write(samples, first);
            if (recording is not null)
            {
                var count = Math.Min(samples.Length, maxSamples - recordingCount);
                if (postRollRemaining >= 0) count = Math.Min(count, postRollRemaining);
                samples[..count].CopyTo(recording.AsSpan(recordingCount));
                recordingCount += count;
                if (levelDb >= noiseGateDb) voicedSamples += count;
                if (postRollRemaining >= 0) postRollRemaining -= count;
                reachedLimit = recordingCount >= maxSamples;
                if (postRollRemaining == 0 || reachedLimit)
                {
                    completion!.TrySetResult(new(recording, recordingCount, voicedSamples >= SampleRate / 20));
                    recording = null;
                    configurationChanged.Set();
                }
            }
            if (keepReady) preRoll.Write(samples);
        }
        if (reachedLimit) RecordingLimitReached?.Invoke();
        if (interrupted) RecordingInterrupted?.Invoke();
    }

    private bool CaptureWanted
    {
        get
        {
            lock (gate)
            {
                var wanted = !disposed && !paused && (keepReady || preview || wakeListening || recording is not null);
                if (!wanted) { captureRunning = false; levelDb = float.NaN; } // Fence new starts before an old session begins disposal.
                return wanted;
            }
        }
    }

    private void CaptureLoop()
    {
        var waits = new WaitHandle[] { shutdown, ready, configurationChanged };
        if (!keepReady || paused) started.TrySetResult(); // Cold readiness opens no device.
        try
        {
            while (!shutdown.WaitOne(0))
            {
                TaskCompletionSource? closed = null;
                lock (gate)
                {
                    if (CaptureWanted)
                    {
                        // Claim ownership atomically with cancellation's closure snapshot.
                        closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        captureClosed = closed.Task;
                    }
                }
                if (closed is null)
                {
                    if (WaitHandle.WaitAny([shutdown, configurationChanged]) == 0) break;
                    continue;
                }
                try
                {
                    if (!CaptureWanted) continue;
                    using var capture = createSession(ready, deviceId);
                    if (diagnostics && capture is WasapiCaptureSession native)
                    {
                        native.DiagnosticsEnabled = true;
                        lock (gate) diagnosticCapture = native;
                    }
                    if (!CaptureWanted) continue;
                    filter = new();
                    capture.Start();
                    lock (gate)
                    {
                        ResetWakeCapture();
                        captureRunning = true;
                        started.TrySetResult(); // Keep Ready acknowledges real native Start.
                        if (recording is not null) recordingReady?.TrySetResult();
                    }
                    while (CaptureWanted)
                    {
                        var signal = WaitHandle.WaitAny(waits);
                        if (signal == 0) return;
                        if (signal == 1 && CaptureWanted) capture.Drain(consumeSamples);
                    }
                }
                finally
                {
                    lock (gate) { captureRunning = false; preRoll.Clear(); ResetWakeCapture(); levelDb = float.NaN; }
                    closed.TrySetResult(); // The using declaration has released every native handle.
                }
            }
        }
        catch (Exception ex)
        {
            lock (gate)
            {
                failure = ex;
                if (recording is not null) ArrayPool<float>.Shared.Return(recording, clearArray: true);
                recording = null;
                recordingReady?.TrySetException(ex);
                completion?.TrySetException(ex);
            }
            started.TrySetException(ex);
            Failed?.Invoke(ex);
        }
        finally
        {
            started.TrySetCanceled();
            lock (gate) { captureRunning = false; levelDb = float.NaN; preRoll.Clear(); }
        }
    }

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        shutdown.Set();
        Cancel();
        if (worker.IsAlive) worker.Join();
        preRoll.Dispose();
        wakeBuffer?.Clear(); wakeBuffer = null; wakeReceiver = null;
        shutdown.Dispose(); ready.Dispose(); configurationChanged.Dispose();
    }
}
