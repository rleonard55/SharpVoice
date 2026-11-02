using SharpVoice.Core;

internal static class ConversationChecks
{
    public static Task FullAnswer()
    {
        var store = new ConversationStore(); var scope = new ConversationScope("general", "", "fixture"); var view = store.Active(scope);
        var lease = store.Begin(view.Id, "A long answer"); var full = new string('x', 100000) + "THE END"; store.Complete(lease, full, "Answered", "fixture");
        var snapshot = ConversationStore.Presentation(store.Get(view.Id)!);
        if (!snapshot.Omitted || snapshot.Turns.Single().Response == full) throw new Exception("Fixture did not exercise bounded presentation.");
        if (store.Answer(view.Id, lease.TurnId, scope) != full) throw new Exception("Explicit Copy lost the end of a long answer.");
        try { store.Answer(view.Id, lease.TurnId, scope with { Provider="other" }); throw new Exception("Copy crossed a conversation scope."); } catch (InvalidOperationException) { }
        return Task.CompletedTask;
    }
    public static Task GuardedBounds()
    {
        var errors = new List<string>(); var scope = new ConversationScope("general","","fixture");
        var store = new ConversationStore(); var proposal=store.New(scope); var lease=store.Begin(proposal.Id,"Propose code"); store.Complete(lease,"exact pending proposal","Proposed","fixture");
        for(var i=0;i<30;i++)store.New(scope);
        if(store.Get(proposal.Id) is null)errors.Add("Pending approval was evicted as inactive history.");
        var budget=new ConversationStore();
        for(var i=0;i<12;i++)
        {
            var view=budget.New(scope);ConversationLease? turn=null;
            try{turn=budget.Begin(view.Id,new string('q',16000));budget.Complete(turn,new string('r',80000),"Answered","fixture");turn=budget.Begin(view.Id,new string('p',16000));}
            catch(InvalidOperationException){if(turn is not null)budget.Cancel(turn);}
            if(budget.List(scope).Sum(v=>v.Turns.Sum(t=>t.Request.Length+t.Response.Length))>1_048_576){errors.Add("A rejected or active turn exceeded the memory budget.");break;}
        }
        if(errors.Count>0)throw new Exception(string.Join(" ",errors));return Task.CompletedTask;
    }
    public static Task Store()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var store = new ConversationStore(); var scope = new ConversationScope("general", "", "provider-A");
        var first = store.Active(scope); var turn = store.Begin(first.Id, "Give me a recipe");
        Check(store.Complete(turn, "Recipe with beef", "Answered", "fixture"), "Current reply was rejected.");
        Check(store.Active(scope).Id == first.Id && store.Get(first.Id)!.Turns.Single().Response == "Recipe with beef", "A new request lost the recipe context.");
        var next = store.Begin(first.Id, "Make it vegetarian");
        Check(!store.Complete(turn, "Stale overwrite", "Answered", "fixture"), "Old lease overwrote a new turn.");
        store.Cancel(next); Check(!store.Complete(next, "Late reply", "Answered", "fixture"), "Canceled generation committed a reply.");
        Check(ConversationStore.Presentation(store.Get(first.Id)!).Turns.Any(x=>x.Status=="Canceled"), "Canceled turns disappeared from the visible conversation.");
        Check(ConversationStore.Context(store.Get(first.Id)!).Single().Response == "Recipe with beef", "Canceled text entered model context.");
        Check(store.Active(scope with { Provider = "provider-B" }).Id != first.Id, "Provider switch inherited previous account context.");
        Check(store.Active(scope with { Domain = "code", Binding = "workspace/file.cs" }).Id != first.Id, "Code scope leaked into a recipe thread.");
        Check(!store.Select(first.Id, scope with { Provider = "provider-B" }), "Foreign scope selected historical context.");
        Check(new ConversationStore().HostId != store.HostId && new ConversationStore().List(scope).Length == 0, "Restart retained conversation content.");
        var protectedTurn = store.Begin(first.Id, "Active");
        for(var i=0;i<30;i++)store.New(scope);
        Check(store.Get(first.Id) is not null && store.List(scope).Length <=20, "Eviction deleted active work or exceeded thread bound.");
        store.Cancel(protectedTurn); store.Select(first.Id,scope);
        for(var i=0;i<60;i++){var lease=store.Begin(first.Id,"Question "+i);store.Complete(lease,new string('x',30000),"Answered","fixture");}
        var bounded=store.Get(first.Id)!; Check(bounded.Turns.Length<=40 && bounded.Omitted, "History grew without bound or omission indication.");
        var context=ConversationStore.Context(bounded); Check(context.First().Request=="Give me a recipe" && context.Last().Request=="Question 59", "Bounded context lost first or latest exchange.");
        Check(context.Sum(x=>x.Request.Length+x.Response.Length)<=48000,"Model context exceeded budget.");
        store.Delete(first.Id); Check(store.Get(first.Id) is null && !store.Complete(protectedTurn,"Late","Answered","fixture"),"Delete resurrected old work.");
        var fullStore = new ConversationStore(); var fullView=fullStore.Active(scope);var fullTurn=fullStore.Begin(fullView.Id,"Give me a long recipe");var recipe=new string('r',30000);fullStore.Complete(fullTurn,recipe,"Answered","fixture");
        Check(ConversationStore.Context(fullStore.Get(fullView.Id)!).Single().Response==recipe,"A single in-budget recipe was truncated before a follow-up.");
        using(var payload=System.Text.Json.JsonDocument.Parse(SharpVoice.Refiner.AnswerConversation.From(fullStore.Get(fullView.Id)!).Input("Make it vegetarian")))
            Check(payload.RootElement.GetProperty("conversation")[0].GetProperty("assistant").GetString()==recipe,"Prompt adapter truncated an in-budget recipe a second time.");
        store.Clear(); Check(store.List(scope).Length==0,"Clear retained text.");
        return Task.CompletedTask;
    }
}
