using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using SharpVoice.Audio;
using SharpVoice.Core;

internal static class MicrophoneActivationChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static Task Configuration()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new AppConfig(), CoreJsonContext.Default.AppConfig));
        Check(json.RootElement.GetProperty("microphone_activation").GetString() == "on_hotkey", "Default must be On Hotkey.");
        var path = Path.Combine(Path.GetTempPath(), "sharpvoice-activation-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ConfigManager.Save(new AppConfig { MicrophoneActivation = "keep_ready" }, path);
            Check(ConfigManager.Load(path).MicrophoneActivation == "keep_ready", "Explicit Keep Ready was lost.");
            var legacy = File.ReadAllText(path).Replace("\"microphone_activation\": \"keep_ready\",", "");
            File.WriteAllText(path, legacy);
            Check(ConfigManager.Load(path).MicrophoneActivation == "on_hotkey", "Legacy config did not gain On Hotkey.");
            ConfigManager.Save(new AppConfig(), path);
            Check(ConfigManager.Load(path).MicrophoneActivation == "on_hotkey", "On Hotkey did not round-trip.");
            var rejected = false;
            try { (new AppConfig { MicrophoneActivation = "always" }).Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid activation accepted.");
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }

    public static async Task KeepReadyStartup()
    {
        using var factory = new Drivers { BlockOpening = true };
        using var recorder = factory.Recorder(keepReady: true);
        var start = recorder.StartAsync(); var opening = await factory.Next();
        Check(!start.IsCompleted, "Keep Ready reported ready before its native device opened.");
        factory.OpenGate.Set(); await start.WaitAsync(TimeSpan.FromSeconds(3));
        Check(opening.Started.Task.IsCompleted, "Keep Ready startup did not acknowledge native Start.");
        using var failed = new WasapiAudioRecorder(-90, 2, "fixture", true, (_, _) => throw new IOException("Missing device"));
        var rejected = false;
        try { await failed.StartAsync(); } catch (IOException) { rejected = true; }
        Check(rejected, "Keep Ready startup did not propagate unavailable-device failure.");
    }

    public static async Task OnHotkey()
    {
        using var factory = new Drivers();
        using var recorder = factory.Recorder(keepReady: false);
        await recorder.StartAsync();
        await Task.Delay(30);
        Check(factory.Count == 0 && float.IsNaN(recorder.LevelDb), "Idle On Hotkey opened a device or showed a stale level.");
        await recorder.BeginAsync().WaitAsync(TimeSpan.FromSeconds(3));
        var session = await factory.Next();
        Check(session.Started.Task.IsCompleted, "Recording acknowledged before native Start.");
        await session.Emit(1600);
        var end = recorder.EndAsync(default);
        await session.Emit(1000);
        using var audio = await end;
        Check(audio.Samples.Length == 2400 && audio.HasSpeech, "Recording/tail length or speech was lost.");
        await session.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(float.IsNaN(recorder.LevelDb), "Finished capture left stale meter level.");
        await recorder.BeginAsync();
        var next = await factory.Next();
        recorder.Cancel();
        await next.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(factory.Count == 2, "On Hotkey reopened a device without a request.");
    }

    public static async Task PreviewAndKeepReady()
    {
        using var factory = new Drivers();
        using var recorder = factory.Recorder(keepReady: false);
        await recorder.StartAsync();
        recorder.SetPreview(true);
        var preview = await factory.Next();
        await preview.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await preview.Emit(5000);
        Check(float.IsFinite(recorder.LevelDb), "Explicit preview did not meter.");
        recorder.SetPreview(false);
        await preview.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await recorder.BeginAsync();
        var recording = await factory.Next();
        var end = recorder.EndAsync(default); await recording.Emit(1000);
        using var audio = await end;
        Check(audio.Samples.Length == 800, "Preview leaked pre-roll into On Hotkey dictation.");
        await recording.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        recorder.SetPreview(true); var paused = await factory.Next(); await paused.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        recorder.Configure(-90, true); await paused.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        recorder.Configure(-90, false); await Task.Delay(30);
        Check(factory.Count == 3, "Resume implicitly restarted On Hotkey preview.");

        using var warmFactory = new Drivers();
        using var warm = warmFactory.Recorder(keepReady: true);
        await warm.StartAsync(); var ready = await warmFactory.Next(); await ready.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await ready.Emit(5000);
        await warm.BeginAsync();
        var finish = warm.EndAsync(default); await ready.Emit(1000);
        using var withPreRoll = await finish;
        Check(withPreRoll.Samples.Length == 4800, "Keep Ready lost its 250 ms pre-roll.");
        Check(!ready.Closed.Task.IsCompleted, "Keep Ready closed after dictation.");
        warm.Configure(-90, true); await ready.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        warm.Configure(-90, false); var resumed = await warmFactory.Next(); await resumed.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    public static async Task StartupCancellationAndFailure()
    {
        using var factory = new Drivers { BlockOpening = true };
        using var recorder = factory.Recorder(keepReady: false);
        await recorder.StartAsync();
        var begin = recorder.BeginAsync();
        var opening = await factory.Next();
        Check(!begin.IsCompleted, "Start was acknowledged before opening completed.");
        recorder.Cancel(); factory.OpenGate.Set();
        try { await begin; throw new Exception("Cancelled opening succeeded."); } catch (OperationCanceledException) { }
        await opening.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(!opening.Started.Task.IsCompleted, "Cancelled device opening still started native capture.");
        factory.BlockOpening = false; factory.BlockStarting = true;
        var wait = recorder.BeginAsync(); var starting = await factory.Next(); await starting.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(!wait.IsCompleted, "Start cue could occur before native Start completed.");
        recorder.Configure(-90, true); recorder.Cancel(); factory.StartGate.Set();
        try { await wait; throw new Exception("Paused startup succeeded."); } catch (OperationCanceledException) { }
        await starting.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using var closingFactory = new Drivers { BlockDisposing = true };
        using var closing = closingFactory.Recorder(keepReady: false);
        await closing.StartAsync(); closing.SetPreview(true);
        var old = await closingFactory.Next(); await old.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        closing.SetPreview(false); await old.Disposing.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var restarting = closing.BeginAsync();
        Check(!restarting.IsCompleted, "New recording used a stale ready flag while a device was closing.");
        closingFactory.DisposeGate.Set();
        await restarting.WaitAsync(TimeSpan.FromSeconds(3));
        var fresh = await closingFactory.Next();
        Check(fresh.Started.Task.IsCompleted, "Restart did not wait for its own native session.");
        closingFactory.DisposeGate.Reset();
        var stopped = closing.EndAsync(default); await fresh.Emit(1000);
        await fresh.Disposing.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.WhenAny(stopped, Task.Delay(50));
        Check(!stopped.IsCompleted, "On Hotkey completed before native disposal finished.");
        closingFactory.DisposeGate.Set();
        using var finished = await stopped.WaitAsync(TimeSpan.FromSeconds(3));

        using var cancelFactory = new Drivers { BlockDisposing = true };
        using var cancelled = cancelFactory.Recorder(keepReady: false);
        await cancelled.StartAsync(); await cancelled.BeginAsync(); var stopping = await cancelFactory.Next();
        var cancellation = cancelled.CancelAsync();
        await stopping.Disposing.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.WhenAny(cancellation, Task.Delay(50));
        Check(!cancellation.IsCompleted, "Cancellation reported idle before native disposal finished.");
        cancelFactory.DisposeGate.Set(); await cancellation.WaitAsync(TimeSpan.FromSeconds(3));

        using var failed = new WasapiAudioRecorder(-90, 2, "fixture", false, (_, _) => throw new IOException("Missing device"));
        var error = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        failed.Failed += _ => error.TrySetResult();
        await failed.StartAsync();
        var rejected = false;
        try { await failed.BeginAsync(); } catch (IOException) { rejected = true; }
        await error.Task;
        Check(rejected && !failed.IsAvailable, "Device failure did not fail pending recording.");
    }

    public static async Task DraftSnapshot()
    {
        using var drivers = new Drivers(); using var recorder = drivers.Recorder(false);
        await recorder.StartAsync(); var armed = recorder.BeginAsync(); var driver = await drivers.Next(); await driver.Started.Task; await armed;
        Check(recorder.Snapshot(1) is null, "Empty recording produced a live draft snapshot.");
        await driver.Emit(24000); using var draft = recorder.Snapshot(1) ?? throw new Exception("Recorded audio has no draft snapshot.");
        Check(draft.Samples.Length == 16000 && draft.HasSpeech, "Draft audio window is not bounded to the requested tail.");
        var expected = draft.Samples.ToArray(); var ending = recorder.EndAsync(default); await driver.Emit(800);
        using(var final = await ending) Check(final.Samples.Length == 24800, "Draft snapshot consumed original audio.");
        Check(draft.Samples.Span.SequenceEqual(expected), "Final-buffer return cleared the independently owned draft audio.");
        Check(recorder.Snapshot(1) is null, "Stopped recording leaked an obsolete draft.");
    }
    internal sealed class Drivers : IDisposable
    {
        private readonly Channel<Driver> created = Channel.CreateUnbounded<Driver>();
        public readonly ManualResetEventSlim OpenGate = new(false), StartGate = new(false), DisposeGate = new(false);
        public volatile bool BlockOpening, BlockStarting, BlockDisposing;
        private int count;
        public int Count => Volatile.Read(ref count);
        public WasapiAudioRecorder Recorder(bool keepReady, int maxSeconds = 2, bool diagnostics = false) => new(-90, maxSeconds, "fixture", keepReady, Create, diagnostics);
        private IAudioCaptureSession Create(AutoResetEvent ready, string device)
        {
            var driver = new Driver(ready, this);
            Interlocked.Increment(ref count); created.Writer.TryWrite(driver);
            if (BlockOpening && !OpenGate.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Fixture opening blocked.");
            return driver;
        }
        public async Task<Driver> Next(int seconds = 3) => await created.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(seconds));
        public void Dispose() { OpenGate.Set(); StartGate.Set(); DisposeGate.Set(); OpenGate.Dispose(); StartGate.Dispose(); DisposeGate.Dispose(); }
    }
    internal sealed class Driver(AutoResetEvent ready, Drivers owner) : IAudioCaptureSession
    {
        private readonly ConcurrentQueue<(float[] Data, bool Discontinuity, TaskCompletionSource Done)> chunks = new();
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously),
            StartEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), Disposing = new(TaskCreationOptions.RunContinuationsAsynchronously), Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Start()
        {
            StartEntered.TrySetResult();
            if (owner.BlockStarting && !owner.StartGate.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Fixture Start blocked.");
            Started.TrySetResult();
        }
        public Task Emit(int count)
        {
            var data = Enumerable.Range(0,count).Select(i => i % 2 == 0 ? .5f : -.5f).ToArray();
            return Emit(data);
        }
        public Task Emit(float[] data, bool discontinuity = false)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            chunks.Enqueue((data,discontinuity,done)); ready.Set();
            return done.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        public void Drain(AudioSamplesHandler consume)
        {
            while (chunks.TryDequeue(out var chunk)) { consume(chunk.Data, chunk.Discontinuity); chunk.Done.TrySetResult(); }
        }
        public void Dispose()
        {
            Disposing.TrySetResult();
            if (owner.BlockDisposing && !owner.DisposeGate.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Fixture disposal blocked.");
            Closed.TrySetResult();
        }
    }
}
