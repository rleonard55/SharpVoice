namespace SharpVoice.Core;

public sealed record DictationEntry(Guid Id, DateTimeOffset CreatedAt, string AppName, string Transcript, string PreparedText, string Result, string? SourceText, bool AskAi, AppConfig Settings, string Model, string Status, string Error);
public sealed record HistoryRetryRequest(Guid EntryId, string Backend, string Model);

/// <summary>Text-only bounded journal. Stable identities prevent late retries resurrecting deleted entries.</summary>
public sealed class DictationHistory
{
    public const int Capacity = 50;
    private readonly object gate = new();
    private readonly List<DictationEntry> entries = [];
    public DictationEntry[] Snapshot() { lock (gate) return entries.ToArray(); }
    public DictationEntry? Find(Guid id) { lock (gate) return entries.FirstOrDefault(e => e.Id == id); }
    public void Add(DictationEntry entry)
    {
        Validate(entry);
        lock (gate)
        {
            entries.RemoveAll(e => e.Id == entry.Id);
            entries.Insert(0, entry);
            if (entries.Count > Capacity) entries.RemoveRange(Capacity, entries.Count - Capacity);
        }
    }
    public bool Update(Guid id, string result, string model, string status, string error)
    {
        lock (gate)
        {
            var index = entries.FindIndex(e => e.Id == id);
            if (index < 0) return false;
            var changed = entries[index] with { Result = result, Model = model, Status = status, Error = error };
            Validate(changed); entries[index] = changed; return true;
        }
    }
    public void Restore(IEnumerable<DictationEntry> saved)
    {
        var snapshot = saved.Take(Capacity).ToArray();
        foreach (var entry in snapshot) Validate(entry);
        if (snapshot.Select(e => e.Id).Distinct().Count() != snapshot.Length) throw new ArgumentException("Saved history contains duplicate entries.");
        lock (gate) { entries.Clear(); entries.AddRange(snapshot); }
    }
    public void Remove(Guid id) { lock (gate) entries.RemoveAll(e => e.Id == id); }
    public void Clear() { lock (gate) entries.Clear(); }
    public static void Validate(DictationEntry entry)
    {
        if (entry is null || entry.Id == Guid.Empty || entry.AppName is null || entry.AppName.Length > 256 || entry.Transcript is null || entry.PreparedText is null || entry.Result is null || entry.Error is null || entry.Error.Length > 4000 || entry.Model is null || entry.Model.Length > 512 || entry.Status is null || entry.Status.Length > 100 || entry.Transcript.Length > WritingTools.MaximumText || entry.PreparedText.Length > WritingTools.MaximumText || entry.Result.Length > WritingTools.MaximumText || entry.SourceText?.Length > 32768 || entry.Settings is null) throw new ArgumentException("Dictation history entry is invalid or too large.");
        entry.Settings.Validate();
    }
}
