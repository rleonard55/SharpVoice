using System.Diagnostics;
using SharpVoice.App;
using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.Refiner.Auth;
using SharpVoice.UI;

internal static partial class WindowsOnboardingChecks
{
    internal static void RunApp(string model, string wav)
    {
        var isolated = CreateDesktopW("SharpVoiceSetupHost" + Guid.NewGuid().ToString("N"), 0, 0, 0, 0x10000000, 0);
        if (isolated == 0) throw new Exception("Cannot create isolated setup host desktop.");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { if (SetThreadDesktop(isolated) == 0) throw new Exception("Cannot enter setup host desktop."); CheckApp(model, wav); }
            catch (Exception ex) { failure = ex; }
        });
        thread.Start(); thread.Join(); CloseDesktop(isolated);
        if (failure is not null) throw failure;
        Console.WriteLine("PASS real setup host transcribes public fixture audio without selection, AI, history or insertion; focus loss drains owned capture.");
    }
    private static void CheckApp(string model, string wav)
    {
        var root = Path.Combine(Path.GetTempPath(), "SharpVoiceSetupHost-" + Guid.NewGuid().ToString("N"));
        var models = Path.Combine(root, "models"); Directory.CreateDirectory(models);
        File.Copy(model, Path.Combine(models, "ggml-tiny.en-q5_1.bin"));
        try
        {
            var config = new AppConfig { Backend = "chatgpt_oauth", MinimumAiWords = 0, LiveTranscription = false, MaxRecordingSeconds = 30 };
            using var network = new SetupRejectNetwork(); using var http = new HttpClient(network); using var auth = new ChatGptAuthManager(http);
            NativeDesktop? ownedDesktop = null;
            using var desktop = new NativeDesktop(config, foregroundWindowProvider: () => ownedDesktop?.SetupHandle ?? 0);
            ownedDesktop = desktop;
            desktop.Initialize(false, false, audioFeedback: false);
            using var drivers = new MicrophoneActivationChecks.Drivers();
            var selections = 0; var insertions = 0; var changedTarget = false;
            var environment = new VoiceEnvironment
            {
                CreateRecorder = settings => drivers.Recorder(false, settings.MaxRecordingSeconds),
                ModelDirectory = models, HistoryPath = Path.Combine(root, "history.bin"), EditorIntegration = false,
                CaptureTarget = () => new(desktop.SetupHandle, (uint)(Environment.ProcessId + (changedTarget ? 1 : 0)), 1, "SharpVoice"),
                CaptureSelection = (_, _) => { selections++; throw new Exception("Practice captured a selection."); },
                Insert = (_, _, _, _, _, _) => { insertions++; throw new Exception("Practice inserted text."); }
            };
            using var app = new SharpVoiceApp(config, desktop, http, auth, environment);
            app.Start(null); Wait(app.Preparation, 30);
            desktop.ShowSetupIfNeeded(); Click(4803); Click(4803); Click(4803);
            Check(desktop.SetupPracticeFocused, "Cannot establish isolated setup practice focus.");
            Click(4807); Until(() => Text(Item(4807)) == "Stop Recording", 10);
            var next = drivers.Next(); Wait(next, 5); var driver = next.Result;
            Wait(driver.Started.Task, 5);
            Wait(driver.Emit(ReadPcm(wav)), 5);
            Click(4807);
            for (var n = 0; n < 30 && !driver.Closed.Task.IsCompleted; n++)
            {
                Pump(); Thread.Sleep(20);
                if (driver.Closed.Task.IsCompleted) break;
                var emitted = driver.Emit(new float[1600]); Wait(Task.WhenAny(emitted, driver.Closed.Task), 5);
            }
            Wait(app.Preparation, 30);
            var transcript = Text(Item(4810));
            Check(transcript.Contains("country", StringComparison.OrdinalIgnoreCase), "Practice did not show the public JFK transcript.");
            Check(network.Count == 0 && selections == 0 && insertions == 0 && desktop.ReadHistory?.Invoke().Length == 0 && !File.Exists(environment.HistoryPath),
                "Practice accessed network, selection, AI/history or insertion.");
            Click(4807); Until(() => Text(Item(4807)) == "Stop Recording", 10);
            next = drivers.Next(); Wait(next, 5); driver = next.Result; Wait(driver.Started.Task, 5);
            Win32.SendMessageW(desktop.SetupHandle, 0x6, 0, 0);
            Wait(driver.Closed.Task, 5); Until(() => desktop.CanConfigureSetup?.Invoke() == true, 5);
            Check(Text(Item(4810)) == transcript && insertions == 0, "Canceled practice changed its result or pasted text.");
            changedTarget = true; var count = drivers.Count;
            Click(4807); Pump();
            Check(drivers.Count == count && selections == 0 && desktop.CanConfigureSetup?.Invoke() == true, "A changed practice target opened capture or selected external text.");
            desktop.StopSetupMicrophone();
            nint Item(int id) => Win32.GetDlgItem(desktop.SetupHandle, id);
            void Click(int id) => Win32.SendMessageW(desktop.SetupHandle, 0x111, (nuint)id, Item(id));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void Wait(Task task, int seconds)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(seconds)) { Pump(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new TimeoutException("Setup host fixture timed out.");
        task.GetAwaiter().GetResult(); Pump();
    }
    private static void Until(Func<bool> condition, int seconds)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(seconds)) { Pump(); Thread.Sleep(5); }
        Check(condition(), "Setup host state did not complete."); Pump();
    }
    private static float[] ReadPcm(string path)
    {
        var bytes = File.ReadAllBytes(path); var p = 12;
        while (p + 8 <= bytes.Length)
        {
            var name = System.Text.Encoding.ASCII.GetString(bytes, p, 4); var count = BitConverter.ToInt32(bytes, p + 4); p += 8;
            if (name == "data") return Enumerable.Range(0, count / 2).Select(i => BitConverter.ToInt16(bytes, p + i * 2) / 32768f).ToArray();
            p += count + (count & 1);
        }
        throw new Exception("Public fixture has no PCM audio.");
    }
    private sealed class SetupRejectNetwork : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Interlocked.Increment(ref Count); throw new Exception("Practice must not access the network."); }
    }
}
