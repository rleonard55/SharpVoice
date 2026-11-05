namespace SharpVoice.Core;

/// <summary>Correlates a desktop gesture with a fresh editor context; cancelled starts cannot be revived.</summary>
public sealed class EditorHotkeyGate
{
    public string Id { get; private set; } = "";
    public VoiceMode Mode { get; private set; }
    public bool Pending { get; private set; }
    public bool Active => Id.Length != 0;
    private DateTimeOffset deadline;
    public string Begin(DateTimeOffset now, VoiceMode mode = VoiceMode.Dictation)
    {
        if (Active) throw new InvalidOperationException("An editor hotkey request is already active.");
        Id = Guid.NewGuid().ToString("N"); Mode = mode; Pending = true; deadline = now.AddSeconds(5); return Id;
    }
    public bool Expired(DateTimeOffset now) => Pending && now >= deadline;
    public bool Accept(string id, DateTimeOffset now)
    {
        if (!Pending || id != Id || Expired(now)) return false;
        Pending = false; return true;
    }
    public void Cancel() { Id = ""; Pending = false; }
}
