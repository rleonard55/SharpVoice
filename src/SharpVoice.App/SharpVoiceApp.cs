using SharpVoice.Audio;
using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Refiner;
using SharpVoice.Refiner.Auth;
using SharpVoice.Speech;
using SharpVoice.UI;

namespace SharpVoice.App;

internal sealed partial class SharpVoiceApp : IDisposable
{
    private enum Phase { Starting, Idle, Arming, Recording, Processing, Cancelling }
    private readonly NativeDesktop desktop;
    private readonly VoiceEnvironment environment;
    private WasapiAudioRecorder recorder;
    private readonly WhisperEngine speech = new();
    private readonly ModelManager models;
    private readonly UnifiedRefiner refiner;
    private readonly ConnectionTester connectionTester;
    private readonly LocalModelStore localModels;
    private readonly ChatGptAuthManager auth;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private CancellationTokenSource? login;
    private AppConfig config;
    private WindowTarget? target;
    private Phase phase = Phase.Starting;
    private Task work = Task.CompletedTask;
    private Task authWork = Task.CompletedTask;
    private Task preload = Task.CompletedTask;
    private readonly List<Task> preloads = [];
    private int generation;
    private VoiceMode recordingMode;
    private bool microphoneReady => recorder.IsAvailable;
    private readonly WindowsLoginStartup loginStartup = new();
    private bool authBusy;
    private readonly ClaudeCodeClient claude = new();
    private readonly AntigravityClient google = new();
    private int googleStatusGeneration;
    private bool claudeSignedIn;
    private int claudeStatusGeneration;
    private int modelCatalogGeneration;
    private DeferredDisposable<SelectionSnapshot> selectionCapture = new(Task.FromResult<SelectionSnapshot?>(null));

    public SharpVoiceApp(AppConfig config, NativeDesktop desktop, HttpClient http, ChatGptAuthManager auth, VoiceEnvironment? environment = null)
    {
        this.config = config; this.desktop = desktop; this.auth = auth;
        this.environment = environment ?? new(); historyStore = new(this.environment.HistoryPath);
        recorder = CreateRecorder(config);
        InitializeWritingTools(http, auth);
        InitializeAnswers();
        InitializeWake(http);
        InitializeConversations();
        InitializeConnected();
        models = new(http);
        refiner = new(http, auth, claude, google);
        refiner.IdentityChanged += () => desktop.Post(() => { Cancel(); RetireConversations(); ResetConnected(); });
        connectionTester = new(http, auth);
        localModels = new(http);
        desktop.LocalModelDownloadRequested = DownloadLocalModel;
        desktop.IsLocalModelPresent = localModels.IsPresent;
        desktop.ConnectionTestRequested = TestConnection;
        var editorPlugins = new EditorPlugins();
        desktop.PluginRequested = (id, install, ct) => install ? editorPlugins.InstallAsync(id, ct) : editorPlugins.GetStatusAsync(id, ct);
        refiner.ModelResolved += model =>
        {
            var backend = connectedCancellation is not null ? "chatgpt_oauth" : recordingConfig.Backend;
            recordingModel = model;
            desktop.Post(() => desktop.SetResolvedModel(backend, model));
        };
        desktop.ModelsRefreshRequested = RefreshAvailableModels;
        desktop.ReadLevel = () => recorder.LevelDb;
        desktop.InputPreviewChanged = enabled => { if (enabled && !speechWork.IsCompleted) throw new InvalidOperationException("Stop the spoken answer before previewing the microphone."); recorder.SetPreview(enabled); };
        desktop.VoicePressed = mode => { if (editorSession.Busy) { desktop.ResetRecordingGesture(); desktop.SetStatus("Finish or cancel the editor voice session first."); return; } RequestVoicePressed(mode); if (!pendingVoiceStart && phase is not (Phase.Arming or Phase.Recording)) desktop.ResetRecordingGesture(); };
        desktop.Released = () => { if (wakeSession is not null) { wakeSession.ConsumeRelease(); return; } if (!editorSession.Busy) Released(); };
        desktop.CancelRequested = Cancel;
        desktop.PauseChanged = paused =>
        {
            recorder.Configure(this.config.NoiseGateDb, speechInput.ShouldPause(paused));
            Cancel();
            desktop.SetStatus(paused ? "Microphone paused. Resume from the tray menu." : ReadyText());
        };
        desktop.ConfigChanged = next =>
        {
            if (phase != Phase.Idle || editorSession.Busy) throw new InvalidOperationException("Wait for the current operation to finish before applying settings.");
            if (next.Backend != this.config.Backend) RetireConversations();
            ResetConnected();
            var restart = next.MicrophoneActivation != this.config.MicrophoneActivation || next.AudioDeviceId != this.config.AudioDeviceId || next.MaxRecordingSeconds != this.config.MaxRecordingSeconds || !microphoneReady;
            var modelChanged = next.SpeechModel != this.config.SpeechModel;
            var wakeChanged = modelChanged || next.Language != this.config.Language || next.WakePhrasesEnabled != this.config.WakePhrasesEnabled || next.AskWakePhrase != this.config.AskWakePhrase || next.DictationWakePhrase != this.config.DictationWakePhrase;
            loginStartup.Save(next.StartOnLogin, () => ConfigManager.Save(next));
            var historyChanged = this.config.PersistHistory != next.PersistHistory;
            this.config = next;
            if (!next.SmartAsk) { StopAnswerSpeech(); desktop.HideAnswer(); }
            if (historyChanged) { historyRevision++; PersistHistory(); }
            if (restart)
            {
                recorder.Dispose();
                recorder = CreateRecorder(next);
                recorder.Configure(next.NoiseGateDb, speechInput.ShouldPause(desktop.IsPaused));
                Prepare(startCapture: true);
            }
            else
            {
                recorder.Configure(next.NoiseGateDb, speechInput.ShouldPause(desktop.IsPaused));
                if (modelChanged) Prepare(startCapture: false);
                else desktop.SetStatus("Settings saved. " + ReadyText());
            }
            if (wakeChanged) RestartWake();
        };
        desktop.SignInRequested = SignIn;
        desktop.SignOutRequested = SignOut;
        desktop.GoogleAccountRequested = GoogleAccount;
        var googleKey = new GeminiApiKeyStore();
        desktop.HasGeminiApiKey = () => googleKey.IsConfigured;
        desktop.SaveGeminiApiKeyRequested = key => { googleKey.Save(key); Cancel(); RetireConversations(); ResetConnected(); };
        desktop.RemoveGeminiApiKeyRequested = () => { googleKey.Remove(); Cancel(); RetireConversations(); ResetConnected(); };
        desktop.ClaudeSignInRequested = () => ClaudeAuthentication(signIn: true);
        desktop.ClaudeSignOutRequested = () => ClaudeAuthentication(signIn: false);
        desktop.IsClaudeSignedIn = () => claudeSignedIn;
        desktop.IsChatGptSignedIn = () => auth.IsSignedIn;
        desktop.IsAuthenticationBusy = () => authBusy;
        desktop.RefreshAuthentication();
        auth.RefreshFailed += message => desktop.Post(() => desktop.SetStatus(message, isError: true));
    }

