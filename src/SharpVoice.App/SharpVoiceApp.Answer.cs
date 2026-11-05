using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.Refiner;

namespace SharpVoice.App;

internal sealed partial class SharpVoiceApp
{
    private sealed record AnswerContext(string Text, WindowTarget? Target, AppConfig Settings, DeferredDisposable<SelectionSnapshot>? Ownership, Guid HistoryId, string? SelectedContext, string ConversationId);
    private readonly AnswerSpeech answerSpeech = new();
    private AnswerContext? answer;
    private AnswerContext? answerReply;
    private CancellationTokenSource? speechCancellation;
    private Task speechWork = Task.CompletedTask;
    private long speechGeneration;
    private readonly VoiceStartGate voiceStart = new();
    private readonly SpeechInputGate speechInput = new();
    private bool pendingVoiceStart => voiceStart.Pending;

    private void InitializeAnswers()
    {
        desktop.AnswerSpeechChanged = enabled =>
        {
            var next = config with { AnswerSpeechEnabled = enabled };
            ConfigManager.Save(next); config = next;
            if (!enabled) StopAnswerSpeech();
            else if (answer is not null && phase == Phase.Idle && !pendingVoiceStart && !editorSession.Busy) StartAnswerSpeech(answer);
        };
        desktop.AnswerStopRequested = () => { if (answerReply is not null) Cancel(); else { StopAnswerSpeech(); desktop.SetAnswerStatus("Stopped · Replay to Read Again"); } };
        desktop.AnswerReplayRequested = () => { if (answer is not null && phase == Phase.Idle && !pendingVoiceStart && !editorSession.Busy) StartAnswerSpeech(answer); };
        desktop.AnswerVoiceRequested = ToggleAnswerReply;
        desktop.AnswerReplyRequested = StartTypedAnswerReply;
        desktop.AnswerClosed = () => { if (answerReply is not null) Cancel(); StopAnswerSpeech(); answer?.Ownership?.Dispose(); answer = null; };
    }
    private void PresentAnswer(string text, string model, WindowTarget destination, AppConfig settings, DeferredDisposable<SelectionSnapshot> ownership, Guid historyId, string request, string? source, string conversationId)
    {
        desktop.HideAnswer();
        answer = new(text, destination, settings, ownership, historyId, source, conversationId);
        desktop.SetPreview(text, retain: true); UpdateHistory(historyId, text, "Answered · Not Inserted");
        desktop.SetStatus("Answered · " + ReadyText()); desktop.ShowAnswer(text, model);
        desktop.SetAnswerConversation(conversations.Get(conversationId)!);
        if (config.AnswerSpeechEnabled) StartAnswerSpeech(answer);
    }
    private void StopAnswerSpeech()
    {
        if (!speechWork.IsCompleted && answer is not null) desktop.SetAnswerStatus("Stopped · Replay to Read Again");
        ++speechGeneration;
        speechCancellation?.Cancel();
    }
    private void StartAnswerSpeech(AnswerContext context)
    {
        StopAnswerSpeech(); var previous = speechWork;
        var id = ++speechGeneration;
        var owner = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); owner.CancelAfter(TimeSpan.FromMinutes(15));
        speechCancellation = owner;
        speechInput.Suspend(); desktop.StopInputPreview(); recorder.Configure(config.NoiseGateDb, true); // Clear pre-roll and suspend keep-ready input while speaking.
        speechWork = ReadAnswerAsync(previous, context, owner, id);
    }
    private async Task ReadAnswerAsync(Task previous, AnswerContext context, CancellationTokenSource owner, long id)
    {
        var ct = owner.Token;
        try
        {
            try { await previous.ConfigureAwait(false); } catch (Exception) { }
            ct.ThrowIfCancellationRequested();
            await answerSpeech.SpeakAsync(context.Text, config.AnswerVoice, message => desktop.Post(() =>
            { if (speechGeneration == id && ReferenceEquals(answer, context)) desktop.SetAnswerStatus(message); }), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { desktop.Post(() => { if (speechGeneration == id && ReferenceEquals(answer, context)) desktop.SetAnswerStatus("Readout Stopped or Timed Out · Full Answer Retained"); }); }
        catch (Exception) { desktop.Post(() => { if (speechGeneration == id && ReferenceEquals(answer, context)) desktop.SetAnswerStatus("Speech Unavailable · The Full Answer Is Still Here"); }); }
        finally
        {
            desktop.Post(() =>
            {
                if (ReferenceEquals(speechCancellation, owner))
                {
                    speechCancellation = null; owner.Dispose();
                    if (!lifetime.IsCancellationRequested) RestoreSpeechInput();
                }
                else owner.Dispose();
            });
        }
    }
    private void RestoreAnswerTarget(WindowTarget destination)
    {
        Win32.GetWindowThreadProcessId(destination.Handle, out var pid);
        if (pid != destination.ProcessId || pid == 0) throw new InvalidOperationException("The original text field is no longer available. Use Copy instead.");
        if (Win32.SetForegroundWindow(destination.Handle) == 0 && Win32.GetForegroundWindow() != destination.Handle)
            throw new InvalidOperationException("Could not return to the original text field. Use Copy instead.");
    }
    private void RestoreSpeechInput()
    {
        if (speechInput.Release()) { GuardWakeRearm(); recorder.Configure(config.NoiseGateDb, desktop.IsPaused); }
    }
    private void RequestVoicePressed(VoiceMode mode)
    {
        if (workspaceAsking) { if (phase == Phase.Recording) FinishWorkspaceRecording(); else if (phase == Phase.Arming) CancelWorkspaceRequest(); return; }
        if (answerReply is not null) { ToggleAnswerReply(); return; }
        if (mode == VoiceMode.AskAi && answer is not null && Win32.GetForegroundWindow() == desktop.ResponseHandle) { ToggleAnswerReply(); return; }
        if (pendingVoiceStart) { voiceStart.Promote(mode); return; }
        if (answer is null && speechWork.IsCompleted && !speechInput.Suspended) { Pressed(mode); return; }
        var source = Win32.GetForegroundWindow() == desktop.ResponseHandle ? answer?.Target : null;
        var expected = source ?? WindowTarget.Capture();
        StopAnswerSpeech(); desktop.HideAnswer();
        if (speechWork.IsCompleted)
        {
            if (source is not null) RestoreAnswerTarget(source);
            if (!expected.IsStillFocused()) throw new InvalidOperationException("The focused text field changed while speech stopped. Press the shortcut again in the field you want.");
            RestoreSpeechInput(); Pressed(mode); return;
        }
        var id = voiceStart.Begin(mode);
        desktop.SetStatus("Stopping spoken answer before recording…");
        _ = ResumeVoiceAfterSpeechAsync(speechWork, id, source, expected);
    }
    private async Task ResumeVoiceAfterSpeechAsync(Task stopping, long id, WindowTarget? source, WindowTarget expected)
    {
        try { await stopping.ConfigureAwait(false); } catch (Exception) { }
        desktop.Post(() =>
        {
            if (lifetime.IsCancellationRequested || !voiceStart.TryComplete(id, out var mode)) return;
            try
            {
                if (source is not null) RestoreAnswerTarget(source);
                if (!expected.IsStillFocused()) throw new InvalidOperationException("The focused text field changed while speech stopped. Press the shortcut again in the field you want.");
                RestoreSpeechInput(); Pressed(mode);
            }
            catch (Exception ex) { desktop.SetStatus(ex.Message, isError: true); }
            if (phase is not (Phase.Arming or Phase.Recording)) desktop.ResetRecordingGesture();
        });
    }
    private void CancelPendingVoice()
    { voiceStart.Cancel(); }
    private void BeginEditorAfterSpeech()
    {
        StopAnswerSpeech(); desktop.HideAnswer();
        if (speechWork.IsCompleted) { RestoreSpeechInput(); BeginEditorRecording(); return; }
        phase = Phase.Arming; var request = editorSession.Id;
        _ = ResumeEditorAfterSpeechAsync(speechWork, request);
    }
    private async Task ResumeEditorAfterSpeechAsync(Task stopping, string request)
    {
        try { await stopping.ConfigureAwait(false); } catch (Exception) { }
        desktop.Post(() =>
        {
            if (!editorSession.Busy || editorSession.Id != request || phase != Phase.Arming || lifetime.IsCancellationRequested) return;
            if (!EditorWindowFocused()) { Cancel(); return; }
            RestoreSpeechInput(); BeginEditorRecording();
        });
    }
    private bool CanReplyToAnswer() => answer is not null && phase == Phase.Idle && !pendingVoiceStart && !editorSession.Busy && !authBusy;
    private void StartTypedAnswerReply(string request)
    {
        if (!CanReplyToAnswer() || string.IsNullOrWhiteSpace(request)) return;
        var context = answer!;
        if (request.Length > 4000) { desktop.SetAnswerStatus("Keep replies under 4,000 characters."); return; }
        BeginAnswerOperation(context); phase = Phase.Processing;
        desktop.SetAnswerReplyState(true); desktop.SetAnswerStatus("Answering your reply…");
        var stopping = speechWork; var id = generation; var ct = operation!.Token;
        work = Task.Run(async () =>
        {
            try { await stopping.ConfigureAwait(false); ct.ThrowIfCancellationRequested(); await GenerateAnswerReplyAsync(context, request, id, ct); }
            catch (Exception ex) { ReportAnswerReplyFailure(context, id, ex); }
            finally { desktop.Post(() => FinishAnswerReply(context, id)); }
        });
    }
    private void BeginAnswerOperation(AnswerContext context)
    {
        StopAnswerSpeech(); answerReply = context;
        recordingConfig = context.Settings;
        operation?.Dispose(); operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); operation.CancelAfter(TimeSpan.FromMinutes(5));
        generation++; recordingModel = ""; desktop.SetActive(true);
    }
    private void ToggleAnswerReply()
    {
        if (answerReply is not null)
        {
            if (phase == Phase.Recording) FinishAnswerRecording(); else if (phase == Phase.Arming) Cancel();
            return;
        }
        if (!CanReplyToAnswer()) return;
        if (desktop.IsPaused) { desktop.SetAnswerStatus("Microphone paused · Choose Type Reply or resume the microphone"); return; }
        if (!microphoneReady) { desktop.SetAnswerStatus("Microphone unavailable · Choose Type Reply instead"); return; }
        var context = answer!; BeginAnswerOperation(context); phase = Phase.Arming;
        recordingConfig = context.Settings with { RecordingGesture = "toggle" }; recordingMode = VoiceMode.AskAi;
        desktop.SetDictationConfig(recordingConfig); desktop.SetVoiceMode(VoiceMode.AskAi); desktop.SetEditingSelection(false); desktop.SetCleanupSkipped(false);
        desktop.SetAnswerReplyState(true, arming: true); desktop.SetAnswerStatus("Starting microphone · Wait for the tone…");
        var stopping = speechWork; var id = generation; var ct = operation!.Token;
        work = ArmAnswerReplyAsync(context, stopping, id, ct);
    }
    private async Task ArmAnswerReplyAsync(AnswerContext context, Task stopping, int id, CancellationToken ct)
    {
        try
        {
            await stopping.ConfigureAwait(false); ct.ThrowIfCancellationRequested();
            desktop.Post(() =>
            {
                if (generation != id || phase != Phase.Arming || !ReferenceEquals(answer, context) || ct.IsCancellationRequested) return;
                try
                {
                    RestoreSpeechInput(); desktop.StopInputPreview();
                    preload = Task.Run(async () => { var path = await models.EnsureAsync(recordingConfig.SpeechModel, _ => { }, ct); await speech.PreloadAsync(path, ct); }, ct);
                    preloads.Add(preload); _ = preload.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    var armed = recorder.BeginAsync(ct);
                    if (armed.IsCompletedSuccessfully) CompleteRecordingStart(id); else work = AwaitRecordingStartAsync(armed, id);
                }
                catch (Exception ex) { Cancel(); desktop.SetAnswerStatus(ex.Message + " · Type Reply is available"); }
            });
        }
        catch (Exception ex) { ReportAnswerReplyFailure(context, id, ex); desktop.Post(() => { if (phase == Phase.Arming) FinishAnswerReply(context, id); }); }
    }
    private void FinishAnswerRecording()
    {
        if (answerReply is not { } context || phase != Phase.Recording) return;
        StopSilenceWatch(); phase = Phase.Processing; desktop.SetAnswerReplyState(true); desktop.SetAnswerStatus("Transcribing your reply…");
        var id = generation; var ct = operation!.Token; var settings = recordingConfig;
        work = Task.Run(async () =>
        {
            try
            {
                var stoppedDraft = StopLiveTranscription();
                using var audio = await recorder.EndAsync(ct);
                desktop.Post(() => { if (generation == id) desktop.SetRecording(false); });
                await stoppedDraft;
                if (!audio.HasSpeech) throw new InvalidOperationException("No speech detected · Try again or choose Type Reply");
                await preload;
                var path = await models.EnsureAsync(settings.SpeechModel, _ => { }, ct);
                var transcript = await speech.TranscribeAsync(path, audio.Samples, settings.Language, _ => { }, ct, fast: settings.FastTranscription);
                if (string.IsNullOrWhiteSpace(transcript)) throw new InvalidOperationException("No text transcribed · Try again or choose Type Reply");
                await GenerateAnswerReplyAsync(context, transcript, id, ct);
            }
            catch (Exception ex) { ReportAnswerReplyFailure(context, id, ex); }
            finally { desktop.Post(() => FinishAnswerReply(context, id)); }
        });
    }
    private async Task GenerateAnswerReplyAsync(AnswerContext context, string request, int id, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var view = conversations.Get(context.ConversationId) ?? throw new OperationCanceledException();
        var lease = conversations.Begin(view.Id, request);
        string text;
        try { text = await AnswerConversation.From(view, context.SelectedContext).ReplyAsync(refiner, request, context.Target?.ProcessName ?? "", context.Settings with { AnswerVerbosity = config.AnswerVerbosity }, timeout.Token); }
        catch { conversations.Cancel(lease); throw; }
        if (ct.IsCancellationRequested) { conversations.Cancel(lease); ct.ThrowIfCancellationRequested(); }
        if (!CompleteAskTurn(lease, text, "Answered")) throw new OperationCanceledException();
        ct.ThrowIfCancellationRequested(); var model = recordingModel;
        desktop.Post(() =>
        {
            if (generation != id || ct.IsCancellationRequested || !ReferenceEquals(answer, context)) return;
            var next = context with { Text = text };
            answer = next;
            desktop.ShowAnswer(text, model); desktop.SetAnswerConversation(conversations.Get(context.ConversationId)!); desktop.SetPreview(text, retain: true);
            // A follow-up is retained in this popup, not written over the original dictation history entry.
            if (config.AnswerSpeechEnabled) StartAnswerSpeech(next);
        });
    }
    private void ReportAnswerReplyFailure(AnswerContext context, int id, Exception ex) => desktop.Post(() =>
    {
        if (generation != id || !ReferenceEquals(answer, context)) return;
        desktop.SetAnswerStatus(ex is OperationCanceledException ? "Reply canceled · Previous answer retained" : ex.Message);
        desktop.SetStatus(ex is OperationCanceledException ? "Reply canceled" : ex.Message, isError: ex is not OperationCanceledException, warningSound: ex is not OperationCanceledException);
    });
    private void FinishAnswerReply(AnswerContext context, int id, bool canceled = false)
    {
        if (generation != id || !ReferenceEquals(answerReply, context)) return;
        if (phase == Phase.Cancelling && !canceled) return; // Wait until native microphone closure has completed.
        answerReply = null; phase = Phase.Idle; desktop.SetRecording(false); desktop.SetActive(false);
        desktop.SetAnswerReplyState(false);
        if (speechWork.IsCompleted) RestoreSpeechInput(); PublishWorkspace();
    }
    private void CancelAnswerReply()
    {
        if (answerReply is not { } context) return;
        _ = StopLiveTranscription();
        StopSilenceWatch(); operation?.Cancel(); StopAnswerSpeech(); phase = Phase.Cancelling;
        desktop.SetRecording(false); desktop.SetAnswerReplyState(true); desktop.SetAnswerStatus("Canceling reply…");
        var previous = work; var closing = recorder.CancelAsync(); var id = generation;
        work = Task.Run(async () =>
        {
            try { await previous.ConfigureAwait(false); } catch (Exception) { }
            try { await closing.ConfigureAwait(false); } catch (Exception) { }
            desktop.Post(() => { FinishAnswerReply(context, id, canceled: true); if (ReferenceEquals(answer, context)) desktop.SetAnswerStatus("Reply canceled · Previous answer retained"); });
        });
    }
}
