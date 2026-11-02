using SharpVoice.Audio;

internal static class WakeCaptureChecks
{
    public static Task ResampledContinuity()
    {
        // Real shared-mode packet shape: 48 kHz endpoint clock, 16 kHz client PCM.
        // Resampler startup returns 129 frames, followed by 160-frame packets.
        var clock = new CaptureContinuity(48000, 16000);
        Check(!clock.Observe(0, 129, 1), "Startup packet falsely reported a gap.");
        for (ulong position = 480; position <= 4800; position += 480)
            Check(!clock.Observe(position, 160, 0), "Valid resampled microphone audio reset wake detection.");
        Check(clock.Observe(5760, 160, 0), "A missing resampled packet was ignored.");
        Check(clock.Observe(6240, 160, 1), "Explicit driver glitch was ignored after resampling.");
        var fractional = new CaptureContinuity(44100, 16000);
        Check(!fractional.Observe(0, 160, 0) && !fractional.Observe(441, 160, 0), "44.1 kHz endpoint conversion reported a gap.");
        return Task.CompletedTask;
    }
    public static async Task Discontinuity()
    {
        var continuity = new CaptureContinuity();
        Check(!continuity.Observe(400, 800, 1), "First packet falsely reported a gap.");
        Check(!continuity.Observe(1200, 800, 2), "Silent contiguous packet reported a gap.");
        Check(continuity.Observe(2400, 800, 0), "Missing device frames were ignored.");
        Check(continuity.Observe(3200, 800, 1), "Driver discontinuity was ignored.");
        using var drivers = new MicrophoneActivationChecks.Drivers(); using var recorder = drivers.Recorder(false, 10);
        await recorder.StartAsync(); recorder.SetWakeListening(true, (_, _, _) => { });
        var capture = await drivers.Next(); await capture.Started.Task; await capture.Emit(1600);
        var oldEpoch = recorder.CaptureEpoch;
        await capture.Emit(new float[800], true);
        Check(recorder.CaptureEpoch != oldEpoch, "Gap retained old activation epoch.");
        try { await recorder.BeginWakeAsync(0, oldEpoch); throw new Exception("Pre-gap activation accepted."); } catch (InvalidOperationException) { }
        await capture.Emit(1600); await recorder.BeginWakeAsync(0, recorder.CaptureEpoch);
        var interrupted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.RecordingInterrupted += () => interrupted.TrySetResult();
        await capture.Emit(new float[800], true); await interrupted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try { using var result = await recorder.EndAsync(default); throw new Exception("Incomplete wake audio was returned."); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("interrupted")) { }
    }
    public static async Task SeededSilence()
    {
        using var drivers = new MicrophoneActivationChecks.Drivers(); using var recorder = drivers.Recorder(false, 10);
        await recorder.StartAsync(); recorder.SetWakeListening(true, (_, _, _) => { });
        var capture = await drivers.Next(); await capture.Started.Task; await capture.Emit(1600);
        await capture.Emit(new float[16000]); // Drain the high-pass filter tail before measuring silence.
        await capture.Emit(new float[24000]);
        await recorder.BeginWakeAsync(0, recorder.CaptureEpoch); var silence = recorder.WakeSilenceElapsed;
        recorder.SetWakeListening(false, null);
        Check(recorder.WakeSilenceElapsed == silence && silence.TotalSeconds >= 1, "Stopping detector erased seeded silence.");
        await recorder.CancelAsync();
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task EpochAcrossDevices()
    {
        using var first = new MicrophoneActivationChecks.Drivers(); using var second = new MicrophoneActivationChecks.Drivers();
        using var a = first.Recorder(false); using var b = second.Recorder(false);
        await a.StartAsync(); await b.StartAsync();
        a.SetWakeListening(true, (_, _, _) => { }); b.SetWakeListening(true, (_, _, _) => { });
        var da = await first.Next(); var db = await second.Next(); await da.Started.Task; await db.Started.Task;
        await da.Emit(800); await db.Emit(800);
        Check(a.CaptureEpoch != b.CaptureEpoch, "Replacement microphone reused an old detector epoch.");
    }
    public static async Task DemandAndHandoff()
    {
        using var drivers = new MicrophoneActivationChecks.Drivers(); using var recorder = drivers.Recorder(false, 10);
        await recorder.StartAsync(); Check(drivers.Count == 0, "Wake disabled opened idle mic.");
        var raw = new List<float>(); long epoch = 0;
        recorder.SetWakeListening(true, (samples, first, deviceEpoch) => { raw.AddRange(samples.ToArray()); epoch = deviceEpoch; });
        var capture = await drivers.Next(); await capture.Started.Task;
        var original = Enumerable.Range(0, 16000).Select(i => (i % 199 - 99) / 250f).ToArray();
        var expected = original.ToArray(); new AudioFilter().Process(expected, -90);
        await capture.Emit(original[..6000]); var activation = 4000L;
        await capture.Emit(original[6000..10000]);
        await recorder.BeginWakeAsync(activation, epoch);
        await capture.Emit(original[10000..15200]);
        var end = recorder.EndAsync(default); await capture.Emit(original[15200..]);
        using var result = await end.WaitAsync(TimeSpan.FromSeconds(3));
        Check(result.Samples.Span.SequenceEqual(expected.AsSpan(800)), "Wake handoff lost, duplicated or altered delayed samples.");
        Check(raw.SequenceEqual(original), "Wake detector received gated/modified audio.");
        Check(drivers.Count == 1 && !capture.Closed.Task.IsCompleted, "Wake capture used another microphone or ended shared idle demand.");
        recorder.Configure(-90, true); await capture.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        recorder.Configure(-90, false); var resumed = await drivers.Next(); await resumed.Started.Task;
        await resumed.Emit(1000);
        try { await recorder.BeginWakeAsync(activation, epoch - 1); throw new Exception("Stale device activation accepted."); } catch (InvalidOperationException) { }
        recorder.SetWakeListening(false, null); await resumed.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(float.IsNaN(recorder.LevelDb), "Disabled wake left idle microphone open.");
    }
    public static async Task BoundsAndGating()
    {
        using var drivers = new MicrophoneActivationChecks.Drivers(); using var recorder = drivers.Recorder(false, 10);
        await recorder.StartAsync(); long epoch = 0; var receivedQuiet = false;
        recorder.Configure(-10, false);
        recorder.SetWakeListening(true, (samples, _, e) => { epoch = e; if (samples.Contains(.001f)) receivedQuiet = true; });
        var capture = await drivers.Next(); await capture.Started.Task;
        await capture.Emit(Enumerable.Repeat(.001f, 496000).ToArray());
        Check(receivedQuiet, "Noise gate suppressed quiet wake audio.");
        try { await recorder.BeginWakeAsync(0, epoch); throw new Exception("Expired wake audio was silently clipped."); } catch (InvalidOperationException) { }
        await recorder.BeginWakeAsync(400000, epoch);
        try { await recorder.BeginWakeAsync(400000, epoch); throw new Exception("Concurrent wake replaced active recording."); } catch (InvalidOperationException) { }
        var end = recorder.EndAsync(default); await capture.Emit(new float[800]);
        using var audio = await end;
        Check(!audio.HasSpeech, "Wake changed the configured transcription gate.");
        recorder.SetWakeListening(false, null); await capture.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