    private WasapiAudioRecorder CreateRecorder(AppConfig settings)
    {
        var capture = environment.CreateRecorder?.Invoke(settings) ?? new WasapiAudioRecorder(settings.NoiseGateDb, settings.MaxRecordingSeconds, settings.AudioDeviceId, keepReady: settings.MicrophoneActivation == "keep_ready");
        capture.Failed += _ => desktop.Post(() =>
        {
            if (!ReferenceEquals(recorder, capture) || phase == Phase.Starting) return;
            Cancel();
            desktop.SetStatus("Microphone unavailable. Open Audio settings, refresh or select a microphone, and Apply to reconnect.", true, isError: true, warningSound: true);
        });
        capture.RecordingLimitReached += () => desktop.Post(() => { if (ReferenceEquals(recorder, capture)) Released(); });
        capture.RecordingInterrupted += () => desktop.Post(() => { if (ReferenceEquals(recorder, capture)) Released(); });
        return capture;
    }

    internal Task Preparation => work;
    internal bool IsMicrophoneReady => microphoneReady;

    public void Start(string? authWarning)
    {
        LoadHistory();
        Prepare(startCapture: true, authWarning);
        RefreshAvailableModels(config.Backend);
        if (environment.EditorIntegration) StartEditorBridge();
        RestartWake();
    }

