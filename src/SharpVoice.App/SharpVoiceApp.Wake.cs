using SharpVoice.Audio;
using SharpVoice.Core;
using SharpVoice.Speech;

namespace SharpVoice.App;

internal sealed partial class SharpVoiceApp
{
    private readonly object wakeGate = new();
    private WakeModelManager wakeModels = null!;
    private WakeWhisperDetector? wakeDetector;
    private CancellationTokenSource? wakePreparation;
    private Task wakeWork = Task.CompletedTask;
    private Timer? wakeTimer;
    private long wakeGeneration, wakeRearmAt, wakeStartedAt;
    private WakeSession? wakeSession;
    private bool wakeConfirmed;
    private bool wakeWasBusy;
    private WakePreparation wakeState = new("off", 0, null, "Wake Phrases Off");

    private void InitializeWake(HttpClient http)
    {
        wakeModels = new(http, environment.WakeModelRoot);
        desktop.WakePreparationRetryRequested = RestartWake;
        desktop.WakePreparationCancelRequested = () =>
        {
            var stopping = StopWakeAsync(); preloads.Add(stopping);
            SetWakeState(new("canceled", 0, null, "Preparation canceled · Hotkeys still work. Choose Retry to enable wake listening."));
        };
        wakeTimer = new Timer(_ => desktop.Post(() => { if (!lifetime.IsCancellationRequested) RefreshWakeListening(); }), null, 100, 100);
    }
    private void SetWakeState(WakePreparation state)
    {
        if (wakeState == state) return;
        wakeState = state; desktop.SetWakePreparation(state);
    }
    private void RestartWake()
    {
        var previous = wakeWork; var settings = config; WakeWhisperDetector? old; long revision;
        var priorOwner = wakePreparation; priorOwner?.Cancel();
        var owner = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); wakePreparation = owner;
        lock (wakeGate) { revision = ++wakeGeneration; old = wakeDetector; wakeDetector = null; }
        recorder.SetWakeListening(false, null);
        SetWakeState(new(settings.WakePhrasesEnabled ? "preparing" : "off", 0, null, settings.WakePhrasesEnabled ? "Preparing Local Wake Detection…" : "Wake Phrases Off"));
        wakeWork = Task.Run(async () =>
        {
            WakeWhisperDetector? candidate = null;
            try
            {
                await previous.ConfigureAwait(false);
                priorOwner?.Dispose();
                if (old is not null) await old.DisposeAsync().ConfigureAwait(false);
                if (!settings.WakePhrasesEnabled) return;
                var ct = owner.Token;
                void Progress(WakePreparation value) => desktop.Post(() => { if (revision == wakeGeneration && !lifetime.IsCancellationRequested && !ct.IsCancellationRequested) SetWakeState(value.State == "ready" ? value with { State = "preparing", Message = "Loading Local Wake Detector…" } : value); });
                var path = await wakeModels.EnsureSpeechAsync(Progress, ct).ConfigureAwait(false);
                var speechPath = await models.EnsureAsync(settings.SpeechModel, message => Progress(new("preparing", 0, null, message)), ct).ConfigureAwait(false);
                await speech.PreloadAsync(speechPath, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                candidate = new(path, settings, (audio, token) => speech.TranscribeAsync(speechPath, audio, settings.Language, _ => { }, token, WakePhrases.VocabularyPrompt(settings)));
                candidate.Detected += hit => desktop.Post(() => { if (revision == wakeGeneration && !lifetime.IsCancellationRequested) WakeDetected(hit); });
                candidate.Failed += error => desktop.Post(() =>
                {
                    if (revision != wakeGeneration || lifetime.IsCancellationRequested) return;
                    var stopping = StopWakeAsync(); preloads.Add(stopping);
                    SetWakeState(new("unavailable", 0, null, "Wake Detection Unavailable · " + error.Message + " Hotkeys still work."));
                });
                lock (wakeGate)
                {
                    if (revision == wakeGeneration && !ct.IsCancellationRequested) { wakeDetector = candidate; candidate = null; }
                }
                desktop.Post(() => { if (revision == wakeGeneration && !ct.IsCancellationRequested && !lifetime.IsCancellationRequested) { SetWakeState(new("ready", 0, null, "Wake Detector Ready")); RefreshWakeListening(); } });
            }
            catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
            catch (Exception ex) { desktop.Post(() => { if (revision == wakeGeneration && !lifetime.IsCancellationRequested) SetWakeState(new("unavailable", 0, null, "Wake Detection Unavailable · " + ex.Message + " Hotkeys still work.")); }); }
            finally { if (candidate is not null) await candidate.DisposeAsync().ConfigureAwait(false); }
        });
        preloads.RemoveAll(t => t.IsCompleted); preloads.Add(wakeWork);
        // Sources remain alive until service work drains; old source disposal is tracked too.
    }
    private void OnWakeAudio(ReadOnlySpan<float> samples, long first, long epoch)
    {
        WakeWhisperDetector? current;
        lock (wakeGate) current = wakeDetector;
        current?.Enqueue(samples, first, epoch);
    }
    private WakeConditions WakeConditions()
    {
        bool ready; lock (wakeGate) ready = wakeDetector is not null;
        var confirming = wakeSession is not null && !wakeConfirmed && Environment.TickCount64 - wakeStartedAt < 8000
            && phase is Phase.Arming or Phase.Recording && !workspaceAsking && answerReply is null;
        return new(config.WakePhrasesEnabled, ready, desktop.IsPaused, speechInput.Suspended || !speechWork.IsCompleted,
            authBusy, editorSession.Busy || pendingVoiceStart || desktop.SetupOpen, phase == Phase.Idle, confirming, Environment.TickCount64, wakeRearmAt);
    }
    private void RefreshWakeListening()
    {
        var busy = phase != Phase.Idle || speechInput.Suspended || !speechWork.IsCompleted;
        if (wakeWasBusy && !busy) GuardWakeRearm();
        wakeWasBusy = busy;
        var conditions = WakeConditions();
        // Continue local speech activity analysis throughout a wake recording,
        // including after the longer phrase has been confirmed. New activations
        // remain guarded by CanActivate in WakeDetected.
        var trackSpeech = conditions.Ready && wakeSession is not null && phase == Phase.Recording && !desktop.IsPaused && !speechInput.Suspended;
        lock (wakeGate) wakeDetector?.SetRecognitionEnabled(conditions.CanActivate);
        recorder.SetWakeListening(conditions.CanListen || trackSpeech, conditions.CanListen || trackSpeech ? OnWakeAudio : null);
        if (!conditions.Ready) return;
        var message = desktop.IsPaused ? "Wake Listening Paused" : speechInput.Suspended || !speechWork.IsCompleted ? "Wake Listening Paused for Readout"
            : conditions.CanListen ? recorder.IsWakeListening ? "Listening for Wake Phrases · Whisper " + config.SpeechModel : "Starting Wake Microphone…" : "Wake Listening Suspended · Waiting for Ready";
        SetWakeState(new(conditions.CanListen && recorder.IsWakeListening ? "listening" : "ready", 0, null, message));
    }
    private void WakeDetected(WakeDetection detection)
    {
        if (detection.Epoch != recorder.CaptureEpoch) return;
        var conditions = WakeConditions();
        if (conditions.Confirming && wakeSession is not null)
        {
            if (wakeSession.Confirm(detection)) { wakeConfirmed = true; recordingMode = wakeSession.Mode; desktop.SetVoiceMode(recordingMode); }
            return;
        }
        if (!conditions.CanActivate || !microphoneReady) return;
        lock (wakeGate) wakeDetector?.SetRecognitionEnabled(false);
        Pressed(detection.Mode, detection);
    }
    private void FinishWakeOperation()
    {
        if (wakeSession is null) return;
        wakeSession = null; desktop.SetExternalRecording(false); wakeConfirmed = false; GuardWakeRearm();
    }
    private bool WakeSpeechEnded()
    {
        WakeSpeechActivity? activity;
        lock (wakeGate) activity = wakeDetector?.Activity;
        return activity is not null && activity.Epoch == recorder.CaptureEpoch
            && activity.Silence >= TimeSpan.FromSeconds(recordingConfig.WakeSilenceSeconds);
    }
    private void GuardWakeRearm()
    {
        wakeRearmAt = Environment.TickCount64 + 750;
        recorder.SetWakeListening(false, null);
        var epoch = recorder.CaptureEpoch;
        lock (wakeGate) wakeDetector?.Reset(epoch);
    }
    private async Task StopWakeAsync()
    {
        wakePreparation?.Cancel(); WakeWhisperDetector? old;
        lock (wakeGate) { ++wakeGeneration; old = wakeDetector; wakeDetector = null; }
        recorder.SetWakeListening(false, null);
        await wakeWork.ConfigureAwait(false);
        if (old is not null) await old.DisposeAsync().ConfigureAwait(false);
    }
}
