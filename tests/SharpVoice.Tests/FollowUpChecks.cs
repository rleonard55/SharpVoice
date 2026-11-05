using SharpVoice.Core;

internal static class FollowUpChecks
{
    public static async Task CanceledDelivery()
    {
        using var stop = new CancellationTokenSource(); Action? queued = null; var accepted = 0;
        var response = new FollowUpAnswer("clarify", "Which style?", ["Compact", "Expanded"]);
        var pending = response.DeliverAsync(action => queued = action, _ => accepted++, stop.Token);
        if (queued is null) throw new Exception("Question delivery was not posted.");
        stop.Cancel(); queued();
        try { await pending; throw new Exception("Canceled question was accepted."); } catch (OperationCanceledException) { }
        if (accepted != 0) throw new Exception("Canceled question became visible or played its cue.");
        var live = response.DeliverAsync(action => action(), _ => accepted++, default); await live;
        if (accepted != 1) throw new Exception("Live question did not arrive exactly once.");
    }
    public static Task Contracts()
    {
        var context = new EditorContext("file:///test.cs", 1, "csharp", "test.cs", "x", 0, 1, "x", "", "    ", "\n");
        var result = EditorValidation.ParseResult("""{"kind":"clarify","text":"","question":"Which style?","choices":["XML docs","Plain comments"]}""", "edit", context);
        Check(result.Choices?.Length == 2, "Structured code choices were lost.");
        foreach (var choices in new[] { "null", "[\"\"]", "[\"a\",\"a\"]", "[\"a\",\"b\",\"c\",\"d\",\"e\"]", "[42]" })
            Reject(() => EditorValidation.ParseResult("{\"kind\":\"clarify\",\"text\":\"\",\"question\":\"Style?\",\"choices\":" + choices + "}", "edit", context));
        var answer = FollowUpAnswer.Parse("""{"kind":"clarify","text":"Which city?","choices":["Chicago","Austin"]}""");
        Check(answer.Kind == "clarify" && answer.Choices.Length == 2, "Structured Ask clarification failed.");
        Reject(() => FollowUpAnswer.Parse("""{"kind":"answer","text":"Done","choices":["Yes"]}"""));
        Reject(() => FollowUpAnswer.Parse("""{"kind":"clarify","text":"?","choices":null}"""));
        var store = new ConversationStore(); var thread = store.New(new("general", "", "test")); var lease = store.Begin(thread.Id, "Find weather");
        store.Complete(lease, answer.Text, "Needs Clarification", "test", answer.Choices);
        Check(store.Get(thread.Id)?.Turns.Last().Choices?.Length == 2, "Conversation lost quick replies.");
        return Task.CompletedTask;
    }
    private static void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { return; } throw new Exception("Malformed follow-up accepted."); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
