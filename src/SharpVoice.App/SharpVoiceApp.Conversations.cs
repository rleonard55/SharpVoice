using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Refiner;

namespace SharpVoice.App;

internal sealed partial class SharpVoiceApp
{
    private readonly ConversationStore conversations = new();
    private long conversationIdentity;
    private ConversationScope GeneralScope(AppConfig settings) => new("general", "", settings.Backend + ":" + conversationIdentity);
    private void InitializeConversations()
    {
        conversations.Changed += () => desktop.Post(PublishWorkspace);
        desktop.ReadConversations = () => conversations.List(GeneralScope(answer?.Settings ?? config));
        desktop.NewConversationRequested = () => ChangeNativeConversation(() => OpenNativeConversation(conversations.New(GeneralScope(config))));
        desktop.ConversationSelected = id => ChangeNativeConversation(() =>
        {
            var scope = GeneralScope(config);
            if (conversations.Select(id, scope) && conversations.Get(id) is { } view) OpenNativeConversation(view);
        });
        desktop.DeleteConversationRequested = id => ChangeNativeConversation(() => { conversations.Delete(id); OpenNativeConversation(conversations.Active(GeneralScope(config))); });
        desktop.ClearConversationsRequested = () => ChangeNativeConversation(() => { ResetConnected(); conversations.Clear(); OpenNativeConversation(conversations.Active(GeneralScope(config))); });
    }
    private void ChangeNativeConversation(Action change)
    {
        if (editorSession.Busy) { desktop.SetAnswerStatus("Finish or cancel the editor request first"); return; }
        StopAnswerSpeech();
        if (phase == Phase.Idle) { change(); return; }
        Cancel(); var stopping = work; var id = generation;
        preloads.Add(Task.Run(async () =>
        {
            try { await stopping.ConfigureAwait(false); } catch (Exception) { }
            desktop.Post(() => { if (generation == id && phase == Phase.Idle && !lifetime.IsCancellationRequested) change(); });
        }));
    }
    private void OpenNativeConversation(ConversationView view)
    {
        desktop.HideAnswer();
        var last = view.Turns.LastOrDefault(x => x.Response.Length > 0);
        answer = new(last?.Response ?? "", null, config, null, Guid.Empty, null, view.Id);
        desktop.ShowAnswer(last?.Response ?? "Start speaking or type a request to begin this conversation.", last?.Model ?? "");
        desktop.SetAnswerConversation(view);
        desktop.SetAnswerStatus(last is null ? "New conversation · Kept only until SharpVoice exits" : "Conversation restored · Reply by voice or text");
        desktop.SetAnswerInsertionAvailable(false);
    }
    private void RetireConversations()
    {
        foreach (var scope in new[] { GeneralScope(config), GeneralScope(recordingConfig) }.Distinct()) conversations.Retire(scope);
        conversationIdentity++;
        if (answer is not null) desktop.HideAnswer();
    }
    private ConversationLease BeginAskTurn(AppConfig settings, string request, out ConversationView before)
    {
        before = conversations.Active(GeneralScope(settings));
        return conversations.Begin(before.Id, request);
    }
    private bool CompleteAskTurn(ConversationLease lease, string text, string status) => conversations.Complete(lease, text, status, recordingModel);
}
