using System.Diagnostics;
using SharpVoice.App;
using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.Refiner.Auth;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    public static void RunWakeApp(string modelRoot, string dictateWav, string askWav, string outputRoot, bool repeat = false, bool noisy = false, int cycles = 6)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var isolated = CreateDesktopW("SharpVoiceWakeCheck" + Guid.NewGuid().ToString("N"), 0, 0, 0, 0x10000000, 0);
        if (isolated == 0) throw new Exception("Cannot create isolated wake app desktop.");
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (SetThreadDesktop(isolated) == 0) throw new Exception("Cannot enter wake fixture desktop.");
                var config = new AppConfig { WakePhrasesEnabled = true, WakeSilenceSeconds = cycles == 1 ? 10 : 2, LiveTranscription = repeat, MicrophoneActivation = repeat ? "keep_ready" : "on_hotkey", Backend = "none", MaxRecordingSeconds = noisy ? 120 : 20, NoiseGateDb = noisy ? -71 : -65 };
                using var http = new HttpClient(new RejectNetwork()); using var auth = new ChatGptAuthManager(http);
                using var desktop = new NativeDesktop(config); desktop.Initialize(false, false, audioFeedback: false);
                Console.WriteLine("WAKE APP desktop ready");
                using var drivers = new MicrophoneActivationChecks.Drivers();
                var inserted = new List<string>();
                SharpVoice.Audio.WasapiAudioRecorder? recorder = null;
                var environment = new VoiceEnvironment
                {
                    CreateRecorder = settings =>
                    {
                        recorder = drivers.Recorder(repeat, settings.MaxRecordingSeconds);
                        recorder.Configure(settings.NoiseGateDb, false); return recorder;
                    },
                    CaptureTarget = () => new(1, (uint)(Environment.ProcessId + 1), 1, "notepad"),
                    CaptureSelection = (_, _) => Task.FromResult<SelectionSnapshot?>(null),
                    Insert = (text, _, _, _, _, _) => { lock (inserted) inserted.Add(text); return Task.CompletedTask; },
                    HistoryPath = Path.Combine(outputRoot, "history.bin"), WakeModelRoot = modelRoot, EditorIntegration = false
                };
                using var app = new SharpVoiceApp(config, desktop, http, auth, environment);
                Console.WriteLine("WAKE APP coordinator created");
                app.Start(null); Wait(app.Preparation, 30);
                Console.WriteLine("WAKE APP preparation complete");
                MicrophoneActivationChecks.Driver? activeDriver = null;
                void Feed(string wav)
                {
                    int insertedBefore; lock (inserted) insertedBefore = inserted.Count;
                    Until(() => recorder?.IsWakeListening == true, 20);
                    if (activeDriver is null || activeDriver.Closed.Task.IsCompleted)
                    {
                        var next = drivers.Next(15); Wait(next, 20); activeDriver = next.Result;
                    }
                    var driver = activeDriver; Wait(driver.Started.Task, 5);
                    Console.WriteLine("WAKE APP fake microphone ready: " + Path.GetFileName(wav));
                    var samples = ReadWakeWav(wav); var audio = new float[samples.Length + 16000 * (config.WakeSilenceSeconds + 2)]; samples.CopyTo(audio, 0);
                    if (noisy)
                    {
                        // Continuous background above a permissive gate must not keep
                        // a completed spoken request recording until the hard limit.
                        var random = new Random(741);
                        for (var p = 0; p < audio.Length; p++) audio[p] += (float)(random.NextDouble() * 2 - 1) * .0015f;
                    }
                    for (var p = 0; p < audio.Length && !driver.Closed.Task.IsCompleted; p += 1600)
                    {
                        var sent = driver.Emit(audio.AsSpan(p, Math.Min(1600, audio.Length - p)).ToArray());
                        try { Wait(sent, 5); } catch (TimeoutException) when (driver.Closed.Task.IsCompleted) { break; }
                        Wait(Task.Delay(100), 2);
                        if (cycles == 1 && p < samples.Length - 16000)
                            lock (inserted) if (inserted.Count != insertedBefore) throw new Exception("Continuous speech was finalized before the request ended.");
                    }
                    Console.WriteLine("WAKE APP audio fed: " + Path.GetFileName(wav));
                }
                void Complete(Func<bool> completed, int seconds)
                {
                    var watch = Stopwatch.StartNew(); var noise = new Random(952);
                    while (!completed() && watch.Elapsed.TotalSeconds < seconds)
                    {
                        var driver = activeDriver;
                        if (driver is not null && !driver.Closed.Task.IsCompleted)
                        {
                            var tail = new float[1600];
                            if (noisy) for (var p = 0; p < tail.Length; p++) tail[p] = (float)(noise.NextDouble() * 2 - 1) * .0015f;
                            try { Wait(driver.Emit(tail), 5); } catch (TimeoutException) when (driver.Closed.Task.IsCompleted) { }
                        }
                        Wait(Task.Delay(100), 2);
                    }
                    if (!completed()) throw new TimeoutException("Wake app did not finish while capture frames continued.");
                }
                Feed(dictateWav);
                Complete(() => { lock (inserted) return inserted.Count == 1; }, 30);
                string result; lock (inserted) result = inserted[0];
                if (!result.Contains("blue folder", StringComparison.OrdinalIgnoreCase) || result.Contains("sharp", StringComparison.OrdinalIgnoreCase) || result.Contains("dictate", StringComparison.OrdinalIgnoreCase)) throw new Exception("App did not remove wake phrase or preserve continuous request: " + result);
                Console.WriteLine("PASS actual App dictation with AI disabled: " + result);
                Feed(askWav);
                Complete(() => WakeWindowText(Win32.GetDlgItem(desktop.SettingsHandle, 1024)).Contains("Ask AI needs", StringComparison.OrdinalIgnoreCase), 30);
                lock (inserted) if (inserted.Count != 1) throw new Exception("Ask without provider inserted or repeated the prior dictation.");
                Console.WriteLine("PASS actual App resolves short Ask phrase before provider validation; no network or paste.");
                if (repeat)
                {
                    for (var cycle = 2; cycle <= cycles; cycle++)
                    {
                        Feed(dictateWav);
                        Complete(() => { lock (inserted) return inserted.Count == cycle; }, 30);
                        Feed(askWav);
                        Complete(() => WakeWindowText(Win32.GetDlgItem(desktop.SettingsHandle, 1024)).Contains("Ask AI needs", StringComparison.OrdinalIgnoreCase), 30);
                        Console.WriteLine("PASS repeated Keep Ready + live transcription cycle " + cycle);
                    }
                    lock (inserted) if (inserted.Count != cycles) throw new Exception("Repeated wake session inserted duplicate text.");
                }
                Wait(Task.Delay(1000), 3); // Drain final UI callbacks before orderly coordinator disposal.
            }
            catch (Exception ex) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
        });
        thread.Start(); thread.Join(); CloseDesktop(isolated); failure?.Throw();
    }
    private static void Wait(Task task, int seconds)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(seconds)) { WakePump(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new TimeoutException("Wake fixture operation timed out.");
        task.GetAwaiter().GetResult(); WakePump();
    }
    private static void Until(Func<bool> condition, int seconds)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(seconds)) { WakePump(); Thread.Sleep(10); }
        if (!condition()) throw new TimeoutException("Wake fixture expected app outcome did not arrive.");
    }
    private static float[] ReadWakeWav(string path)
    {
        var bytes = File.ReadAllBytes(path); var p = 12;
        while (p + 8 <= bytes.Length)
        {
            var name = System.Text.Encoding.ASCII.GetString(bytes, p, 4); var count = BitConverter.ToInt32(bytes, p + 4); p += 8;
            if (name == "data") return Enumerable.Range(0, count / 2).Select(i => BitConverter.ToInt16(bytes, p + i * 2) / 32768f).ToArray();
            p += count + (count & 1);
        }
        throw new Exception("Fixture has no PCM audio.");
    }
    private static unsafe void WakePump()
    {
        // A live HUD posts animation timers continuously. Bound each pump so
        // prerecorded audio delivery and timeout checks cannot starve behind it.
        var msg = new Win32.Msg();
        for (var i = 0; i < 16 && PeekMessageW(&msg, 0, 0, 0, 1) != 0; i++)
        { Win32.TranslateMessage(msg); Win32.DispatchMessageW(msg); }
    }
    private static unsafe string WakeWindowText(nint window)
    {
        var buffer = new char[4096]; fixed (char* ptr = buffer) return new string(buffer, 0, Win32.GetWindowTextW(window, ptr, buffer.Length));
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new Exception("Unexpected external request in wake app fixture.");
    }
}
