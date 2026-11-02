using SharpVoice.Audio;
using SharpVoice.Diagnostics;
using System.IO.Compression;

internal static class WakeDiagnosticChecks
{
    public static async Task CaptureCounters()
    {
        using var drivers = new MicrophoneActivationChecks.Drivers();
        using var recorder = drivers.Recorder(false, 10, diagnostics: true);
        await recorder.StartAsync(); recorder.SetWakeListening(true, (_, _, _) => { });
        var capture = await drivers.Next(); await capture.Started.Task;
        await capture.Emit(new float[1600]); await capture.Emit(new float[800], true);
        var snapshot = recorder.Diagnostics ?? throw new Exception("Diagnostic capture counters are absent.");
        if (snapshot.Packets != 2 || snapshot.Frames != 2400 || snapshot.Discontinuities != 1 || snapshot.LastPacketAgeMs > 1000)
            throw new Exception("Capture diagnostics lost a packet, gap or liveness update.");
        if (snapshot.DeviceRate is not null) throw new Exception("Synthetic capture invented native clock metadata.");
        using var normal = drivers.Recorder(false);
        if (normal.Diagnostics is not null) throw new Exception("Production capture enables diagnostic overhead by default.");
    }
    public static Task JournalPrivacy()
    {
        var root = Path.Combine(Path.GetTempPath(), "sharpvoice-diagnostic-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var journal = new DiagnosticJournal(root, new("Hey SharpVoice", "Hey SharpVoice Dictate", -71, 2, "Windows Default", false), maxEvents: 3);
            File.WriteAllText(Path.Combine(journal.DirectoryPath, "credentials.json"), "PRIVATE_CREDENTIAL_CANARY");
            journal.Record("start", "Testing"); journal.Record("level", "Quiet"); journal.Record("gap", "Discontinuity"); journal.Record("level", "Later");
            var zip = journal.Export();
            using var archive = ZipFile.OpenRead(zip);
            if (archive.Entries.Any(x => x.FullName.Contains("credentials") || x.FullName.EndsWith(".wav"))) throw new Exception("Export included unapproved files or default audio.");
            var text = string.Join("\n", archive.Entries.Select(x => { using var reader = new StreamReader(x.Open()); return reader.ReadToEnd(); }));
            if (text.Contains("PRIVATE_CREDENTIAL_CANARY") || text.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase)) throw new Exception("Report exposed adjacent private data or a user path.");
            if (!text.Contains("events_dropped") || !text.Contains("Hey SharpVoice")) throw new Exception("Bounded journal lost settings or its truncation notice.");
            try { journal.AddClip(1, new float[16000]); throw new Exception("Audio saved without explicit opt-in."); } catch (InvalidOperationException) { }
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }
    public static Task GuidedModes()
    {
        var config = new SharpVoice.Core.AppConfig();
        var attempt = new GuidedAttempt(SharpVoice.Core.VoiceMode.Dictation, config);
        attempt.Observe(new(SharpVoice.Core.VoiceMode.AskAi, 100, 1));
        if (attempt.Outcome != "Wrong Mode") throw new Exception("A provisional short prefix falsely passed a Dictation trial.");
        attempt.Observe(new(SharpVoice.Core.VoiceMode.Dictation, 100, 1));
        if (attempt.Outcome != "Passed") throw new Exception("Longer same-onset phrase failed to confirm Dictation.");
        if (attempt.Result(endpointTimedOut: true) != "Endpoint Timeout") throw new Exception("A matched wake word concealed a failed speech endpoint.");
        var noise = new GuidedAttempt(null, config); noise.Observe(new(SharpVoice.Core.VoiceMode.AskAi, 100, 1));
        if (noise.Outcome != "False Activation") throw new Exception("Noise trial concealed a false activation.");
        var missed = new GuidedAttempt(SharpVoice.Core.VoiceMode.AskAi, config);
        if (missed.Outcome != "Missed") throw new Exception("No detection falsely passed a trial.");
        var final = new GuidedAttempt(SharpVoice.Core.VoiceMode.Dictation, config);
        final.Observe(new(SharpVoice.Core.VoiceMode.AskAi, 100, 1));
        if (final.Complete("Hey SharpVoice Dictate, bring the folder") != "Passed" || final.FinalMode != SharpVoice.Core.VoiceMode.Dictation)
            throw new Exception("Diagnostic final mode omitted production prefix resolution.");
        return Task.CompletedTask;
    }
    public static async Task EarlyStop()
    {
        var root = Path.Combine(Path.GetTempPath(), "sharpvoice-diag-stop-" + Guid.NewGuid().ToString("N"));
        DiagnosticSession? session = null; Task? stopping = null; var prepared = 0;
        try
        {
            session = new(root, new DiagnosticEnvironment
            {
                MainAppRunning = () => { stopping = session!.StopAsync(); return false; },
                PrepareModels = (_, _, _) => { Interlocked.Increment(ref prepared); throw new Exception("Canceled start prepared models."); },
                CreateRecorder = (_, _) => throw new Exception("Canceled start opened capture.")
            });
            await session.StartAsync(new("Hey SharpVoice", "Hey SharpVoice Dictate", -65, 2, "Default", false), "");
            await stopping!.WaitAsync(TimeSpan.FromSeconds(5));
            if (prepared != 0 || session.View.Running) throw new Exception("Stop missed a start registered before model preparation.");
        }
        finally { if (session is not null) await session.DisposeAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    public static async Task FaultedShutdown()
    {
        var released = false;
        var error = await DiagnosticShutdown.RunAsync(Task.FromException(new IOException("Worker fault racing Stop")), () => { released = true; return Task.CompletedTask; });
        if (!released || error is not IOException) throw new Exception("A failed worker prevented shutdown or lost the failure.");
    }
    public static async Task MainAppGuard()
    {
        var root = Path.Combine(Path.GetTempPath(), "sharpvoice-diag-guard-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var session = new DiagnosticSession(root, new DiagnosticEnvironment
            {
                MainAppRunning = () => true,
                CreateRecorder = (_, _) => throw new Exception("Main-app guard opened a microphone."),
                PrepareModels = (_, _, _) => throw new Exception("Main-app guard prepared models.")
            });
            await session.StartAsync(new("Hey SharpVoice", "Hey SharpVoice Dictate", -65, 2, "Default", false), "");
            if (session.View.Running || !session.View.CanExport || !session.View.Status.Contains("Exit SharpVoice")) throw new Exception("Blocked diagnostic start is not explained/exportable.");
            session.Export();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    public static Task AudioBounds()
    {
        var root = Path.Combine(Path.GetTempPath(), "sharpvoice-diag-clips-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var journal = new DiagnosticJournal(root, new("Hey SharpVoice", "Hey SharpVoice Dictate", -65, 2, "Default", true));
            journal.AddClip(1, new float[16000 * 25]);
            for (var i = 2; i <= 23; i++) journal.AddClip(i, new float[16]);
            using var archive = ZipFile.OpenRead(journal.Export());
            if (archive.Entries.Count(x => x.FullName.EndsWith(".wav")) != 20 || archive.GetEntry("attempt-001.wav")?.Length != 640044)
                throw new Exception("Opt-in audio exceeded the clip count or 20-second WAV bound.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        return Task.CompletedTask;
    }
}
