namespace SharpVoice.Core;

/// <summary>Connection identity comes from the authenticated server, never a client-claimed scope.</summary>
public sealed class EditorWorkspaceBinding
{
    public string ConversationId { get; set; } = "";
    public ConversationView ResolveConversation(ConversationStore store, ConversationScope scope)
    {
        var view = scope.Domain == "general" ? store.Active(scope) : store.Get(ConversationId);
        if (view is null || view.Scope != scope || view.Retired) view = store.Active(scope);
        ConversationId = view.Id;
        return view;
    }
    public string ClientId { get; set; } = "";
    public string Peer { get; private set; } = "";
    public string Workspace { get; private set; } = "";
    public string Mode { get; set; } = "ask";
    public bool IncludeFile { get; set; }
    public EditorContext? Context { get; set; }
    public bool Bind(string client, string authenticatedPeer, string workspace)
    {
        if (string.IsNullOrEmpty(client) || client.Length > 128 || string.IsNullOrEmpty(authenticatedPeer) || authenticatedPeer.Length > 256 || workspace.Length > 8192) throw new ArgumentException("Invalid authenticated editor binding.");
        var changed = Peer != authenticatedPeer || Workspace != workspace;
        if (changed) { ConversationId = ""; Context = null; IncludeFile = false; Mode = "ask"; }
        var connected = ClientId != client; ClientId = client; Peer = authenticatedPeer; Workspace = workspace;
        return changed || connected;
    }
}
