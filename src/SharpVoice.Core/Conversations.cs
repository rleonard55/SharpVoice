namespace SharpVoice.Core;

public sealed record ConversationScope(string Domain, string Binding, string Provider);
public sealed record ConversationTurn(string Id, string Request, string Response, string Status, string Model, bool Omitted = false);
public sealed record ConversationView(string Id, ConversationScope Scope, string Title, ConversationTurn[] Turns, bool Omitted, bool Retired = false);
public sealed record ConversationSummary(string Id, string Title, string Domain, bool Retired);
public sealed record ConversationLease(string ConversationId, string TurnId);

/// <summary>App-lifetime-only text. No persistence or OS ownership objects belong in this store.</summary>
public sealed class ConversationStore
{
    private sealed class Thread(string id, ConversationScope scope)
    {
        public readonly string Id = id; public readonly ConversationScope Scope = scope;
        public readonly List<ConversationTurn> Turns = []; public string? Pending; public bool Omitted, Retired;
        public long Used;
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Thread> threads = new(StringComparer.Ordinal);
    private readonly Dictionary<ConversationScope, string> selected = [];
    private long clock;
    public string HostId { get; } = Guid.NewGuid().ToString("N");
    public event Action? Changed;
    private static string Clip(string value, int max) => value.Length <= max ? value : value[..(char.IsHighSurrogate(value[max - 1]) ? max - 1 : max)] + "\n[Earlier context omitted]";
    private static string Title(Thread t)
    { var title = (t.Turns.FirstOrDefault()?.Request ?? "New Conversation").Replace('\r', ' ').Replace('\n', ' '); return title.Length <= 70 ? title : title[..(char.IsHighSurrogate(title[68]) ? 68 : 69)] + "…"; }
    private static bool Protected(Thread t) => t.Pending is not null || t.Turns.LastOrDefault()?.Status is "Proposed" or "Applying";
    private static ConversationView View(Thread t) => new(t.Id, t.Scope, Title(t), t.Turns.ToArray(), t.Omitted, t.Retired);
    public ConversationView? Get(string id) { lock (gate) return threads.TryGetValue(id, out var t) ? View(t) : null; }
    public string Answer(string conversation, string turn, ConversationScope scope)
    {
        lock (gate)
        {
            if (!threads.TryGetValue(conversation, out var thread) || thread.Scope != scope || thread.Retired) throw new InvalidOperationException("Answer belongs to another context or is no longer available.");
            return thread.Turns.FirstOrDefault(t => t.Id == turn && t.Response.Length > 0)?.Response ?? throw new InvalidOperationException("Answer is no longer available.");
        }
    }
    public ConversationView[] List(ConversationScope scope)
    { lock (gate) return threads.Values.Where(t => t.Scope == scope).OrderByDescending(t => t.Used).Select(View).ToArray(); }
    public ConversationView Active(ConversationScope scope)
    {
        lock (gate) if (selected.TryGetValue(scope, out var id) && threads.TryGetValue(id, out var t) && !t.Retired) return View(t);
        return New(scope);
    }
    public ConversationView New(ConversationScope scope)
    {
        if (scope.Domain is not ("general" or "editor_ask" or "code" or "connected") || scope.Binding.Length > 8192 || scope.Provider.Length > 1024) throw new ArgumentException("Invalid conversation scope.");
        ConversationView view;
        lock (gate)
        {
            while (threads.Count >= 20)
            {
                var old = threads.Values.Where(t => !Protected(t)).MinBy(t => t.Used) ?? throw new InvalidOperationException("Finish an active conversation before starting another.");
                Remove(old.Id);
            }
            var t = new Thread(Guid.NewGuid().ToString("N"), scope) { Used = ++clock };
            threads.Add(t.Id, t); selected[scope] = t.Id; view = View(t);
        }
        Changed?.Invoke(); return view;
    }
    public bool Select(string id, ConversationScope scope)
    {
        lock (gate)
        {
            if (!threads.TryGetValue(id, out var t) || t.Scope != scope || t.Retired) return false;
            selected[scope] = id; t.Used = ++clock;
        }
        Changed?.Invoke(); return true;
    }
    public ConversationLease Begin(string id, string request)
    {
        if (string.IsNullOrWhiteSpace(request) || request.Length > 16000) throw new ArgumentException("Conversation requests must contain 1–16,000 characters.");
        ConversationLease lease;
        lock (gate)
        {
            if (!threads.TryGetValue(id, out var t) || t.Retired || Protected(t)) throw new InvalidOperationException("Conversation unavailable or busy.");
            var turn = new ConversationTurn(Guid.NewGuid().ToString("N"), request, "", "Working", "");
            t.Turns.Add(turn); t.Pending = turn.Id; t.Used = ++clock; lease = new(id, turn.Id);
            try { Trim(t); TrimBudget(t); }
            catch { t.Turns.RemoveAll(x => x.Id == turn.Id); t.Pending = null; throw; }
        }
        Changed?.Invoke(); return lease;
    }
    public bool Complete(ConversationLease lease, string response, string status, string model)
    {
        if (response.Length > 200000 || model.Length > 512 || status.Length > 200) throw new ArgumentException("Excessive conversation result.");
        lock (gate)
        {
            if (!threads.TryGetValue(lease.ConversationId, out var t) || t.Retired || t.Pending != lease.TurnId) return false;
            var i = t.Turns.FindIndex(x => x.Id == lease.TurnId); if (i < 0) return false;
            var original = t.Turns[i];
            t.Turns[i] = original with { Response = response, Status = status, Model = model };
            t.Pending = null; t.Used = ++clock;
            try { Trim(t); TrimBudget(t); }
            catch { var restored = t.Turns.FindIndex(x => x.Id == lease.TurnId); if (restored >= 0) t.Turns[restored] = original; t.Pending = lease.TurnId; throw; }
        }
        Changed?.Invoke(); return true;
    }
    public void Cancel(ConversationLease lease, string status = "Canceled")
    {
        if (Complete(lease, "", status, "")) return;
        var changed = false;
        lock (gate)
        {
            if (threads.TryGetValue(lease.ConversationId, out var t))
            { var i = t.Turns.FindIndex(x => x.Id == lease.TurnId && x.Status == "Proposed"); if (i >= 0) { t.Turns[i] = t.Turns[i] with { Status = status }; changed = true; } }
        }
        if (changed) Changed?.Invoke();
    }
    public bool Mark(string conversationId, string turnId, string status)
    {
        lock (gate)
        {
            if (!threads.TryGetValue(conversationId, out var t)) return false;
            var i = t.Turns.FindIndex(x => x.Id == turnId); if (i < 0) return false;
            t.Turns[i] = t.Turns[i] with { Status = status };
        }
        Changed?.Invoke(); return true;
    }
    public void Delete(string id) { lock (gate) Remove(id); Changed?.Invoke(); }
    public void Clear() { lock (gate) { threads.Clear(); selected.Clear(); } Changed?.Invoke(); }
    public void Retire(ConversationScope scope)
    {
        lock (gate)
        {
            selected.Remove(scope);
            foreach (var t in threads.Values.Where(t => t.Scope == scope))
            {
                t.Retired = true;
                if (t.Pending is { } pending)
                { var i = t.Turns.FindIndex(x => x.Id == pending); if (i >= 0) t.Turns[i] = t.Turns[i] with { Status = "Canceled", Response = "" }; t.Pending = null; }
            }
        }
        Changed?.Invoke();
    }
    private void Remove(string id) { threads.Remove(id); foreach (var key in selected.Where(x => x.Value == id).Select(x => x.Key).ToArray()) selected.Remove(key); }
    private static int Size(Thread t) => t.Turns.Sum(x => x.Request.Length + x.Response.Length);
    private static void Trim(Thread t) { while (t.Turns.Count > 40) { t.Turns.RemoveAt(1); t.Omitted = true; } }
    private void TrimBudget(Thread current)
    {
        while (threads.Values.Sum(Size) > 1_048_576)
        {
            var other = threads.Values.Where(t => t != current && !Protected(t)).MinBy(t => t.Used);
            if (other is not null) { Remove(other.Id); continue; }
            if (current.Turns.Count > 2) { current.Turns.RemoveAt(1); current.Omitted = true; continue; }
            // Two individually bounded turns cannot exceed the global budget.
            throw new InvalidOperationException("Conversation memory is busy. Finish other requests first.");
        }
    }
    public static ConversationView Presentation(ConversationView view)
    {
        if (view.Turns.Sum(t => t.Request.Length + t.Response.Length) <= 64000) return view;
        var turns = view.Turns; var remaining = 64000; var kept = new List<ConversationTurn>();
        if (turns.Length == 0) return view;
        var first = turns[0] with { Request = Clip(turns[0].Request, 4000), Response = Clip(turns[0].Response, turns.Length == 1 ? 58000 : 4000), Omitted = true };
        kept.Add(first); remaining -= first.Request.Length + first.Response.Length;
        var recent = new List<ConversationTurn>();
        for (var i = turns.Length - 1; i > 0 && remaining > 100; i--)
        {
            var request = Clip(turns[i].Request, Math.Min(4000, remaining / 4));
            var response = Clip(turns[i].Response, Math.Max(1, Math.Min(i == turns.Length - 1 ? 40000 : 8000, remaining - request.Length - 50)));
            if (request.Length + response.Length > remaining) break;
            recent.Insert(0, turns[i] with { Request = request, Response = response, Omitted = request != turns[i].Request || response != turns[i].Response });
            remaining -= request.Length + response.Length;
        }
        return view with { Turns = [.. kept, .. recent], Omitted = true };
    }
    public static ConversationTurn[] Context(ConversationView view)
    {
        var turns = view.Turns.Where(x => x.Response.Length > 0 && x.Status is not ("Canceled" or "Failed" or "Working")).ToArray();
        if (turns.Length == 0) return [];
        const int budget = 48000;
        var first = turns[0] with { Request = Clip(turns[0].Request, 4000), Response = Clip(turns[0].Response, 12000), Omitted = turns[0].Request.Length > 4000 || turns[0].Response.Length > 12000 };
        if (turns.Length == 1)
        {
            var response = Clip(turns[0].Response, budget - first.Request.Length - 50);
            return [first with { Response = response, Omitted = response != turns[0].Response || first.Request != turns[0].Request }];
        }
        var remaining = budget - first.Request.Length - first.Response.Length;
        var list = new List<ConversationTurn>();
        for (var i = turns.Length - 1; i >= 1 && remaining > 100; i--)
        {
            var request = Clip(turns[i].Request, Math.Min(4000, remaining / 4));
            var response = Clip(turns[i].Response, Math.Max(1, remaining - request.Length - 50));
            var count = request.Length + response.Length;
            if (count > remaining) break;
            list.Insert(0, turns[i] with { Request = request, Response = response, Omitted = count != turns[i].Request.Length + turns[i].Response.Length }); remaining -= count;
        }
        return [first, .. list];
    }
}
