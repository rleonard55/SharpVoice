using System.Buffers;

namespace SharpVoice.Audio;

public delegate void WakeAudioHandler(ReadOnlySpan<float> samples, long firstSample, long epoch);

public sealed partial class WasapiAudioRecorder
{
    private bool wakeListening;
    private bool wakeRecording;
    private WakeAudioHandler? wakeReceiver;
    private WakeAudioBuffer? wakeBuffer;
    private long captureEpoch, sampleCursor, lastAudibleSample;
    private static long nextCaptureEpoch;
    public long CaptureEpoch { get { lock (gate) return captureEpoch; } }
    public bool IsWakeListening { get { lock (gate) return wakeListening && captureRunning && !paused && !disposed; } }
    public TimeSpan WakeSilenceElapsed { get { lock (gate) return TimeSpan.FromSeconds((sampleCursor - lastAudibleSample) / (double)SampleRate); } }
    public void SetWakeListening(bool enabled, WakeAudioHandler? receiver)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (wakeListening == enabled && wakeReceiver == receiver) return;
            wakeListening = enabled; wakeReceiver = enabled ? receiver : null;
            wakeBuffer?.Clear(); wakeBuffer = enabled ? new(SampleRate * 30) : null;
            var silence = recording is not null ? sampleCursor - lastAudibleSample : 0;
            captureEpoch = Interlocked.Increment(ref nextCaptureEpoch); sampleCursor = 0; lastAudibleSample = -silence;
            configurationChanged.Set();
        }
    }
    public Task BeginWakeAsync(long startSample, long epoch, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (failure is not null || paused || !captureRunning || !wakeListening || wakeBuffer is null || epoch != captureEpoch)
                throw new InvalidOperationException("Wake microphone changed or paused. Please say the phrase again.");
            if (recording is not null) throw new InvalidOperationException("Already recording.");
            var owned = ArrayPool<float>.Shared.Rent(maxSamples);
            try { recordingCount = wakeBuffer.CopyFrom(startSample, SampleRate / 5, owned.AsSpan(0, maxSamples)); }
            catch { ArrayPool<float>.Shared.Return(owned, true); throw; }
            recording = owned; wakeRecording = true; voicedSamples = 0;
            for (var i = 0; i < recordingCount; i++) if (recording[i] != 0) voicedSamples++;
            postRollRemaining = -1;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            recordingReady = new(TaskCreationOptions.RunContinuationsAsynchronously); recordingReady.SetResult();
            return Task.CompletedTask;
        }
    }
    private void ResetWakeCapture()
    {
        wakeBuffer?.Clear(); captureEpoch = Interlocked.Increment(ref nextCaptureEpoch); sampleCursor = lastAudibleSample = 0;
    }
}
