using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Refiner;

namespace SharpVoice.App;

internal sealed partial class SharpVoiceApp
{
    private readonly EditorSession editorSession = new();
    private EditorBridge? editorBridge;
    private Timer? editorTimer;
    private WindowTarget? editorWindow;
    private TaskCompletionSource<bool>? editorValidated;
    private string editorValidationNonce = "";
    private string editorThread = "";
    private ConversationLease? editorTurn;
    private string EditorDisplayName => editorWindow?.ProcessName.StartsWith("antigravity", StringComparison.OrdinalIgnoreCase) == true ? "Antigravity" : "VS Code";

    private void StartEditorBridge()
    {
        try
        {
            editorBridge = new(EditorBridge.DefaultDiscoveryPath, HandleEditorFrameAsync, () => desktop.Post(WorkspaceDisconnected), conversations.HostId);
            editorBridge.Start();
            editorTimer = new Timer(_ => desktop.Post(() =>
            {
                if (editorSession.Busy && (editorSession.IsExpired(DateTimeOffset.UtcNow) || editorSession.State != "applying" && !EditorWindowFocused())) Cancel();
            }), null, 500, 500);
        }
        catch { editorBridge?.Dispose(); editorBridge = null; desktop.SetStatus("VS Code integration unavailable. Check access to your local SharpVoice editor folder.", isError: true); }
    }
    private bool EditorWindowFocused()
    {
        var current = WindowTarget.Capture();
        return editorWindow is not null && current.Handle == editorWindow.Handle && current.ProcessId == editorWindow.ProcessId;
    }
    private Task<EditorFrame> HandleEditorFrameAsync(EditorFrame frame, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<EditorFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        desktop.Post(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested(); lifetime.Token.ThrowIfCancellationRequested();
                LastUpdateActivity = Environment.TickCount64;
                if (frame.Type.StartsWith("workspace_", StringComparison.Ordinal)) { HandleWorkspace(frame); completion.TrySetResult(new("ack", frame.Id)); return; }
                if (frame.Type == "start")
                {
                    if (phase != Phase.Idle || authBusy || desktop.IsPaused || editorSession.Busy) throw new InvalidOperationException("SharpVoice is busy or paused. Finish or cancel the current operation first.");
                    BindWorkspace(frame);
                    var window = WindowTarget.Capture();
                    if (!EditorPlugins.IsSupportedEditor(window.ProcessName)) throw new InvalidOperationException("Focus a local VS Code or Antigravity source editor first.");
                    var effective = WritingTools.Resolve(config, window.ProcessName);
                    if (effective.Backend == "none") throw new InvalidOperationException("Choose an AI provider in SharpVoice AI & Models or this editor's app profile first.");
                    if (frame.Instruction is null && !microphoneReady || frame.Context is null) throw new InvalidOperationException("Editor context or microphone is unavailable.");
                    editorSession.Start(frame.Id, frame.Mode ?? "", frame.Context, DateTimeOffset.UtcNow); editorWindow = window;
                    recordingConfig = effective with { RecordingGesture = "toggle" }; recordingModel = "";
                    workspaceMode = frame.Mode == "edit" ? "edit" : "dictate"; workspaceContext = frame.Context; workspaceIncludeFile = false;
                    workspaceConversation = ""; editorThread = WorkspaceView().Id; workspaceMessage = frame.Instruction is null ? "Starting microphone · Wait for the tone…" : "Working…";
                    if (!string.IsNullOrWhiteSpace(frame.Instruction))
                    {
                        if (frame.Instruction.Length > 4000) throw new ArgumentException("Code instruction is too long.");
                        operation?.Dispose(); operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); operation.CancelAfter(TimeSpan.FromMinutes(2));
                        generation++; phase = Phase.Processing; editorSession.Processing(frame.Id); RunEditorGeneration(frame.Instruction, generation, operation.Token);
                    }
                    else BeginEditorAfterSpeech();
                    PublishWorkspace();
                }
                else
                {
                    if (!editorSession.Busy || frame.Id != editorSession.Id) throw new InvalidOperationException("This editor request has expired.");
                    switch (frame.Type)
                    {
                        case "stop": if (phase == Phase.Arming) Cancel(); else if (phase == Phase.Recording) FinishEditorRecording(); break;
                        case "cancel": Cancel(); break;
                        case "validated":
                            if (editorValidated is not null && !editorValidated.Task.IsCompleted && frame.Message == editorValidationNonce)
                            {
                                if (frame.Context is not null) editorSession.RefreshContext(frame.Id, frame.Context);
                                editorValidated.TrySetResult(true);
                            }
                            break;
                        case "clarify":
                            if (phase != Phase.Idle || !EditorWindowFocused()) throw new InvalidOperationException("Focus the original editor before answering.");
                            editorSession.BeginClarification(frame.Id, frame.Context);
                            if (!string.IsNullOrWhiteSpace(frame.Instruction))
                            {
                                if (frame.Instruction.Length > 4000) throw new ArgumentException("Clarification is too long.");
                                operation?.Dispose(); operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); operation.CancelAfter(TimeSpan.FromMinutes(2));
                                phase = Phase.Processing; editorSession.Processing(frame.Id); RunEditorGeneration(frame.Instruction, generation, operation.Token);
                            }
                            else BeginEditorAfterSpeech();
                            break;
                        case "applying":
                            if (!EditorWindowFocused()) throw new InvalidOperationException("Focus the original editor before applying.");
                            editorSession.BeginApply(frame.Id);
                            if (editorTurn is not null) conversations.Mark(editorTurn.ConversationId, editorTurn.TurnId, "Applying");
                            workspaceMessage = "Applying approved edit…"; SendEditor(new("apply_ready", frame.Id)); PublishWorkspace(); break;
                        case "apply_failed":
                            if (editorSession.State != "applying") throw new InvalidOperationException("No edit is being applied.");
                            if (editorTurn is not null) conversations.Mark(editorTurn.ConversationId, editorTurn.TurnId, "Failed");
                            editorSession.Cancel(); editorWindow = null; workspaceMessage = "Edit failed · Document was not changed"; desktop.SetActive(false); PublishWorkspace(); break;
                        case "applied":
                            if (editorSession.State != "applying") throw new InvalidOperationException("No submitted edit is awaiting a receipt.");
                            if (editorTurn is not null) conversations.Mark(editorTurn.ConversationId, editorTurn.TurnId, "Applied");
                            workspaceMessage = "Applied · Undo is available";
                    var editorName = EditorDisplayName;
                            editorSession.Complete(frame.Id); editorWindow = null; desktop.SetActive(false);
                            desktop.PlayInsertionSuccess(); desktop.SetStatus($"Inserted · Applied in {editorName}", true); PublishWorkspace(); break;
                        default: throw new InvalidDataException("Unknown editor command.");
                    }
                }
                completion.TrySetResult(new("ack", frame.Id));
            }
            catch (Exception ex) { if (frame.Type is "start" or "clarify" && editorSession.Busy) Cancel(); completion.TrySetException(ex); }
        });
        return completion.Task.WaitAsync(ct);
    }
    private void BeginEditorRecording()
    {
        workspaceMessage = "Starting microphone · Wait for the tone…";
        operation?.Dispose(); operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); operation.CancelAfter(TimeSpan.FromMinutes(5));
        generation++; phase = Phase.Arming; recordingMode = VoiceMode.AskAi;
        desktop.StopInputPreview(); desktop.SetDictationConfig(recordingConfig); desktop.SetVoiceMode(VoiceMode.AskAi); desktop.SetEditingSelection(false); desktop.SetCleanupSkipped(false);
        desktop.SetActive(true); desktop.SetPreview(""); desktop.SetStatus($"Starting microphone for {EditorDisplayName} · Wait for the tone…", true);
        SendEditor(new("status", editorSession.Id, Message: "arming"));
        var ct = operation.Token;
        var armed = recorder.BeginAsync(ct);
        preload = Task.Run(async () => { var path = await models.EnsureAsync(recordingConfig.SpeechModel, _ => { }, ct); await speech.PreloadAsync(path, ct); }, ct);
        preloads.RemoveAll(t => t.IsCompleted); preloads.Add(preload);
        _ = preload.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        if (armed.IsCompletedSuccessfully) CompleteRecordingStart(generation); else work = AwaitRecordingStartAsync(armed, generation);
    }
    private void FinishEditorRecording()
    {
        if (phase != Phase.Recording || !editorSession.Busy) return;
        StopSilenceWatch(); phase = Phase.Processing; editorSession.Processing(editorSession.Id);
        var id = generation; var ct = operation!.Token; var settings = recordingConfig; var requestId = editorSession.Id;
        workspaceMessage = "Finalizing Transcript…"; PublishWorkspace();
        SendEditor(new("status", requestId, Message: "processing")); desktop.SetStatus($"Finishing audio for {EditorDisplayName}…", true);
        work = Task.Run(async () =>
        {
            try
            {
                var stoppedDraft = StopLiveTranscription();
                using var audio = await recorder.EndAsync(ct);
                desktop.Post(() => { if (generation == id) desktop.SetRecording(false); });
                await stoppedDraft;
                if (!audio.HasSpeech) throw new InvalidOperationException("No speech detected. Lower the noise gate or move closer to the microphone.");
                await preload;
                var path = await models.EnsureAsync(settings.SpeechModel, _ => { }, ct);
                var transcript = await speech.TranscribeAsync(path, audio.Samples, settings.Language, _ => { }, ct, fast: settings.FastTranscription);
                if (string.IsNullOrWhiteSpace(transcript)) throw new InvalidOperationException("No text was transcribed. No AI request was made.");
                desktop.Post(() => { if (generation == id) desktop.SetPreview(transcript, retain: true); });
                await GenerateEditorAsync(transcript, id, ct);
            }
            catch (Exception ex) { FailEditor(ex, id); }
            finally { FinishEditorWork(id); }
        });
    }
    private void RunEditorGeneration(string clarification, int id, CancellationToken ct)
        => work = Task.Run(async () => { try { await GenerateEditorAsync(clarification, id, ct); } catch (Exception ex) { FailEditor(ex, id); } finally { FinishEditorWork(id); } });
    private async Task GenerateEditorAsync(string transcript, int id, CancellationToken ct)
    {
        var requestId = editorSession.Id; var mode = editorSession.Mode;
        var instruction = editorSession.Instruction.Length == 0 ? transcript : editorSession.Instruction + "\nUser clarification: " + transcript;
        if (instruction.Length > 16000) throw new InvalidOperationException("Code request is too long. Start again with a shorter instruction.");
        var history = conversations.Get(editorThread) ?? throw new OperationCanceledException();
        var turn = conversations.Begin(history.Id, transcript); editorTurn = turn;
        var validated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); editorValidated = validated;
        editorValidationNonce = Guid.NewGuid().ToString("N");
        await editorBridge!.SendAsync(new("validate", requestId, Message: editorValidationNonce));
        await validated.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        ct.ThrowIfCancellationRequested();
        var context = editorSession.Context!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(2));
        desktop.Post(() => { if (generation == id && editorSession.Busy) { workspaceMessage = "Thinking…"; PublishWorkspace(); } });
        EditorResult result;
        try { result = await new EditorRefiner(refiner).GenerateAsync(new(mode, instruction, context, history), recordingConfig, deadline.Token); ct.ThrowIfCancellationRequested(); }
        catch { conversations.Cancel(turn); throw; }
        if (!conversations.Complete(turn, result.Kind == "clarify" || result.Kind == "unsupported" ? result.Question : result.Text, result.Kind == "clarify" ? "Needs Clarification" : result.Kind == "unsupported" ? "Unsupported" : "Proposed", recordingModel)) throw new OperationCanceledException();
        ct.ThrowIfCancellationRequested();
        desktop.Post(() =>
        {
            if (generation != id || !editorSession.Busy || editorSession.Id != requestId || ct.IsCancellationRequested) return;
            if (!EditorWindowFocused()) { Cancel(); return; }
            editorSession.Instruction = instruction + (result.Kind == "clarify" ? "\nClarification asked: " + result.Question : "");
            editorSession.Propose(requestId, result); phase = Phase.Idle; desktop.SetRecording(false);
            desktop.SetStatus(result.Kind == "clarify" ? $"Needs clarification in {EditorDisplayName}" : result.Kind == "unsupported" ? $"{EditorDisplayName} request unsupported" : $"Review in {EditorDisplayName}", true);
            workspaceMessage = result.Kind == "clarify" ? "Needs Clarification" : result.Kind == "unsupported" ? "Request Unsupported" : "Review the proposed change";
            SendEditor(new("result", requestId, Result: result, Model: recordingModel));
            PublishWorkspace();
            if (result.Kind == "unsupported") { editorSession.Cancel(); editorWindow = null; desktop.SetActive(false); }
        });
    }
    private void FinishEditorWork(int id) => desktop.Post(() => { if (generation == id && phase == Phase.Cancelling) { phase = Phase.Idle; desktop.SetRecording(false); desktop.SetActive(false); PublishWorkspace(); } });
    private void FailEditor(Exception ex, int id) => desktop.Post(() =>
    {
        if (generation != id || !editorSession.Busy) return;
        if (editorTurn is not null) conversations.Complete(editorTurn, "", ex is OperationCanceledException ? "Canceled" : "Failed", recordingModel);
        SendEditor(new("error", editorSession.Id, Message: ex is OperationCanceledException ? "Code request cancelled or timed out." : ex.Message));
        Cancel(); desktop.SetStatus(ex is OperationCanceledException ? "Code request cancelled or timed out." : ex.Message, true, isError: true, warningSound: true);
    });
    private void SendEditor(EditorFrame frame)
    {
        if (editorBridge is null) return;
        var task = editorBridge.SendAsync(frame);
        preloads.RemoveAll(t => t.IsCompleted); preloads.Add(task);
        _ = task.ContinueWith(_ => desktop.Post(() => { if (editorSession.Busy) Cancel(); }), TaskContinuationOptions.OnlyOnFaulted);
    }
    private void CancelEditor()
    {
        if (!editorSession.Busy) return;
        if (editorTurn is not null) { conversations.Cancel(editorTurn); if (editorSession.State is "proposal" or "applying") conversations.Mark(editorTurn.ConversationId, editorTurn.TurnId, editorSession.State == "applying" ? "Outcome Unknown" : "Canceled"); }
        var committing = editorSession.State == "applying";
        var id = editorSession.Id; var editorName = EditorDisplayName; editorSession.Cancel(); editorWindow = null; editorValidated?.TrySetCanceled(); editorValidated = null; editorValidationNonce = "";
        workspaceMessage = committing ? "Edit Outcome Unknown · Check the document before retrying" : "Canceled · Conversation retained";
        operation?.Cancel(); SendEditor(new("cancelled", id)); PublishWorkspace(); desktop.SetActive(false); desktop.SetStatus($"{editorName} voice edit cancelled.");
    }
}
