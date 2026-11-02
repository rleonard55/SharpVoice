using SharpVoice.Diagnostics;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

internal static partial class WindowsHelpSmoke
{
    public static void RunWakeDiagnosticsUi(string output)
    {
        var isolated = CreateDesktopW("SharpVoiceDiagnosticsCheck" + Guid.NewGuid().ToString("N"), 0, 0, 0, 0x10000000, 0);
        if (isolated == 0) throw new Exception("Cannot create diagnostic fixture desktop.");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (SetThreadDesktop(isolated) == 0) throw new Exception("Cannot enter diagnostic fixture desktop.");
                var controller = new FakeDiagnosticController();
                using var window = new DiagnosticWindow(controller); window.Initialize(true);
                void Click(int id) { Win32.SendMessageW(window.Handle, 0x111, (nuint)id, 0); Wait(Task.Delay(500), 3); }
                Click(110);
                if (!controller.View.Running) throw new Exception("Diagnostic Start did not reach its controller.");
                Click(113);
                if (controller.Expected != VoiceMode.Dictation || !controller.View.AttemptActive) throw new Exception("Guided Dictation button was not routed.");
                Click(116);
                if (!controller.Missed || controller.View.AttemptActive) throw new Exception("Mark Missed failed to finish the trial.");
                if (RenderHelp(window.Handle, output, 40) == 0) throw new Exception("Diagnostic screenshot rendered black.");
                if (!WakeWindowText(Win32.GetDlgItem(window.Handle, 120)).Contains("Missed")) throw new Exception("Diagnostic outcome is not visible.");
                Click(111); if (controller.View.Running) throw new Exception("Stop failed to release the diagnostic session.");
                Click(119);
                Console.WriteLine("PASS diagnostic UI Start/Dictation/Mark Missed/Stop/Close and screenshot; no microphone or account used.");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.Start(); thread.Join(); CloseDesktop(isolated); if (failure is not null) throw failure;
    }
    private sealed class FakeDiagnosticController : IDiagnosticController
    {
        public DiagnosticView View { get; private set; } = new("Ready", "Microphone closed", "No events yet", false, false, false);
        public VoiceMode? Expected; public bool Missed;
        public string? LogDirectory => null;
        public Task StartAsync(DiagnosticSettings settings, string deviceId) { View = new("Listening", "Packets: 120 | Gaps: 0 | Detector queue: 0ms\r\nNo audio saved", "1.0s listening: Real capture ready", true, false, true); return Task.CompletedTask; }
        public Task StopAsync() { View = View with { Running = false, AttemptActive = false, Status = "Stopped" }; return Task.CompletedTask; }
        public Task BeginAttemptAsync(VoiceMode? expected) { Expected = expected; View = View with { AttemptActive = true, Status = "Say the test phrase" }; return Task.CompletedTask; }
        public Task FinishAttemptAsync(bool missed) { Missed = missed; View = View with { AttemptActive = false, Status = "Attempt 1: Missed", Events = "1.0s listening\r\n2.0s trial_start: expected Dictation\r\n4.0s trial_result: Missed (User Marked)\r\n4.8s rearm: listening again" }; return Task.CompletedTask; }
        public string Export() => throw new Exception("UI smoke must not open Explorer.");
    }
    public static async Task RunDiagnosticSession(string model, string dictateWav, string askWav, string output, bool expectTimeout = false)
    {
        var probe = new SharpVoice.Speech.WakeKeywordDetector(model, "Hey SharpVoice Dictate", "Hey SharpVoice", diagnostics: true);
        try
        {
            if (probe.Enqueue(new float[32000], 0, 1)) throw new Exception("Oversized diagnostic detector block was accepted.");
            probe.Reset(1); probe.Enqueue(new float[1600], 0, 1);
            var until = System.Diagnostics.Stopwatch.StartNew();
            while (probe.Diagnostics?.Processed == 0 && until.Elapsed.TotalSeconds < 5) await Task.Delay(20);
            var counters = probe.Diagnostics!;
            if (counters.Dropped != 1 || counters.Resets < 1 || counters.Enqueued != 1 || counters.Processed != 1 || counters.LastWorkMs <= 0)
                throw new Exception("Native detector diagnostic overload/reset/work counters are incorrect.");
        }
        finally { await probe.DisposeAsync(); }
        if (probe.Diagnostics?.WorkerCompleted != true) throw new Exception("Diagnostic detector concealed worker completion.");
        Console.WriteLine("PASS native detector diagnostic overflow/reset/work/completion counters");
        using var drivers = new MicrophoneActivationChecks.Drivers();
        var environment = new DiagnosticEnvironment
        {
            MainAppRunning = () => false, PrepareModels = (_, _, _) => Task.FromResult(model),
            CreateRecorder = (s, _) => { var capture = drivers.Recorder(true, 30, diagnostics: true); capture.Configure(s.NoiseGateDb, false); return capture; }
        };
        await using var session = new DiagnosticSession(output, environment);
        await session.StartAsync(new("Hey SharpVoice", "Hey SharpVoice Dictate", -71, 2, "Synthetic Microphone", false, true), "fixture");
        if (!session.View.Running) throw new Exception("Diagnostic controller did not become ready: " + session.View.Status);
        var driver = await drivers.Next(); await driver.Started.Task;
        for (var cycle = 0; cycle < (expectTimeout ? 1 : 3); cycle++)
        {
            foreach (var (mode, wav) in new[] { (VoiceMode.Dictation, dictateWav), (VoiceMode.AskAi, askWav) }.Take(expectTimeout ? 1 : 2))
            {
                await session.BeginAttemptAsync(mode);
                var speech = ReadWakeWav(wav);
                var samples = new float[expectTimeout ? Math.Min(speech.Length, 16000 * 23) : speech.Length + 16000 * 4];
                speech.AsSpan(0, Math.Min(speech.Length, samples.Length)).CopyTo(samples);
                var noise = new Random(741);
                for (var i = 0; i < samples.Length; i++) samples[i] += (float)(noise.NextDouble() * 2 - 1) * .0015f;
                for (var i = 0; i < samples.Length; i += 1600)
                {
                    await driver.Emit(samples.AsSpan(i, Math.Min(1600, samples.Length - i)).ToArray()); await Task.Delay(100);
                }
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (session.View.AttemptActive && watch.Elapsed.TotalSeconds < 8)
                {
                    // A real microphone keeps delivering frames while Whisper is
                    // busy; EndAsync still needs its capture post-roll to arrive.
                    var tail = new float[1600];
                    for (var j = 0; j < tail.Length; j++) tail[j] = (float)(noise.NextDouble() * 2 - 1) * .0015f;
                    await driver.Emit(tail); await Task.Delay(100);
                }
                if (session.View.AttemptActive || !(expectTimeout ? session.View.Status.Contains("Endpoint Timeout") : session.View.Status.Contains("Passed")))
                    throw new Exception("Diagnostic guided trial failed: " + session.View.Status);
                if (expectTimeout && session.View.Events.Contains("trial_result") && session.View.Status.Contains("Passed")) throw new Exception("Endpoint timeout falsely passed.");
                Console.WriteLine($"PASS real diagnostic controller {mode} cycle {cycle + 1}");
            }
        }
        await session.StopAsync(); await driver.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var zip = session.Export();
        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        if (archive.Entries.Any(x => x.FullName.EndsWith(".wav"))) throw new Exception("Default diagnostic fixture leaked audio.");
        Console.WriteLine("PASS diagnostic microphone release and metadata-only report: " + zip);
    }
}
