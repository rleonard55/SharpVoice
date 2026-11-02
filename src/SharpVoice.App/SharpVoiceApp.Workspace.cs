using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Refiner;

namespace SharpVoice.App;

internal sealed partial class SharpVoiceApp
{
    private readonly EditorWorkspaceBinding workspaceBinding = new();
    private string workspaceClient { get => workspaceBinding.ClientId; set => workspaceBinding.ClientId = value; }
    private string workspacePeer => workspaceBinding.Peer;
    private string workspaceKey => workspaceBinding.Workspace;
    private string workspaceMode { get => workspaceBinding.Mode; set => workspaceBinding.Mode = value; }
    private bool workspaceIncludeFile { get => workspaceBinding.IncludeFile; set => workspaceBinding.IncludeFile = value; }
    private EditorContext? workspaceContext { get => workspaceBinding.Context; set => workspaceBinding.Context = value; }
    private string workspaceConversation { get => workspaceBinding.ConversationId; set => workspaceBinding.ConversationId = value; }
    private ConversationView? workspaceBefore;
    private bool workspaceAsking;
    private int workspaceSequence;
    private ConversationLease? workspaceLease;
    private string workspaceMessage = "Ready";
    private AppConfig WorkspaceSettings => WritingTools.Resolve(config, "code");
    private ConversationScope WorkspaceScope => workspaceMode is "dictate" or "edit"
        ? new("code", workspacePeer + "|" + workspaceKey + "|" + workspaceContext?.Uri, WorkspaceSettings.Backend + ":" + conversationIdentity)
        : workspaceIncludeFile ? new("editor_ask", workspacePeer + "|" + workspaceKey + "|" + workspaceContext?.Uri, WorkspaceSettings.Backend + ":" + conversationIdentity)
        : GeneralScope(WorkspaceSettings);
    private void BindWorkspace(EditorFrame frame)
    {
        if (frame.ClientId is null || frame.ClientScope is null) throw new InvalidOperationException("Update the SharpVoice app and plugin together.");
        if (workspaceClient != frame.ClientId && workspaceAsking) CancelWorkspaceRequest();
        if (frame.Workspace is not null && workspaceKey != frame.Workspace && (workspaceAsking || editorSession.Busy)) throw new InvalidOperationException("Finish the current request before switching workspaces.");
        if (workspaceBinding.Bind(frame.ClientId, frame.ClientScope, frame.Workspace ?? workspaceKey)) workspaceConversation = "";
    }
    private ConversationView WorkspaceView() => workspaceBinding.ResolveConversation(conversations, WorkspaceScope);
    private void PublishWorkspace()
    {
        if (workspaceClient.Length == 0 || editorBridge is null || lifetime.IsCancellationRequested) return;
        var view = WorkspaceView();
        var display = ConversationStore.Presentation(view);
        var state = workspaceAsking ? phase switch { Phase.Arming => "arming", Phase.Recording => "recording", Phase.Processing => "processing", Phase.Cancelling => "processing", _ => "idle" }
            : editorSession.Busy ? editorSession.State switch { "proposal" => "review", "clarify" => "clarify", "applying" => "applying", _ => phase == Phase.Arming ? "arming" : phase == Phase.Recording ? "recording" : "processing" } : phase is Phase.Idle ? "idle" : "busy";
        var frame = new EditorFrame("workspace", Conversation: display,
            Conversations: conversations.List(view.Scope).Select(x => new ConversationSummary(x.Id, x.Title, x.Scope.Domain, x.Retired)).ToArray(),
            Message: state == "busy" ? "SharpVoice is busy · Finish the current recording or request" : workspaceMessage, State: state, Model: recordingModel, Context: workspaceContext,
            Mode: workspaceMode, IncludeFile: workspaceIncludeFile, ConversationId: view.Id, TurnId: editorSession.Busy ? editorSession.Id : workspaceLease?.TurnId,
            HostId: conversations.HostId, ClientId: workspaceClient, Sequence: ++workspaceSequence);
        var sent = editorBridge.SendAsync(frame); preloads.RemoveAll(t => t.IsCompleted); preloads.Add(sent);
        _ = sent.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }
    private void WorkspaceDisconnected()
    {
        if (workspaceAsking) CancelWorkspaceRequest();
        if (editorSession.Busy) Cancel();
        workspaceClient = "";
    }
    private void HandleWorkspace(EditorFrame frame)
    {
        BindWorkspace(frame);
        if (frame.Type == "workspace_copy")
        {
            if (frame.Id.Length is < 1 or > 128 || frame.ConversationId is null || frame.TurnId is null) throw new InvalidDataException("Invalid copy request.");
            var text = conversations.Answer(frame.ConversationId, frame.TurnId, WorkspaceScope);
            var client = workspaceClient;
            var copy = Task.Run(async () =>
            {
                for (var offset = 0; offset < text.Length; offset += 32000)
                    await editorBridge!.SendAsync(new("answer_copy", frame.Id, Message: text.Substring(offset, Math.Min(32000, text.Length - offset)), ClientId: client, ConversationId: frame.ConversationId, TurnId: frame.TurnId, Sequence: offset / 32000 + 1, State: offset + 32000 >= text.Length ? "complete" : "chunk"));
            });
            preloads.Add(copy); _ = copy.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); return;
        }
        if (frame.Type == "workspace_cancel") { if (workspaceAsking) CancelWorkspaceRequest(); else if (editorSession.Busy) Cancel(); PublishWorkspace(); return; }
        if (frame.Type == "workspace_stop") { if (workspaceAsking && phase == Phase.Recording) FinishWorkspaceRecording(); else if (workspaceAsking && phase == Phase.Arming) CancelWorkspaceRequest(); return; }
        if (frame.Type == "workspace_sync") { PublishWorkspace(); return; }
        if ((frame.Type is "workspace_new" or "workspace_select" or "workspace_delete" or "workspace_clear") && phase is not (Phase.Idle or Phase.Starting) && !authBusy)
        {
            Cancel(); var stopping = work; var id = generation; var client = workspaceClient;
            preloads.Add(Task.Run(async () =>
            {
                try { await stopping.ConfigureAwait(false); } catch (Exception) { }
                desktop.Post(() => { if (generation == id && client == workspaceClient && phase == Phase.Idle && !lifetime.IsCancellationRequested) { try { HandleWorkspace(frame); } catch (Exception ex) { workspaceMessage = ex.Message; PublishWorkspace(); } } });
            }));
            return;
        }
        if (phase != Phase.Idle || editorSession.Busy || authBusy) throw new InvalidOperationException("Finish or cancel the current operation first.");
        if (frame.Type is "workspace_mode" or "workspace_new" or "workspace_ask" or "workspace_voice")
        {
            var mode = frame.Mode ?? "ask";
            if (mode is not ("ask" or "dictate" or "edit")) throw new InvalidDataException("Unsupported panel mode.");
            if (frame.IncludeFile || mode != "ask") { if (frame.Context is null) throw new InvalidDataException("Current document context is required."); EditorValidation.ValidateContext(frame.Context); }
            workspaceMode = mode; workspaceIncludeFile = frame.IncludeFile; workspaceContext = frame.IncludeFile || mode != "ask" ? frame.Context : null;
        }
        switch (frame.Type)
        {
            case "workspace_mode": workspaceConversation = ""; break;
            case "workspace_new": workspaceConversation = conversations.New(WorkspaceScope).Id; workspaceMessage = "New conversation"; break;
            case "workspace_select":
                if (frame.ConversationId is null || !conversations.Select(frame.ConversationId, WorkspaceScope)) throw new InvalidOperationException("That conversation belongs to another context or identity.");
                workspaceConversation = frame.ConversationId; workspaceMessage = "Conversation restored"; break;
            case "workspace_delete":
                if (frame.ConversationId is null || conversations.Get(frame.ConversationId)?.Scope != WorkspaceScope) throw new InvalidOperationException("Conversation unavailable.");
                conversations.Delete(frame.ConversationId); workspaceConversation = ""; break;
            case "workspace_clear":
                foreach (var thread in conversations.List(WorkspaceScope)) conversations.Delete(thread.Id); workspaceConversation = ""; break;
            case "workspace_ask": StartWorkspaceAsk(frame.Instruction ?? ""); return;
            case "workspace_voice": StartWorkspaceVoice(); return;
            default: throw new InvalidDataException("Unsupported workspace action.");
        }
        PublishWorkspace();
    }
    private void BeginWorkspaceOperation()
    {
        if (WorkspaceSettings.Backend == "none") throw new InvalidOperationException("Choose an AI provider in SharpVoice AI & Models first.");
        var window = WindowTarget.Capture();
        if (!EditorPlugins.IsSupportedEditor(window.ProcessName)) throw new InvalidOperationException("Focus the VS Code window before using its SharpVoice panel.");
        StopAnswerSpeech(); desktop.HideAnswer();
        workspaceBefore = WorkspaceView(); operation?.Dispose(); operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); operation.CancelAfter(TimeSpan.FromMinutes(5));
        workspaceAsking = true; recordingConfig = WorkspaceSettings with { RecordingGesture = "toggle" }; recordingMode = VoiceMode.AskAi; recordingModel = ""; generation++;
        desktop.SetActive(true); desktop.SetDictationConfig(recordingConfig); desktop.SetVoiceMode(VoiceMode.AskAi); desktop.SetEditingSelection(false);
    }
    private void StartWorkspaceAsk(string request)
    {
        if (string.IsNullOrWhiteSpace(request) || request.Length > 4000) throw new ArgumentException("Enter a reply of 1–4,000 characters.");
        BeginWorkspaceOperation(); phase = Phase.Processing; workspaceMessage = "Answering your request…"; PublishWorkspace();
        var id = generation; var ct = operation!.Token; var stopping = speechWork;
        work = Task.Run(async () =>
        {
            try { await stopping.ConfigureAwait(false); ct.ThrowIfCancellationRequested(); await GenerateWorkspaceAnswer(request, id, ct); }
            catch (Exception ex) { FailWorkspace(ex, id); }
            finally { desktop.Post(() => FinishWorkspace(id)); }
        });
    }
    private void StartWorkspaceVoice()
    {
        if (!microphoneReady || desktop.IsPaused) throw new InvalidOperationException("Microphone unavailable or paused. Type your request instead.");
        BeginWorkspaceOperation(); phase = Phase.Arming; workspaceMessage = "Starting microphone · Wait for the tone…"; PublishWorkspace();
        var id = generation; var ct = operation!.Token; var stopping = speechWork;
        work = Task.Run(async () =>
        {
            try
            {
                await stopping.ConfigureAwait(false); ct.ThrowIfCancellationRequested();
                desktop.Post(() =>
                {
                    if (id != generation || !workspaceAsking || phase != Phase.Arming || ct.IsCancellationRequested) return;
                    try
                    {
                        RestoreSpeechInput(); desktop.StopInputPreview();
                        preload = Task.Run(async () => { var path = await models.EnsureAsync(recordingConfig.SpeechModel, _ => { }, ct); await speech.PreloadAsync(path, ct); }, ct);
                        preloads.Add(preload); _ = preload.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                        var armed = recorder.BeginAsync(ct); if (armed.IsCompletedSuccessfully) CompleteRecordingStart(id); else work = AwaitRecordingStartAsync(armed, id);
                    }
                    catch (Exception ex) { FailWorkspace(ex, id); CancelWorkspaceRequest(); }
                });
            }
            catch (Exception ex) { FailWorkspace(ex, id); desktop.Post(() => FinishWorkspace(id)); }
        });
    }
    private void FinishWorkspaceRecording()
    {
        if (!workspaceAsking || phase != Phase.Recording) return;
        StopSilenceWatch(); phase = Phase.Processing; workspaceMessage = "Finalizing Transcript…"; PublishWorkspace();
        var id = generation; var ct = operation!.Token; var settings = recordingConfig;
        work = Task.Run(async () =>
        {
            try
            {
                var stoppedDraft = StopLiveTranscription();
                using var audio = await recorder.EndAsync(ct);
                desktop.Post(() => { if (generation == id) desktop.SetRecording(false); });
                await stoppedDraft;
                if (!audio.HasSpeech) throw new InvalidOperationException("No speech detected · Try again or type your request");
                await preload; var path = await models.EnsureAsync(settings.SpeechModel, _ => { }, ct);
                var transcript = await speech.TranscribeAsync(path, audio.Samples, settings.Language, _ => { }, ct, fast: settings.FastTranscription);
                if (string.IsNullOrWhiteSpace(transcript)) throw new InvalidOperationException("No text transcribed · Try again or type your request");
                await GenerateWorkspaceAnswer(transcript, id, ct);
            }
            catch (Exception ex) { FailWorkspace(ex, id); }
            finally { desktop.Post(() => FinishWorkspace(id)); }
        });
    }
    private async Task GenerateWorkspaceAnswer(string request, int id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var view = workspaceBefore ?? throw new OperationCanceledException();
        if (view.Id != workspaceConversation || view.Scope != WorkspaceScope || conversations.Get(view.Id)?.Retired != false) throw new OperationCanceledException();
        var lease = conversations.Begin(view.Id, request); workspaceLease = lease;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(2));
            var context = workspaceIncludeFile ? workspaceContext?.ContextText : null;
            var response = await AnswerConversation.From(view, context).ReplyAsync(refiner, request, "code", recordingConfig, deadline.Token);
            ct.ThrowIfCancellationRequested(); if (!CompleteAskTurn(lease, response, "Answered")) throw new OperationCanceledException();
            desktop.Post(() => { if (id == generation && !ct.IsCancellationRequested) { workspaceMessage = "Answered · Reply by voice or text"; PublishWorkspace(); } });
        }
        finally { conversations.Cancel(lease); }
    }
    private void FailWorkspace(Exception ex, int id) => desktop.Post(() =>
    {
        if (id != generation) return;
        workspaceMessage = ex is OperationCanceledException ? "Request canceled · Previous conversation retained" : ex.Message;
        desktop.SetStatus(workspaceMessage, isError: ex is not OperationCanceledException, warningSound: ex is not OperationCanceledException); PublishWorkspace();
    });
    private void FinishWorkspace(int id, bool canceled = false)
    {
        if (generation != id || !workspaceAsking || phase == Phase.Cancelling && !canceled) return;
        workspaceAsking = false; workspaceLease = null; phase = Phase.Idle; desktop.SetRecording(false); desktop.SetActive(false); RestoreSpeechInput(); PublishWorkspace();
    }
    private void CancelWorkspaceRequest()
    {
        if (!workspaceAsking || phase == Phase.Cancelling) return;
        _ = StopLiveTranscription();
        StopSilenceWatch(); operation?.Cancel(); StopAnswerSpeech(); phase = Phase.Cancelling;
        workspaceMessage = "Canceling request…"; PublishWorkspace();
        var previous = work; var closed = recorder.CancelAsync(); var id = generation;
        work = Task.Run(async () =>
        {
            try { await previous.ConfigureAwait(false); } catch (Exception) { }
            try { await closed.ConfigureAwait(false); } catch (Exception) { }
            desktop.Post(() => { if (id == generation) { workspaceMessage = "Canceled · Conversation retained"; FinishWorkspace(id, canceled: true); } });
        });
    }
}
