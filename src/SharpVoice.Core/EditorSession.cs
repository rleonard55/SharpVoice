namespace SharpVoice.Core;

/// <summary>UI-thread editor ownership. No source is persisted and a result is never an apply acknowledgement.</summary>
public sealed class EditorSession
{
    public string Id { get; private set; } = "";
    public string Mode { get; private set; } = "";
    public EditorContext? Context { get; private set; }
    public string State { get; private set; } = "idle";
    public string Instruction { get; set; } = "";
    public int Rounds { get; private set; }
    private DateTimeOffset started;
    public bool Busy => State != "idle";
    public void Start(string id, string mode, EditorContext context, DateTimeOffset now)
    {
        if (Busy) throw new InvalidOperationException("Another editor operation is active. Apply or cancel it first.");
        EditorValidation.ValidateContext(context);
        if (id.Length is < 1 or > 128 || mode is not ("dictate" or "edit")) throw new ArgumentException("Invalid editor request.");
        Id = id; Mode = mode; Context = context; State = "recording"; started = now; Rounds = 0; Instruction = "";
    }
    public void Processing(string id) { Require(id, "recording"); State = "processing"; }
    public void Propose(string id, EditorResult result) { Require(id, "processing"); State = result.Kind == "clarify" ? "clarify" : "proposal"; }
    public void BeginClarification(string id, EditorContext? context = null)
    {
        Require(id, "clarify");
        if (Rounds >= 3) throw new InvalidOperationException("Clarification limit reached. Start a new request.");
        if (context is not null) RefreshContext(id, context);
        Rounds++; State = "recording";
    }
    public void RefreshContext(string id, EditorContext context)
    {
        if (id != Id || State is not ("clarify" or "processing")) throw new InvalidOperationException("This editor context update is stale.");
        EditorValidation.ValidateContext(context);
        var previous = Context!;
        if (context.Uri != previous.Uri || context.Version != previous.Version || context.Language != previous.Language || context.Name != previous.Name)
            throw new ArgumentException("Code targeting must use the unchanged original document.");
        Context = context;
    }
    public void BeginApply(string id) { Require(id, "proposal"); State = "applying"; }
    public bool CanApply(string id) => id == Id && State == "proposal";
    public bool IsExpired(DateTimeOffset now) => Busy && now - started > TimeSpan.FromMinutes(5);
    public void Complete(string id) { if (id != Id || State is not ("proposal" or "applying")) throw new InvalidOperationException("This edit receipt is stale."); Cancel(); }
    public void Cancel() { Id = Mode = Instruction = ""; Context = null; State = "idle"; }
    private void Require(string id, string state) { if (id != Id || State != state) throw new InvalidOperationException("This editor request is stale or in the wrong state."); }
}
