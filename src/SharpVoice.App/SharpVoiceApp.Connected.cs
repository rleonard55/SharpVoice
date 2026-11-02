using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Refiner;

namespace SharpVoice.App;

internal sealed partial class SharpVoiceApp
{
    private readonly object connectedGate = new();
    private ConnectedConversation? connectedConversation;
    private CancellationTokenSource? connectedCancellation;
    private TaskCompletionSource<bool>? connectedDecision;
    private Guid? connectedChangeId;
    private long connectedRevision;
    private string connectedAnswer = "";
    private string connectedThread = "";
    private Action<ConnectedView>? connectedDisplay;
    private Func<ConnectedChange, CancellationToken, Task<bool>>? connectedReview;
    private ConversationScope ConnectedScope(AppConfig settings) => new("connected", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(settings.ConnectedCodexPath + "|" + string.Join("|", settings.ConnectedTools.Order(StringComparer.Ordinal))))), "chatgpt_oauth:" + conversationIdentity);

    private void InitializeConnected()
    {
        desktop.ConnectedDiscoveryRequested = async (path, ct) =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            await using var rpc = CodexRpc.Start(path);
            await using var gateway = await CodexGateway.OpenAsync(rpc, linked.Token);
            return gateway.Tools.ToArray();
        };
        desktop.ConnectedConfirmRequested = id =>
        {
            lock (connectedGate)
            {
                if (connectedChangeId != id) return;
                connectedChangeId = null;
                connectedDecision?.TrySetResult(true);
            }
        };
        desktop.ConnectedCancelRequested = () =>
        {
            Cancel(); var revision=connectedRevision;var stopping=work;
            preloads.Add(CompleteConnectedCancellationAsync(stopping,revision));
        };
        desktop.ConnectedClosed = () => { Cancel(); StopAnswerSpeech(); lock (connectedGate) { connectedRevision++; connectedDisplay = null; connectedReview = null; } };
        desktop.ConnectedNewRequested = () =>
        {
            if (phase != Phase.Idle) { desktop.SetConnectedStatus("Cancel the active request before starting a new conversation"); return; }
            ResetConnected(hide: false); desktop.ShowConnected(new("Ready", "Start a new connected conversation by voice or text."));
        };
        desktop.ConnectedQuestionRequested = StartConnectedQuestion;
        desktop.ConnectedVoiceRequested = () =>
        {
            if (phase == Phase.Recording) { Released(); return; }
            if (phase == Phase.Arming) { Cancel(); return; }
            lock (connectedGate) if (connectedDecision is not null) return;
            if (phase != Phase.Idle || editorSession.Busy || !config.ConnectedAsk) return;
            RequestVoicePressed(VoiceMode.AskAi);
        };
        desktop.ConnectedReadRequested = ReadConnectedAnswer;
    }

    // Called on the desktop thread. Retire the gateway only after its active request has stopped.
    private void ResetConnected(bool hide = true)
    {
        ConnectedConversation? retired;
        lock (connectedGate)
        {
            connectedRevision++; connectedDisplay = null; connectedReview = null;
            connectedCancellation?.Cancel();
            connectedDecision?.TrySetResult(false); connectedDecision = null; connectedChangeId = null;
            retired = connectedConversation; connectedConversation = null; connectedAnswer = "";
        }
        if (connectedThread.Length > 0 && conversations.Get(connectedThread) is { } previous) conversations.Retire(previous.Scope);
        connectedThread = "";
        StopAnswerSpeech();
        if (hide) desktop.HideConnected();
        if (retired is not null)
        {
            var stopping = work;
            preloads.Add(Task.Run(async () => { try { await stopping.ConfigureAwait(false); } catch (Exception) { } await retired.DisposeAsync(); }));
        }
    }

    private async Task CompleteConnectedCancellationAsync(Task stopping,long revision)
    {
        try{await stopping.ConfigureAwait(false);}catch(Exception){}
        desktop.Post(()=>{if(revision==connectedRevision&&!lifetime.IsCancellationRequested)desktop.FinishConnectedCancellation();});
    }

    private void StartConnectedQuestion(string request)
    {
        if (!config.ConnectedAsk || phase != Phase.Idle || editorSession.Busy || authBusy) return;
        lock (connectedGate) if (connectedDecision is not null) return;
        StopAnswerSpeech();
        operation?.Dispose(); operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation.CancelAfter(TimeSpan.FromMinutes(15));
        var ct = operation.Token; var id = ++generation; var settings = config; var stopping = speechWork;
        phase = Phase.Processing; desktop.SetActive(true);
        work = Task.Run(async () =>
        {
            try { await stopping.ConfigureAwait(false); ct.ThrowIfCancellationRequested(); await RunConnectedAsync(request, settings, ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { desktop.Post(() => { if (generation == id) desktop.SetStatus(ex.Message, isError: true); }); }
            finally { desktop.Post(() => { if (generation == id) { phase = Phase.Idle; desktop.SetActive(false); RestoreSpeechInput(); PublishWorkspace(); } }); }
        });
    }

    private async Task RunConnectedAsync(string request, AppConfig settings, CancellationToken ct)
    {
        CancellationTokenSource owner; long revision; ConnectedConversation? conversation;
        lock (connectedGate)
        {
            owner = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            connectedCancellation = owner; revision = connectedRevision; conversation = connectedConversation; connectedAnswer = "";
        }
        var token = owner.Token;
        var scope = ConnectedScope(settings);
        var before = connectedThread.Length > 0 ? conversations.Get(connectedThread) : null;
        if (before is null || before.Scope != scope || before.Retired) before = conversations.Active(scope);
        connectedThread = before.Id; var lease = conversations.Begin(before.Id, request); ConnectedView? lastView = null;
        void Display(ConnectedView view)
        {
            lastView = view; desktop.Post(() =>
        {
            if (revision != connectedRevision || lifetime.IsCancellationRequested) return;
            connectedAnswer = view.Answer.Length==0?"":view.Outcome+". "+view.Answer; desktop.ShowConnected(view);
        });
        }
        connectedDisplay = Display;
        async Task<bool> Review(ConnectedChange change, CancellationToken reviewToken)
        {
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (connectedGate)
            {
                reviewToken.ThrowIfCancellationRequested();
                if (revision != connectedRevision) return false;
                connectedChangeId = change.Id; connectedDecision = decision;
            }
            try { return await decision.Task.WaitAsync(reviewToken); }
            finally { lock (connectedGate) if (ReferenceEquals(connectedDecision, decision)) { connectedDecision = null; connectedChangeId = null; } }
        }
        connectedReview = Review;
        try
        {
            var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            desktop.Post(() =>
            {
                if (revision != connectedRevision || token.IsCancellationRequested) { shown.TrySetCanceled(token); return; }
                desktop.ShowConnected(new("Connecting · Connected Ask AI", "Opening your selected Windows Codex profile. No editor selection or document is included.")); shown.TrySetResult();
            });
            await shown.Task.WaitAsync(token);
            if (!auth.IsSignedIn) throw new InvalidOperationException("Connected Ask requires ChatGPT account sign-in. Open Connected Ask settings, then ChatGPT Sign In.");
            if (conversation is null)
            {
                var rpc = CodexRpc.Start(settings.ConnectedCodexPath);
                CodexGateway gateway;
                try { gateway = await CodexGateway.OpenAsync(rpc, token); }
                catch { await rpc.DisposeAsync(); throw; }
                var plannerSettings = settings with { Backend = "chatgpt_oauth", RefinementModel = settings.Backend == "chatgpt_oauth" ? settings.RefinementModel : "auto" };
                conversation = new(gateway, settings.ConnectedTools,
                    (prompt, cancellation) => refiner.RefineAsync("Connected request", "", plannerSettings, _ => { }, cancellation, askAi: true, forceCleanup: true, connectedPrompt: prompt), view => connectedDisplay?.Invoke(view), (change, ct) => connectedReview?.Invoke(change, ct) ?? Task.FromResult(false), ConversationStore.Context(before));
                lock (connectedGate)
                {
                    if (!token.IsCancellationRequested && revision == connectedRevision) connectedConversation = conversation;
                }
                if (token.IsCancellationRequested || revision != connectedRevision) { await conversation.DisposeAsync(); conversation = null; token.ThrowIfCancellationRequested(); throw new OperationCanceledException(); }
            }
            var answerText = await conversation.AskAsync(request, token);
            token.ThrowIfCancellationRequested();
            conversations.Complete(lease, answerText, lastView?.Status ?? "Answered", recordingModel);
            desktop.Post(() => { if (revision == connectedRevision) { desktop.SetPreview(answerText, retain: false); desktop.SetStatus("Connected Answer · Review the SharpVoice Window"); } });
        }
        catch (Exception ex)
        {
            if (lastView is { } outcome && (outcome.Status.Contains("Unknown", StringComparison.OrdinalIgnoreCase) || outcome.Outcome.Contains("Unknown", StringComparison.OrdinalIgnoreCase))) conversations.Complete(lease, outcome.Outcome + "\n" + outcome.Answer, "Outcome Unknown", recordingModel);
            // The conversation itself retains the actual receipt/unknown-outcome status.
            if (conversation is null) Display(new("Request Failed · No Actions Executed", ex is OperationCanceledException ? "Connection canceled." : ex.Message));
            ConnectedConversation? failed = null;
            lock (connectedGate) if (ReferenceEquals(connectedConversation, conversation)) { failed = connectedConversation; connectedConversation = null; }
            if (failed is not null) await failed.DisposeAsync();
            throw;
        }
        finally
        {
            lock (connectedGate) if (ReferenceEquals(connectedCancellation, owner)) connectedCancellation = null;
            conversations.Cancel(lease); owner.Dispose();
        }
    }

    private void ReadConnectedAnswer()
    {
        if (phase != Phase.Idle || editorSession.Busy || pendingVoiceStart || string.IsNullOrWhiteSpace(connectedAnswer)) return;
        if (!speechWork.IsCompleted) { StopAnswerSpeech(); desktop.SetConnectedStatus("Readout Stopped · Answer Retained"); return; }
        StopAnswerSpeech(); var previous = speechWork; var text = connectedAnswer; var revision = connectedRevision; var id = ++speechGeneration;
        var owner = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); owner.CancelAfter(TimeSpan.FromMinutes(15)); speechCancellation = owner;
        speechInput.Suspend(); desktop.StopInputPreview(); recorder.Configure(config.NoiseGateDb, true);
        speechWork = Task.Run(async () =>
        {
            try
            {
                try { await previous.ConfigureAwait(false); } catch (Exception) { }
                owner.Token.ThrowIfCancellationRequested();
                await answerSpeech.SpeakAsync(text, config.AnswerVoice, message => desktop.Post(() => { if (speechGeneration == id && connectedRevision == revision) desktop.SetConnectedStatus(message); }), owner.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { desktop.Post(() => { if (connectedRevision == revision) desktop.SetConnectedStatus("Readout Unavailable · Answer Retained"); }); }
            finally { desktop.Post(() => { if (ReferenceEquals(speechCancellation, owner)) { speechCancellation = null; if (!lifetime.IsCancellationRequested) RestoreSpeechInput(); } owner.Dispose(); }); }
        });
    }
}
