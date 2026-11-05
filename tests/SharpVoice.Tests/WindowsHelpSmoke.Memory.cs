using System.Diagnostics;
using SharpVoice.App;
using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Refiner.Auth;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    public static void RunIdleMemoryApp(string modelRoot, string activation)
    {
        if (activation is not ("keep_ready" or "on_hotkey")) throw new ArgumentException("Choose a microphone activation mode.");
        var isolated = CreateDesktopW("SharpVoiceMemoryCheck" + Guid.NewGuid().ToString("N"), 0, 0, 0, 0x10000000, 0);
        if (isolated == 0) throw new Exception("Cannot create isolated memory-check desktop.");
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (SetThreadDesktop(isolated) == 0) throw new Exception("Cannot enter memory-check desktop.");
                CheckIdleMemory(modelRoot, activation == "keep_ready");
            }
            catch (Exception ex) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
        });
        thread.Start(); thread.Join(); CloseDesktop(isolated); failure?.Throw();
    }
    private static void CheckIdleMemory(string modelRoot, bool keepReady)
    {
        var root = Path.Combine(Path.GetTempPath(), "sharpvoice-memory-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var config = new AppConfig { Backend = "none", SpeechModel = "tiny.en-q5_1", MicrophoneActivation = keepReady ? "keep_ready" : "on_hotkey", LiveTranscription = true };
            using var http = new HttpClient(new RejectNetwork()); using var auth = new ChatGptAuthManager(http);
            using var desktop = new NativeDesktop(config); desktop.Initialize(false, false, audioFeedback: false);
            using var drivers = new MicrophoneActivationChecks.Drivers();
            var trims = 0; var insertions = 0; var safe = true; double before = 0, after = 0;
            SharpVoiceApp? current = null;
            var environment = new VoiceEnvironment
            {
                CreateRecorder = settings => { var recorder = drivers.Recorder(keepReady, settings.MaxRecordingSeconds); recorder.Configure(settings.NoiseGateDb, false); return recorder; },
                CaptureTarget = () => new(1, (uint)(Environment.ProcessId + 1), 1, "notepad"),
                CaptureSelection = (_, _) => Task.FromResult<SelectionSnapshot?>(null),
                Insert = (_, _, _, _, _, _) => { insertions++; return Task.CompletedTask; },
                ModelDirectory = modelRoot, HistoryPath = Path.Combine(root, "history.bin"), EditorIntegration = false,
                TrimMemory = () =>
                {
                    safe &= current!.UpdateIdle;
                    using var process = Process.GetCurrentProcess(); process.Refresh(); before = process.WorkingSet64 / 1048576d;
                    if (!MemoryTrimmer.Trim()) throw new Exception("Windows refused idle working-set trim.");
                    process.Refresh(); after = process.WorkingSet64 / 1048576d; trims++;
                }
            };
            using var app = new SharpVoiceApp(config, desktop, http, auth, environment); current = app;
            app.Start(null); Wait(app.Preparation, 30); Until(() => app.IsMicrophoneReady && app.UpdateIdle, 10);
            MicrophoneActivationChecks.Driver? driver = null;
            for (var cycle = 0; cycle < 2; cycle++)
            {
                var previous = trims;
                desktop.VoicePressed!(VoiceMode.Dictation);
                if (driver is null || driver.Closed.Task.IsCompleted) { var next = drivers.Next(); Wait(next, 5); driver = next.Result; }
                Wait(driver.Started.Task, 5); Pump(); Wait(Task.Delay(3000), 5);
                if (trims != previous) throw new Exception("Memory cleanup ran during recording.");
                Wait(driver.Emit(new float[1600]), 5);
                desktop.Released!();
                Wait(driver.Emit(new float[1000]), 5);
                Until(() => app.UpdateIdle, 15);
                var deadline = Stopwatch.StartNew();
                while (trims == previous && deadline.Elapsed < TimeSpan.FromSeconds(5)) { Pump(); Thread.Sleep(10); }
                if (trims != previous + 1 || !safe) throw new Exception("Silent hotkey release did not reclaim memory after background work drained.");
                if (insertions != 0 || desktop.ReadHistory?.Invoke().Length != 0 || File.Exists(environment.HistoryPath)) throw new Exception("Silent hotkey created text, history or insertion.");
                Console.WriteLine($"IDLE_MEMORY keep_ready={keepReady} cycle={cycle + 1} before_mib={before:F2} after_mib={after:F2}");
                Wait(Task.Delay(2500), 4);
                if (trims != previous + 1) throw new Exception("Idle cleanup repeated without another recording.");
                using var settled = Process.GetCurrentProcess(); settled.Refresh();
                Console.WriteLine($"IDLE_MEMORY settled_mib={settled.WorkingSet64 / 1048576d:F2}");
            }
            var beforeCancel = trims;
            desktop.VoicePressed!(VoiceMode.Dictation);
            if (!keepReady) { var next = drivers.Next(); Wait(next, 5); driver = next.Result; }
            Wait(driver!.Started.Task, 5);
            desktop.CancelRequested!();
            Until(() => app.UpdateIdle, 15);
            Until(() => trims == beforeCancel + 1, 5);
            if (!safe || insertions != 0) throw new Exception("Canceled capture cleanup interrupted work or inserted text.");
            Console.WriteLine("PASS silent hotkey memory cleanup, repeated recording, no idle churn and active recording guard: " + config.MicrophoneActivation);
        }
        finally { Directory.Delete(root, true); }
    }
}