    private Task DownloadLocalModel(string model, Action<LocalModelProgress> progress, CancellationToken ct)
    {
        if (editorSession.Busy || phase is Phase.Arming or Phase.Recording or Phase.Processing or Phase.Cancelling) throw new InvalidOperationException("Finish dictation before downloading a local model.");
        var task = Task.Run(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            await localModels.EnsureAsync(model, progress, linked.Token);
        });
        preloads.RemoveAll(t => t.IsCompleted); preloads.Add(task);
        return task;
    }

    private Task<ConnectionTestResult> TestConnection(string backend, string model, CancellationToken ct)
    {
        if (editorSession.Busy || phase is Phase.Arming or Phase.Recording or Phase.Processing or Phase.Cancelling || authBusy) throw new InvalidOperationException("Wait for the current operation to finish, then test again.");
        var task = Task.Run(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            return await connectionTester.TestAsync(backend, model, linked.Token);
        });
        preloads.RemoveAll(t => t.IsCompleted);
        preloads.Add(task);
        return task;
    }

    private void RefreshAvailableModels(string backend)
    {
        var request = ++modelCatalogGeneration;
        if (backend == "gemini_antigravity") { RefreshGoogleStatus(); return; }
        if (backend == "claude_code") { RefreshClaudeStatus(); return; }
        if (backend != "chatgpt_oauth") return;
        if (!auth.IsSignedIn)
        {
            desktop.SetAvailableModels([], "Sign in to load your available ChatGPT models.");
            return;
        }
        var task = Task.Run(async () =>
        {
            try
            {
                var choices = await refiner.GetModelsAsync(lifetime.Token);
                desktop.Post(() =>
                {
                    if (request != modelCatalogGeneration || !auth.IsSignedIn) return;
                    desktop.SetAvailableModels(choices.OrderByDescending(x => x.Recommended).Select(x => (x.Id, x.Label + (x.Recommended ? " · Recommended" : ""), x.Description)).ToArray());
                    if (config.Backend == "chatgpt_oauth" && config.RefinementModel == "auto")
                    {
                        var id = ModelCatalog.SelectEconomical(choices);
                        if (id is not null) desktop.SetResolvedModel("chatgpt_oauth", id, used: false);
                    }
                });
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception)
            {
                desktop.Post(() => { if (request == modelCatalogGeneration) desktop.SetAvailableModels([], "Could not load models. Refresh after checking your sign-in; explicit model IDs remain usable."); });
            }
        });
        preloads.RemoveAll(t => t.IsCompleted);
        preloads.Add(task);
    }

    private void RefreshClaudeStatus()
    {
        var request = ++claudeStatusGeneration;
        if (authBusy) return;
        var task = Task.Run(async () =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var state = await claude.GetStatusAsync(timeout.Token);
                desktop.Post(() =>
                {
                    if (request != claudeStatusGeneration || authBusy) return;
                    claudeSignedIn = state.SignedIn;
                    desktop.SetClaudeStatus(state.Message);
                    desktop.RefreshAuthentication();
                });
            }
            catch (Exception) when (!lifetime.IsCancellationRequested)
            {
                desktop.Post(() =>
                {
                    if (request != claudeStatusGeneration || authBusy) return;
                    claudeSignedIn = false;
                    desktop.SetClaudeStatus("Claude Code status unavailable. Check its installation and sign-in.");
                    desktop.RefreshAuthentication();
                });
            }
        });
        preloads.RemoveAll(t => t.IsCompleted);
        preloads.Add(task);
    }

    private void RefreshGoogleStatus()
    {
        var request = ++googleStatusGeneration;
        if (authBusy) return;
        var task = Task.Run(async () =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var note = await google.GetStatusAsync(timeout.Token);
                desktop.Post(() => { if (request == googleStatusGeneration && !authBusy) desktop.SetGoogleStatus(note); });
            }
            catch (Exception) when (!lifetime.IsCancellationRequested)
            { desktop.Post(() => { if (request == googleStatusGeneration && !authBusy) desktop.SetGoogleStatus("Antigravity status unavailable. Check its installation."); }); }
        });
        preloads.RemoveAll(t => t.IsCompleted); preloads.Add(task);
    }

    private void GoogleAccount(bool signOut)
    {
        if (authBusy) return;
        RetireConversations();
        ++googleStatusGeneration;
        if (phase is Phase.Arming or Phase.Recording or Phase.Processing) Cancel();
        authBusy = true;
        login?.Dispose();
        login = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        login.CancelAfter(TimeSpan.FromMinutes(3));
        var ct = login.Token;
        desktop.SetStatus(signOut ? "Sign out in Google's Antigravity window; close it when finished." : "Complete Google sign-in in Antigravity's window; close it when finished.");
        authWork = Task.Run(async () =>
        {
            try
            {
                await google.OpenAccountAsync(signOut, ct);
                desktop.Post(() => desktop.SetStatus("Google account window closed. Your next request will verify the connection."));
            }
            catch (OperationCanceledException) { desktop.Post(() => desktop.SetStatus("Google account window cancelled or timed out.")); }
            catch (Exception ex) { desktop.Post(() => desktop.SetStatus(ex.Message, isError: true, warningSound: true)); }
            finally { desktop.Post(() => { authBusy = false; desktop.RefreshAuthentication(); RefreshGoogleStatus(); }); }
        });
    }

    private void ClaudeAuthentication(bool signIn)
    {
        if (authBusy || (!signIn && !claudeSignedIn)) return;
        RetireConversations();
        ++claudeStatusGeneration;
        if (phase is Phase.Arming or Phase.Recording or Phase.Processing) Cancel();
        authBusy = true;
        login?.Dispose();
        login = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        login.CancelAfter(TimeSpan.FromMinutes(3));
        var ct = login.Token;
        desktop.SetStatus(signIn ? "Complete sign-in in Claude Code's own window and browser…" : "Signing out of Claude Code on this device…");
        authWork = Task.Run(async () =>
        {
            try
            {
                if (signIn) await claude.SignInAsync(ct); else await claude.SignOutAsync(ct);
                var state = await claude.GetStatusAsync(ct);
                desktop.Post(() =>
                {
                    claudeSignedIn = state.SignedIn;
                    desktop.SetClaudeStatus(state.Message);
                    desktop.SetStatus(state.Message);
                });
            }
            catch (OperationCanceledException) { desktop.Post(() => desktop.SetStatus("Claude sign-in/out cancelled or timed out.")); }
            catch (Exception ex) { desktop.Post(() => desktop.SetStatus(ex.Message, isError: true, warningSound: true)); }
            finally { desktop.Post(() => { authBusy = false; desktop.RefreshAuthentication(); RefreshClaudeStatus(); }); }
        });
    }

    private async Task ObserveSelectionAsync(Task<SelectionSnapshot?> capture, int id, CancellationToken ct)
    {
        try
        {
            var selection = await capture;
            if (ct.IsCancellationRequested) selection?.Dispose();
            else desktop.Post(() => { if (generation == id && (phase is Phase.Arming or Phase.Recording)) desktop.SetEditingSelection(selection is not null); });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            desktop.Post(() =>
            {
                if (generation != id || phase is not (Phase.Arming or Phase.Recording)) return;
                Cancel();
                desktop.SetStatus(ex.Message, true, isError: true, warningSound: true);
            });
        }
    }

    private void Prepare(bool startCapture, string? authWarning = null)
    {
        phase = Phase.Starting;
        desktop.SetStatus(startCapture ? config.MicrophoneActivation == "keep_ready" ? "Starting microphone and preparing Whisper…" : "Preparing Whisper · Microphone opens on hotkey…" : "Preparing selected Whisper model…");
        work = Task.Run(async () =>
        {
            await historyTasks.Ready;
            try
            {
                if (startCapture) await recorder.StartAsync();
                await models.EnsureAsync(config.SpeechModel, text => desktop.Post(() => desktop.SetStatus(text)), lifetime.Token);
                desktop.Post(() =>
                {
                    phase = Phase.Idle; PublishWorkspace();
                    if (!microphoneReady) desktop.SetStatus("Microphone unavailable. Open Audio settings and Apply to reconnect.", isError: true, warningSound: true);
                    else desktop.SetStatus(authWarning ?? ReadyText(), isError: authWarning is not null);
                });
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception ex) { desktop.Post(() => { phase = Phase.Idle; desktop.SetStatus("Setup failed: " + ex.Message + " Open Settings and Apply to retry.", isError: true, warningSound: true); }); }
        });
    }

    private string ReadyText() => $"Ready · {HotkeyDefinition.DisplayName(config.Hotkey)}: dictate · {HotkeyDefinition.DisplayName(config.AskHotkey)}: ask AI" + (config.WakePhrasesEnabled ? " · " + wakeState.Message : "");

    private void Pressed(VoiceMode mode, WakeDetection? wake = null)
    {
        if (desktop.IsPaused) return;
        if (phase is Phase.Arming or Phase.Recording)
        {
            if (wakeSession is not null) { if (wakeSession.HandlePress(mode)) Released(); return; }
            if (mode == VoiceMode.AskAi && recordingMode != VoiceMode.AskAi)
            {
                if (recordingConfig.Backend == "none" && !config.ConnectedAsk)
                {
                    Cancel();
                    desktop.SetStatus("Ask AI needs an AI backend. Choose ChatGPT or offline AI in Settings.", true, isError: true, warningSound: true);
                    return;
                }
                recordingMode = mode;
                desktop.SetVoiceMode(mode);
                desktop.SetStatus(phase == Phase.Arming ? "Starting microphone for your request…" : "Listening to your request · " + RecordingHint, true);
            }
            return;
        }
        if (phase != Phase.Idle) return;
        lock (wakeGate) wakeDetector?.SetRecognitionEnabled(false);
        if (wake is null) FinishWakeOperation();
        if (!microphoneReady) { desktop.SetStatus("Microphone is unavailable. Check Windows permissions, then open Audio settings and Apply to reconnect.", isError: true, warningSound: true); return; }
        var snapshot = environment.CaptureTarget();
        if ((snapshot.ProcessId == Environment.ProcessId && (wake is not null || !(mode == VoiceMode.AskAi && config.ConnectedAsk && desktop.ConnectedVisible))) || snapshot.Handle == 0)
        {
            desktop.SetStatus("Focus a text field in another app, then hold the dictation key.");
            return;
        }
        recordingConfig = WritingTools.Resolve(config, snapshot.ProcessName);
        recordingModel = "";
        if (wake is null && mode == VoiceMode.AskAi && recordingConfig.Backend == "none" && !config.ConnectedAsk)
        {
            desktop.SetStatus("Ask AI needs an AI provider for this app. Choose one in AI & Models or its App Profile.", true, isError: true, warningSound: true);
            return;
        }
        desktop.SetDictationConfig(recordingConfig, WritingTools.FindProfile(config, snapshot.ProcessName)?.Name ?? "");
        operation?.Dispose();
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation.CancelAfter(TimeSpan.FromMinutes(15));
        generation++;
        target = snapshot;
        wakeSession = wake is WakeDetection hit ? new(hit, recordingConfig) : null;
        desktop.SetExternalRecording(wakeSession is not null);
        wakeStartedAt = Environment.TickCount64;
        wakeConfirmed = wake is WakeDetection initial && !WakePhrases.IsLonger(recordingConfig, initial.Mode, initial.Mode == VoiceMode.Dictation ? VoiceMode.AskAi : VoiceMode.Dictation);
        try
        {
            recordingMode = mode;
            desktop.SetVoiceMode(mode);
            desktop.SetEditingSelection(false);
            selectionCapture.Dispose();
            phase = Phase.Arming;
            var armed = wake is WakeDetection detected ? recorder.BeginWakeAsync(detected.StartSample, detected.Epoch, operation.Token) : recorder.BeginAsync(operation.Token);
            selectionCapture = new(wake is null && mode == VoiceMode.AskAi && config.ConnectedAsk ? Task.FromResult<SelectionSnapshot?>(null) : environment.CaptureSelection(snapshot, operation.Token));
            preloads.Add(selectionCapture.Disposal);
            desktop.SetActive(true);
            desktop.SetStatus("Starting microphone · Wait for the start tone…", true);
            preloads.Add(ObserveSelectionAsync(selectionCapture.Value, generation, operation.Token));
            var selectedModel = recordingConfig.SpeechModel;
            var preloadToken = operation.Token;
            preload = Task.Run(async () =>
            {
                var path = await models.EnsureAsync(selectedModel, _ => { }, preloadToken);
                await speech.PreloadAsync(path, preloadToken);
            }, preloadToken);
            // A cancelled recording may never enter Released; observe errors without losing
            // the original task, which Released awaits before inference.
            preloads.RemoveAll(t => t.IsCompleted);
            preloads.Add(preload);
            _ = preload.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            if (armed.IsCompletedSuccessfully) CompleteRecordingStart(generation);
            else work = AwaitRecordingStartAsync(armed, generation);

        }
        catch (Exception ex)
        {
            operation?.Cancel(); selectionCapture.Dispose();
            if (phase is Phase.Arming or Phase.Recording) Cancel();
            else { recorder.Cancel(); phase = Phase.Idle; desktop.SetRecording(false); desktop.SetActive(false); }
            desktop.SetStatus(ex.Message, isError: true, warningSound: true);
        }
    }

    private async Task AwaitRecordingStartAsync(Task armed, int id)
    {
        try
        {
            await armed;
            desktop.Post(() => CompleteRecordingStart(id));
        }
        catch (OperationCanceledException) { desktop.Post(() => { if (generation == id && phase == Phase.Arming) Cancel(); }); }
        catch (Exception ex)
        {
            desktop.Post(() =>
            {
                if (generation != id || phase != Phase.Arming) return;
                Cancel();
                desktop.SetStatus(ex.Message, isError: true, warningSound: true);
            });
        }
    }

    private void CompleteRecordingStart(int id)
    {
        if (generation != id || phase != Phase.Arming || lifetime.IsCancellationRequested || operation?.IsCancellationRequested != false) return;
        phase = Phase.Recording;
        desktop.SetRecording(true);
        if (answerReply is not null) { desktop.SetAnswerReplyState(true, recording: true); desktop.SetAnswerStatus("Listening · Click Stop Recording when finished"); }
        if (wakeSession is null && !editorSession.Busy && recordingMode == VoiceMode.AskAi && recordingConfig.ConnectedAsk) desktop.SetConnectedRecording(true);
        if (editorSession.Busy) SendEditor(new("status", editorSession.Id, Message: "recording"));
        if (editorSession.Busy) { workspaceMessage = "Listening · Click Stop Recording when finished"; PublishWorkspace(); }
        if (workspaceAsking) { workspaceMessage = "Listening · Click Stop Recording when finished"; PublishWorkspace(); }
        BeginLiveTranscription();
        BeginSilenceWatch();
        desktop.SetPreview("");
        desktop.SetStatus(recordingMode == VoiceMode.AskAi ? "Listening to your request · " + RecordingHint : "Listening · " + RecordingHint, true);
    }

    private void Released()
    {
        if (pendingVoiceStart) { CancelPendingVoice(); desktop.ResetRecordingGesture(); return; }
        if (workspaceAsking && phase == Phase.Recording) { FinishWorkspaceRecording(); return; }
        if (answerReply is not null && phase == Phase.Recording) { FinishAnswerRecording(); return; }
        if (editorSession.Busy && phase == Phase.Recording) { FinishEditorRecording(); return; }
        if (phase == Phase.Arming)
        {
            Cancel();
            desktop.SetStatus("Microphone start cancelled. Hold the shortcut until the start tone before speaking.");
            return;
        }
        if (phase != Phase.Recording) return;
        var turnaround = System.Diagnostics.Stopwatch.StartNew();
        StopSilenceWatch();
        phase = Phase.Processing;
        desktop.SetStatus("Finalizing Transcript…", true);
        var id = generation;
        var ct = operation!.Token;
        var activeConfig = recordingConfig;
        var activeMode = recordingMode;
        var activeWake = wakeSession;
        var destination = target!;
        var capturedSelection = selectionCapture;
        work = Task.Run(async () =>
        {
            Guid? historyId = null;
            ConversationLease? askLease = null; ConversationView? askBefore = null;
            var retainedSelection = false;
            double decodingAt = 0, decodedAt = 0, insertingAt = 0;
            string CompletedTiming()
            {
                var done = turnaround.Elapsed.TotalMilliseconds;
                var detail = $"Release to Insertion Dispatch: {done:F0} ms\nPreparation (Audio, Draft Drain, Selection, Model): {decodingAt:F0} ms\nTranscription: {decodedAt - decodingAt:F0} ms\nAI and Rules: {insertingAt - decodedAt:F0} ms\nInsertion (Including Configured Delay): {done - insertingAt:F0} ms";
                Ui(() => desktop.SetTurnaround(detail));
                return (done / 1000).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " s";
            }
            void Ui(Action action) => desktop.Post(() => { if (generation == id && !lifetime.IsCancellationRequested) action(); });
            try
            {
                var stoppedDraft = StopLiveTranscription();
                using var audio = await recorder.EndAsync(ct);
                Ui(() => { desktop.SetRecording(false); desktop.SetConnectedRecording(false); });
                await stoppedDraft; // Play stop only after the audio tail is complete.
                var selection = activeWake is null && activeMode == VoiceMode.AskAi && activeConfig.ConnectedAsk ? null : await capturedSelection.Value.WaitAsync(ct);
                Ui(() => desktop.SetEditingSelection(selection is not null));
                if (!audio.HasSpeech) { Ui(() => desktop.SetStatus("No speech detected. Speak closer or lower the noise gate.", true, isError: true, warningSound: true)); return; }
                var path = await models.EnsureAsync(activeConfig.SpeechModel, text => Ui(() => desktop.SetStatus(text)), ct);
                await preload;
                Ui(() => desktop.SetStatus("Transcribing locally…"));
                decodingAt = turnaround.Elapsed.TotalMilliseconds;
                var transcript = await speech.TranscribeAsync(path, audio.Samples, activeConfig.Language, text => Ui(() => desktop.SetPreview(activeWake?.Resolve(text).Text ?? text)), ct, activeWake is not null ? WakePhrases.VocabularyPrompt(activeConfig) : activeMode == VoiceMode.Dictation && selection is null ? WritingTools.VocabularyPrompt(activeConfig.Dictionary) : "", fast: activeConfig.FastTranscription && activeWake is null);
                decodedAt = turnaround.Elapsed.TotalMilliseconds;
                if (activeWake is not null)
                {
                    var resolved = activeWake.Resolve(transcript); activeMode = resolved.Mode; transcript = resolved.Text;
                    if (activeMode == VoiceMode.AskAi && activeConfig.ConnectedAsk) selection = null;
                    Ui(() => { recordingMode = activeMode; desktop.SetVoiceMode(activeMode); desktop.SetEditingSelection(selection is not null); });
                }
                if (string.IsNullOrWhiteSpace(transcript)) { Ui(() => desktop.SetStatus("No text was transcribed. Nothing was sent to the AI or inserted.", true, isError: true, warningSound: true)); return; }
                if (activeMode == VoiceMode.AskAi && activeConfig.Backend == "none" && !activeConfig.ConnectedAsk) throw new InvalidOperationException("Ask AI needs an AI provider for this app. Choose one in AI & Models or its App Profile.");
                Ui(() => desktop.SetPreview(transcript, retain: true));
                if (activeMode == VoiceMode.AskAi && activeConfig.ConnectedAsk) { await RunConnectedAsync(transcript, activeConfig, ct); return; }
                var prepared = WritingTools.Prepare(transcript, activeConfig, activeMode == VoiceMode.AskAi, selection is not null);
                historyId = Guid.NewGuid();
                var entry = new DictationEntry(historyId.Value, DateTimeOffset.UtcNow, destination.ProcessName, transcript, prepared.Text, "", selection?.Text, activeMode == VoiceMode.AskAi, activeConfig with { Dictionary = [], Snippets = [], Profiles = [] }, "", "Transcribed", "");
                if (activeMode != VoiceMode.AskAi) Ui(() => AddHistory(entry));
                else askLease = BeginAskTurn(activeConfig, prepared.Text, out askBefore);
                if (selection is not null && !selection.IsStillSelected()) throw new InvalidOperationException("The highlighted text changed. Your spoken instructions are retained; nothing was inserted.");
                var skipCleanup = prepared.IsSnippet || RefinementPolicy.ShouldSkipCleanup(transcript, activeConfig, activeMode == VoiceMode.AskAi, !string.IsNullOrEmpty(selection?.Text));
                Ui(() =>
                {
                    desktop.SetCleanupSkipped(skipCleanup);
                    desktop.SetStatus(skipCleanup ? "Transcribed · AI cleanup skipped" : activeMode == VoiceMode.AskAi ? "Answering your request…" : activeConfig.Backend == "none" ? "Preparing text insertion…" : "Refining transcript…");
                });
                using var inferenceTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                inferenceTimeout.CancelAfter(TimeSpan.FromMinutes(2));
                if (activeMode == VoiceMode.AskAi && activeConfig.SmartAsk)
                {
                    var routed = await new SmartAskRefiner(refiner).GenerateAsync(prepared.Text, selection?.Text, destination.ProcessName, activeConfig, inferenceTimeout.Token, askBefore);
                    ct.ThrowIfCancellationRequested();
                    if (!CompleteAskTurn(askLease!, routed.Text, routed.Route == "answer" ? "Answered" : "Ready · Not Inserted")) throw new OperationCanceledException();
                    if (routed.Route == "answer")
                    {
                        retainedSelection = true;
                        var model = recordingModel;
                        desktop.Post(() =>
                        {
                            if (generation == id && !ct.IsCancellationRequested) PresentAnswer(routed.Text, model, destination, activeConfig, capturedSelection, entry.Id, prepared.Text, selection?.Text, askLease!.ConversationId);
                            else capturedSelection.Dispose();
                        });
                        audio.Dispose(); MemoryTrimmer.Trim();
                        return;
                    }
                    Ui(() => { desktop.SetPreview(routed.Text, retain: true); UpdateHistory(entry.Id, routed.Text, "Ready · Not Yet Pasted"); });
                    insertingAt = turnaround.Elapsed.TotalMilliseconds;
                    await environment.Insert(routed.Text, destination, activeConfig.Injection, activeConfig.PasteDelayMs, ct, selection);
                    var total = CompletedTiming();
                    Ui(() => { desktop.PlayInsertionSuccess(); UpdateHistory(entry.Id, routed.Text, "Inserted"); desktop.SetStatus("Inserted in " + total + " · " + ReadyText()); });
                    audio.Dispose(); MemoryTrimmer.Trim();
                    return;
                }
                string result;
                try { result = prepared.IsSnippet || skipCleanup ? prepared.Text : await refiner.RefineAsync(prepared.Text, destination.ProcessName, activeConfig, text => Ui(() => desktop.SetPreview(text)), inferenceTimeout.Token, selection?.Text, askAi: activeMode == VoiceMode.AskAi, forceCleanup: true, answerConversation: askBefore is null ? null : AnswerConversation.From(askBefore, selection?.Text));
                    if (!prepared.IsSnippet && activeMode == VoiceMode.Dictation && selection is null) result = WritingTools.Correct(result, activeConfig.Dictionary); }
                catch { Ui(() => desktop.SetPreview(transcript, retain: true)); throw; }
                ct.ThrowIfCancellationRequested();
                if (askLease is not null && !CompleteAskTurn(askLease, result, "Ready · Not Inserted")) throw new OperationCanceledException();
                Ui(() => { desktop.SetPreview(result, retain: true); UpdateHistory(entry.Id, result, "Ready · Not Yet Pasted"); });
                insertingAt = turnaround.Elapsed.TotalMilliseconds;
                await environment.Insert(result, destination, activeConfig.Injection, activeConfig.PasteDelayMs, ct, selection);
                var insertionTime = CompletedTiming();
                Ui(() => { desktop.PlayInsertionSuccess(); UpdateHistory(entry.Id, result, prepared.IsSnippet ? "Snippet Inserted" : "Inserted"); });
                audio.Dispose();
                MemoryTrimmer.Trim();
                Ui(() => desktop.SetStatus("Inserted in " + insertionTime + (skipCleanup ? " · AI cleanup skipped · " : " · ") + ReadyText()));
            }
            catch (OperationCanceledException) { Ui(() => { if (historyId is Guid canceled) UpdateHistory(canceled, null, "Canceled"); desktop.SetRecording(false); desktop.SetStatus(activeMode == VoiceMode.AskAi ? "Request cancelled or timed out. Your spoken request is retained." : "Dictation cancelled or timed out. Any completed transcript is retained."); }); }
            catch (Exception ex) { Ui(() => { if (historyId is Guid failed) UpdateHistory(failed, null, "Failed · Text Retained", ex.Message); desktop.SetRecording(false); desktop.SetStatus(ex.Message, true, isError: true, warningSound: true); }); }
            finally
            {
                if (askLease is not null) conversations.Cancel(askLease); // No-op if already completed.
                if (!retainedSelection) capturedSelection.Dispose();
                Ui(() => { desktop.SetRecording(false); phase = Phase.Idle; FinishWakeOperation(); desktop.SetActive(false); PublishWorkspace(); });
            }
        });
    }

    private void Cancel()
    {
        if (workspaceAsking) { CancelWorkspaceRequest(); return; }
        if (answerReply is not null) { CancelAnswerReply(); return; }
        _ = StopLiveTranscription();
        CancelPendingVoice(); StopAnswerSpeech(); answerInsertion?.Cancel();
        lock (connectedGate) { connectedCancellation?.Cancel(); connectedDecision?.TrySetResult(false); connectedDecision = null; connectedChangeId = null; }
        desktop.SetConnectedRecording(false);
        CancelEditor();
        if (phase is Phase.Starting or Phase.Idle or Phase.Cancelling) return;
        StopSilenceWatch();
        var capturePending = phase is Phase.Arming or Phase.Recording;
        operation?.Cancel();
        desktop.StopInputPreview();
        var closed = recorder.CancelAsync();
        desktop.SetRecording(false);
        phase = Phase.Cancelling;
        desktop.SetStatus("Dictation cancelled.");
        if (capturePending)
        {
            selectionCapture.Dispose();
            preloads.Add(work);
            work = FinishCaptureCancellationAsync(closed, generation);
        }
        // Processing owns its finally block; EndAsync also waits for native closure on cancellation.
    }

    private async Task FinishCaptureCancellationAsync(Task closed, int id)
    {
        await closed;
        desktop.Post(() =>
        {
            if (generation != id || phase != Phase.Cancelling || lifetime.IsCancellationRequested) return;
            phase = Phase.Idle;
            FinishWakeOperation();
            desktop.SetActive(false); PublishWorkspace();
        });
    }

    private void SignIn()
    {
        RetireConversations(); ResetConnected();
        if (authBusy) return;
        if (editorSession.Busy) Cancel();
        if (phase is Phase.Arming or Phase.Recording or Phase.Processing) Cancel();
        authBusy = true;
        login?.Dispose();
        login = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        desktop.SetStatus("Complete ChatGPT sign-in in your browser (3 minute timeout)…");
        authWork = Task.Run(async () =>
        {
            try
            {
                await auth.SignInAsync(login.Token);
                desktop.Post(() => desktop.SetStatus("ChatGPT signed in. " + ReadyText()));
            }
            catch (OperationCanceledException) { desktop.Post(() => desktop.SetStatus("Sign-in cancelled or timed out.")); }
            catch (Exception ex) { desktop.Post(() => desktop.SetStatus("Sign-in failed: " + ex.Message, isError: true, warningSound: true)); }
            finally { desktop.Post(() => { authBusy = false; desktop.RefreshAuthentication(); RefreshAvailableModels(config.Backend); }); }
        });
    }

    private void SignOut()
    {
        RetireConversations(); ResetConnected();
        if (authBusy || !auth.IsSignedIn) return;
        if (editorSession.Busy) Cancel();
        login?.Cancel();
        if (phase is Phase.Arming or Phase.Recording or Phase.Processing) Cancel();
        var previous = authWork;
        authBusy = true;
        desktop.RefreshAuthentication();
        authWork = Task.Run(async () =>
        {
            try
            {
                await previous;
                await auth.LogoutAsync(lifetime.Token);
                desktop.Post(() => desktop.SetStatus("ChatGPT session removed from this device."));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { desktop.Post(() => desktop.SetStatus(ex.Message, isError: true, warningSound: true)); }
            finally { desktop.Post(() => { authBusy = false; desktop.RefreshAuthentication(); RefreshAvailableModels(config.Backend); }); }
        });
    }

    public void Dispose()
    {
        wakeTimer?.Dispose();
        editorTimer?.Dispose();
        StopSilenceWatch(); silenceTimer?.Dispose();
        lifetime.Cancel(); var stoppedWake = StopWakeAsync(); var stoppedDraft = StopLiveTranscription();
        desktop.ConnectedClosed = null; desktop.ConnectedCancelRequested = null; desktop.ConnectedQuestionRequested = null; desktop.ConnectedVoiceRequested = null; desktop.ConnectedReadRequested = null; desktop.ConnectedConfirmRequested = null; desktop.ConnectedDiscoveryRequested = null;
        ResetConnected(hide: false);
        CancelPendingVoice(); StopAnswerSpeech(); answer?.Ownership?.Dispose(); answer = null;
        editorBridge?.Dispose();
        login?.Cancel();
        operation?.Cancel();
        recorder.Cancel();
        selectionCapture.Dispose();
        try { Task.WhenAll(preloads.Append(stoppedWake).Append(stoppedDraft).Append(work).Append(authWork).Append(speechWork).Append(historyTasks.Completion)).GetAwaiter().GetResult(); }
        catch (Exception) when (lifetime.IsCancellationRequested) { /* Worker failures are already observed; always finish shutdown cleanup. */ }
        finally
        {
            recorder.Dispose(); speech.Dispose();
            wakePreparation?.Dispose();
            operation?.Dispose(); login?.Dispose(); lifetime.Dispose();
            speechCancellation?.Dispose(); speechCancellation = null;
            answerInsertion?.Dispose(); answerInsertion = null;
        }
    }
}
