using System.Collections.Concurrent;
using System.Diagnostics;
using SharpVoice.Audio;
using SharpVoice.Core;
using SharpVoice.Speech;

namespace SharpVoice.Diagnostics;

internal sealed record DiagnosticView(string Status, string Health, string Events, bool Running, bool AttemptActive, bool CanExport);
internal interface IDiagnosticController
{
    DiagnosticView View { get; }
    Task StartAsync(DiagnosticSettings settings, string deviceId);
    Task StopAsync();
    Task BeginAttemptAsync(VoiceMode? expected);
    Task FinishAttemptAsync(bool missed);
    string Export();
    string? LogDirectory { get; }
}

internal sealed class DiagnosticSession : IDiagnosticController, IAsyncDisposable
{
    private readonly SemaphoreSlim serial = new(1, 1);
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly ConcurrentQueue<WakeDetection> hits = new();
    private readonly ConcurrentQueue<string> errors = new();
    private readonly object clipGate = new();
    private readonly object lifecycle = new();
    private readonly string root;
    private readonly DiagnosticEnvironment environment;
    private DiagnosticJournal? journal;
    private DiagnosticSettings settings = new("Hey SharpVoice", "Hey SharpVoice Dictate", -65, 2, "Windows Default", false);
    private WasapiAudioRecorder? recorder;
    private readonly WhisperEngine speech = new();
    private WakeWhisperDetector? detector;
    private CancellationTokenSource? cancellation;
    private Task loop = Task.CompletedTask;
    private Task starting = Task.CompletedTask;
    private GuidedAttempt? attempt;
    private AudioBufferRing? clip;
    private int attemptNumber;
    private bool recording, running;
    private string status = "Choose a microphone, then click Start Listening. Close SharpVoice first.";
    private long attemptAt, sessionAt, lastHealth;
    private DiagnosticView view = new("Choose a microphone, then click Start Listening. Close SharpVoice first.", "Microphone closed · No audio saved", "", false, false, false);
    public DiagnosticView View => Volatile.Read(ref view);
    public string? LogDirectory => journal?.DirectoryPath;
    public DiagnosticSession(string? root = null, DiagnosticEnvironment? environment = null)
    {
        this.root = root ?? Path.Combine(AppPaths.Root, "diagnostics"); this.environment = environment ?? new();
    }
    private void Publish()
    {
        var audio = recorder?.Diagnostics; var engine = detector?.Diagnostics; var activity = detector?.Activity;
        var health = audio is null ? "Microphone closed" :
            $"Mic: {(audio.Running ? "Open" : "Closed")} | Level: {audio.LevelDb?.ToString("F1") ?? "—"} dBFS | Packets: {audio.Packets} | Gaps: {audio.Discontinuities} | Last packet: {audio.LastPacketAgeMs:F0} ms ago\r\n" +
            $"Clock: {audio.DeviceRate?.ToString() ?? "—"} Hz → 16000 Hz | Epoch: {audio.Epoch} | Native flags: {audio.Flags?.ToString() ?? "—"}\r\n" +
            $"Detector queue: {(engine?.QueuedSamples ?? 0) / 16d:F0} ms | Work: {engine?.LastWorkMs:F1} ms | Longest: {engine?.LongestWorkMs:F1} ms | Drops: {engine?.Dropped} | Resets: {engine?.Resets}\r\n" +
            $"Speech silence: {activity?.Silence.TotalSeconds:F1}s / {settings.SilenceSeconds}s | Recording: {recording} | Trial: {attemptNumber}\r\n" +
            $"Whisper: {settings.SpeechModel} | Checks: {engine?.Checks} | Last inference: {engine?.LastInferenceMs:F0} ms | Checking: {engine?.Recognizing}";
        Volatile.Write(ref view, new(status, health, journal?.RecentText ?? "", running, attempt is not null, journal is not null));
    }
    public Task StartAsync(DiagnosticSettings next, string deviceId)
    {
        lock (lifecycle)
        {
            if (!starting.IsCompleted || running) return starting;
            cancellation?.Dispose(); cancellation = new();
            var ct = cancellation.Token;
            return starting = Task.Run(() => StartCore(next, deviceId, ct));
        }
    }
    private async Task StartCore(DiagnosticSettings next, string deviceId, CancellationToken ct)
    {
        await serial.WaitAsync();
        try
        {
            ct.ThrowIfCancellationRequested();
            if (running || recorder is not null) return;
            var config = new AppConfig { AskWakePhrase = next.AskPhrase, DictationWakePhrase = next.DictationPhrase, NoiseGateDb = next.NoiseGateDb, WakeSilenceSeconds = next.SilenceSeconds };
            config.Validate();
            settings = next; journal?.Dispose(); journal = new(root, next);
            using (var process = Process.GetCurrentProcess())
            {
                try { journal.Record("build", "Executable SHA-256: " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Environment.ProcessPath!)))); }
                catch (IOException) { journal.Record("build", "Executable fingerprint unavailable"); }
            }
            if (environment.MainAppRunning())
            {
                journal.Record("blocked", "Main SharpVoice is running; no microphone was opened.");
                status = "Exit SharpVoice from its tray menu, then click Start Listening again. This prevents accidental AI requests or pastes."; return;
            }
            ct.ThrowIfCancellationRequested();
            status = "Preparing verified local wake and speech models…"; Publish();
            journal.Record("prepare_models", "Preparing selected Whisper model and verified speech activity asset");
            var model = await environment.PrepareModels(http, p => { status = p.Message + (p.TotalBytes is > 0 ? $" {p.DownloadedBytes * 100 / p.TotalBytes}%" : ""); Publish(); }, ct);
            ct.ThrowIfCancellationRequested();
            var speechPath = await environment.PrepareSpeechModel(http, next.SpeechModel, ct);
            await speech.PreloadAsync(speechPath, ct);
            journal.Record("detector_loading", "Starting speech-triggered Whisper with " + next.SpeechModel);
            detector = await Task.Run(() => new WakeWhisperDetector(model, config,
                (audio, token) => speech.TranscribeAsync(speechPath, audio, "en", _ => { }, token, WakePhrases.VocabularyPrompt(config)), diagnostics: true), ct);
            ct.ThrowIfCancellationRequested();
            detector.Detected += hit => { if (hits.Count < 128) hits.Enqueue(hit); };
            detector.Failed += e => errors.Enqueue($"Detector failure: {e.GetType().Name}, HRESULT 0x{e.HResult:X8}");
            recorder = environment.CreateRecorder(next, deviceId);
            recorder.Failed += e => errors.Enqueue($"Microphone failure: {e.GetType().Name}, HRESULT 0x{e.HResult:X8}");
            recorder.RecordingInterrupted += () => errors.Enqueue("Recording interrupted by a native audio discontinuity.");
            recorder.SetWakeListening(true, Audio);
            journal.Record("microphone_opening", "Waiting for the native capture session"); status = "Opening microphone…"; Publish();
            await recorder.StartAsync().WaitAsync(TimeSpan.FromSeconds(10), ct);
            var start = Stopwatch.StartNew();
            while (!recorder.IsWakeListening)
            {
                if (start.Elapsed.TotalSeconds > 10 || !errors.IsEmpty) throw new IOException("Microphone did not become ready.");
                await Task.Delay(50, ct);
            }
            running = true; sessionAt = Environment.TickCount64; lastHealth = 0;
            journal.Record("listening", "Production capture, speech detection and selected Whisper ready. No AI, paste, readout or global hotkeys.");
            status = "Listening. Choose Test Ask AI, Test Dictation, or Room Noise Test.";
            loop = Task.Run(() => Loop(ct));
        }
        catch (Exception ex)
        {
            SafeRecord("start_failed", $"{ex.GetType().Name}; HRESULT 0x{ex.HResult:X8}");
            status = ct.IsCancellationRequested ? "Start canceled. Microphone closed."
                : "Could not start. Check microphone permissions and internet access for model preparation; export the report for diagnosis.";
            await CloseNative();
        }
        finally { Publish(); serial.Release(); }
    }
    private void Audio(ReadOnlySpan<float> samples, long first, long epoch)
    {
        detector?.Enqueue(samples, first, epoch);
        lock (clipGate) clip?.Write(samples); // No logging, disk I/O or UI on the microphone callback.
    }
    private async Task Loop(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(ct))
            {
                await serial.WaitAsync(ct);
                try
                {
                    if (!running) return;
                    if (errors.TryDequeue(out var error))
                    {
                        journal!.Record("failure", error, recorder?.Diagnostics, detector?.Diagnostics); status = error + " Stop and export the report.";
                        await CloseNative(); Publish(); return;
                    }
                    while (hits.TryDequeue(out var hit))
                    {
                        journal!.Record("wake_hit", $"{hit.Mode}; onset {hit.StartSample}; epoch {hit.Epoch}");
                        if (attempt is null) { status = $"Detected {hit.Mode}. Start a guided test to check recording and rearming."; continue; }
                        if (hit.Epoch != recorder!.CaptureEpoch) { journal.Record("stale_hit", "Detection belongs to a previous capture epoch"); continue; }
                        var first = attempt.Wake is null; attempt.Observe(hit);
                        if (attempt.Expected is not null && first)
                        {
                            detector!.SetRecognitionEnabled(false);
                            await recorder.BeginWakeAsync(hit.StartSample, hit.Epoch, ct); recording = true;
                            journal.Record("recording", "Continuous speech handoff succeeded; awaiting speech end.");
                            status = "Recording your test phrase. Stop speaking and wait for automatic rearming.";
                        }
                    }
                    var activity = detector!.Activity;
                    if (recording && activity?.Epoch == recorder!.CaptureEpoch && activity.Silence.TotalSeconds >= settings.SilenceSeconds)
                        await FinishCore(false, ct);
                    else if (attempt is not null && Environment.TickCount64 - attemptAt >= (attempt.Expected is null ? 10000 : 20000))
                    {
                        var timedOut = recording;
                        if (timedOut) journal!.Record("endpoint_timeout", "Recording did not stop within the 20-second guided test window.");
                        await FinishCore(false, ct, timedOut);
                    }
                    if (Environment.TickCount64 - lastHealth >= 1000)
                    {
                        lastHealth = Environment.TickCount64;
                        journal!.Record("health", "Technical capture/worker snapshot", recorder?.Diagnostics, detector?.Diagnostics);
                    }
                    if (Environment.TickCount64 - sessionAt >= 20 * 60 * 1000)
                    {
                        await FinishCore(false, ct); await CloseNative(); status = "20-minute session limit reached. Export this report or start a new session.";
                    }
                    Publish();
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    SafeRecord("session_failed", $"{ex.GetType().Name}; HRESULT 0x{ex.HResult:X8}");
                    await CloseNative(); status = "Test stopped after an error. Export the report; Stop/Start retries capture."; Publish(); return;
                }
                finally { serial.Release(); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    public async Task BeginAttemptAsync(VoiceMode? expected)
    {
        await serial.WaitAsync();
        try
        {
            if (!running || attempt is not null) return;
            // Throw away passive detections queued before the user started this attempt.
            detector!.Reset(recorder!.CaptureEpoch);
            while (hits.TryDequeue(out _)) { }
            detector.SetRecognitionEnabled(true);
            var config = new AppConfig { AskWakePhrase = settings.AskPhrase, DictationWakePhrase = settings.DictationPhrase };
            attempt = new(expected, config); attemptNumber++; attemptAt = Environment.TickCount64;
            lock (clipGate) { clip?.Dispose(); clip = settings.ClipsEnabled ? new(20 * 16000) : null; }
            journal!.Record("trial_start", $"Attempt {attemptNumber}; expected {expected?.ToString() ?? "no detection"}");
            status = expected is null ? "Room noise test: stay quiet for 10 seconds. Any wake hit is a false activation."
                : expected == VoiceMode.AskAi ? $"Say: “{settings.AskPhrase}, what is two plus two?” Then stop speaking."
                : $"Say: “{settings.DictationPhrase}, please bring the blue folder tomorrow.” Then stop speaking.";
            Publish();
        }
        finally { serial.Release(); }
    }
    public async Task FinishAttemptAsync(bool missed)
    {
        await serial.WaitAsync();
        try { if (running) await FinishCore(missed, cancellation!.Token); Publish(); }
        finally { serial.Release(); }
    }
    private async Task FinishCore(bool missed, CancellationToken ct, bool endpointTimedOut = false)
    {
        if (attempt is null) return;
        var outcome = missed ? "Missed (User Marked)" : attempt.Result(endpointTimedOut);
        if (recording)
        {
            if (missed) await recorder!.CancelAsync();
            else
            {
                using var audio = await recorder!.EndAsync(ct);
                journal!.Record("recording_end", $"{audio.Samples.Length} samples; speech gate passed: {audio.HasSpeech}");
                if (!endpointTimedOut)
                {
                    status = "Checking final transcript and mode with " + settings.SpeechModel + "…"; Publish();
                    if (!audio.HasSpeech) outcome = "Below Noise Gate";
                    else
                    {
                        var path = await environment.PrepareSpeechModel(http, settings.SpeechModel, ct);
                        var config = new AppConfig { AskWakePhrase = settings.AskPhrase, DictationWakePhrase = settings.DictationPhrase };
                        var text = await speech.TranscribeAsync(path, audio.Samples, "en", _ => { }, ct, WakePhrases.VocabularyPrompt(config));
                        outcome = attempt.Complete(text);
                        journal.Record("final_mode", $"Resolved {attempt.FinalMode}; {outcome}. Transcript content is not logged.");
                    }
                }
            }
            recording = false;
        }
        float[]? samples; lock (clipGate) { samples = clip?.Snapshot(); clip?.Dispose(); clip = null; }
        if (samples is not null) { try { journal!.AddClip(attemptNumber, samples); } finally { Array.Clear(samples); } }
        journal!.Record("trial_result", $"Attempt {attemptNumber}: {outcome}; resolved {attempt.Wake?.Mode.ToString() ?? "none"}");
        attempt = null;
        recorder!.SetWakeListening(false, null); detector!.Reset(recorder.CaptureEpoch);
        await Task.Delay(750, ct); detector.SetRecognitionEnabled(true); recorder.SetWakeListening(true, Audio);
        journal.Record("rearm", "Wake audio re-enabled after guard; capture readiness is reported in health snapshots.");
        status = $"Attempt {attemptNumber}: {outcome}. Try again, test the other phrase, or export the report.";
    }
    private async Task CloseNative()
    {
        running = false; recording = false; attempt = null;
        lock (clipGate) { clip?.Dispose(); clip = null; }
        var oldRecorder = recorder; recorder = null;
        var oldDetector = detector; detector = null;
        try
        {
            if (oldRecorder is not null)
            {
                SafeRecord("capture_closing", "Waiting for native microphone release", oldRecorder.Diagnostics, oldDetector?.Diagnostics);
                await Task.Run(oldRecorder.Dispose); SafeRecord("capture_closed", "Native microphone released");
            }
        }
        finally
        {
            if (oldDetector is not null)
            {
                SafeRecord("detector_closing", "Waiting for speech and Whisper workers to drain", detector: oldDetector.Diagnostics);
                await oldDetector.DisposeAsync(); SafeRecord("detector_closed", "Speech and Whisper workers released");
            }
        }
        while (hits.TryDequeue(out _)) { } while (errors.TryDequeue(out _)) { }
    }
    private void SafeRecord(string code, string detail, CaptureDiagnostics? capture = null, WakeDetectorDiagnostics? detector = null)
    {
        try { journal?.Record(code, detail, capture, detector); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public Task StopAsync()
    {
        lock (lifecycle)
        {
            cancellation?.Cancel();
            var pendingStart = starting;
            return Task.Run(() => StopCore(pendingStart));
        }
    }
    private async Task StopCore(Task pendingStart)
    {
        async Task Drain() { await pendingStart; await loop; }
        var failure = await DiagnosticShutdown.RunAsync(Drain(), async () =>
        {
            await serial.WaitAsync();
            try
            {
                if (attempt is not null) SafeRecord("trial_canceled", "Stopped by user; incomplete trial");
                await CloseNative(); SafeRecord("stopped", "Microphone and detector released.");
                status = "Stopped. Microphone closed. Export the report or start another session."; Publish();
            }
            finally { serial.Release(); }
        });
        if (failure is not null) SafeRecord("shutdown_worker_fault", $"{failure.GetType().Name}; native cleanup completed");
    }
    public string Export() => journal?.Export() ?? throw new InvalidOperationException("Start a session first.");
    public async ValueTask DisposeAsync() { await StopAsync(); speech.Dispose(); journal?.Dispose(); cancellation?.Dispose(); http.Dispose(); serial.Dispose(); }
}
