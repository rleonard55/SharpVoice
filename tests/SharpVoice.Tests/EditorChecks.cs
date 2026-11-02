using SharpVoice.Core;
using SharpVoice.Refiner;

internal static class EditorChecks
{
    internal static EditorContext Context() => new("file:///C:/fixture.cs", 1, "csharp", "fixture.cs", "class C { }", 0, 0, "", "", "    ", "\r\n");
    public static Task Contracts()
    {
        EditorValidation.ValidateContext(Context());
        var result = EditorValidation.ParseResult("{\"kind\":\"insert\",\"text\":\"  int x;\\n\",\"question\":\"\"}", "dictate", Context());
        if (result.Text != "  int x;\n") throw new Exception("Code whitespace was changed.");
        foreach (var invalid in new[] { "{\"kind\":\"replace\"}", "{\"kind\":\"replace\",\"text\":\"x\"}", "```json\n{}\n```", "{\"kind\":\"insert\",\"text\":\"\"}", "{\"kind\":\"run\",\"text\":\"command\"}", "{\"kind\":\"insert\",\"text\":\"x\",\"path\":\"other.cs\"}" })
        {
            try { EditorValidation.ParseResult(invalid, "edit", Context()); throw new Exception("Accepted invalid result: " + invalid); }
            catch (InvalidDataException) { }
        }
        try { EditorValidation.ValidateContext(Context() with { ContextText = new string('x', 32_001) }); throw new Exception("Accepted excess context."); } catch (ArgumentException) { }
        try { EditorValidation.ValidateContext(Context() with { Uri = "vscode-remote://host/file" }); throw new Exception("Accepted remote source."); } catch (ArgumentException) { }
        var automatic=Context() with {Start=0,End=11,SourceText="class C { }",TargetKind="document",CursorOffset=8};
        EditorValidation.ValidateContext(automatic);
        using(var input=System.Text.Json.JsonDocument.Parse(EditorRefiner.Input(new("dictate","Refactor the main function",automatic))))
        {
            var c=input.RootElement.GetProperty("context");
            if(c.GetProperty("target_kind").GetString()!="document" || c.GetProperty("cursor_offset").GetInt32()!=8 || c.GetProperty("source_text").GetString()!="class C { }")throw new Exception("Automatic source/cursor context did not reach generation.");
            if(c.TryGetProperty("uri",out _))throw new Exception("Automatic context leaked an editor URI.");
        }
        var memory = new ConversationStore(); var thread = memory.Active(new("code", "fixture", "provider"));
        var lease = memory.Begin(thread.Id, "Create a property"); memory.Complete(lease, "int Id;", "Applied", "fixture");
        using (var historyInput = System.Text.Json.JsonDocument.Parse(EditorRefiner.Input(new("edit", "Make it public", automatic, memory.Get(thread.Id)))))
        {
            if (historyInput.RootElement.GetProperty("conversation")[0].GetProperty("response").GetString() != "int Id;"
                || historyInput.RootElement.GetProperty("context").GetProperty("source_text").GetString() != "class C { }") throw new Exception("Code conversation replaced fresh source or omitted prior intent.");
        }
        foreach(var invalid in new[]{automatic with{CursorOffset=12},automatic with{CursorOffset=null},automatic with{TargetKind="selection"},automatic with{TargetKind="other"}})
            try{EditorValidation.ValidateContext(invalid);throw new Exception("Invalid automatic target accepted.");}catch(ArgumentException){}
        if(EditorRefiner.Instructions.Contains("ask the user to select that code and use Edit Code"))throw new Exception("Dictation still refuses context-aware refactoring.");
        return Task.CompletedTask;
    }
    public static async Task Provider()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            calls++;
            using var json = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            if (!json.RootElement.GetProperty("instructions").GetString()!.Contains("untrusted data")) throw new Exception("Source data was not isolated.");
            using var input = System.Text.Json.JsonDocument.Parse(json.RootElement.GetProperty("input")[0].GetProperty("content").GetString()!);
            if (input.RootElement.GetProperty("context").TryGetProperty("uri", out _) || input.RootElement.GetRawText().Contains("file:///C:/fixture.cs")) throw new Exception("Absolute editor URI reached the provider.");
            if (input.RootElement.GetProperty("context").GetProperty("language").GetString() != "csharp" || input.RootElement.GetProperty("instruction").GetString() != "property") throw new Exception("Editor context lost.");
            var output = "{\"kind\":\"insert\",\"text\":\"  public int Id { get; set; }\\n\",\"question\":\"\"}";
            var delta = System.Text.Json.JsonEncodedText.Encode(output).ToString();
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.output_text.delta\",\"delta\":\"" + delta + "\"}\n\ndata: {\"type\":\"response.completed\",\"response\":{\"model\":\"fixture-model\"}}\n\n", System.Text.Encoding.UTF8, "text/event-stream") };
        }));
        var refiner = new UnifiedRefiner(http, new SessionsProvider()); var actual = ""; refiner.ModelResolved += model => actual = model;
        var result = await new EditorRefiner(refiner).GenerateAsync(new("dictate", "property", Context()), new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "fixture-model", MinimumAiWords = 1000 }, default);
        if (calls != 1 || actual != "fixture-model" || result.Text != "  public int Id { get; set; }\n") throw new Exception("Code request was skipped or whitespace/model lost.");
        try { await new EditorRefiner(refiner).GenerateAsync(new("edit", "change", Context()), new AppConfig(), default); throw new Exception("AI-disabled editor generated code."); } catch (InvalidOperationException) { }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request); }
    private sealed class SessionsProvider : SharpVoice.Refiner.Auth.IChatGptSessionProvider
    { public Task<SharpVoice.Refiner.Auth.ChatGptSession> GetSessionAsync(CancellationToken ct) => Task.FromResult(new SharpVoice.Refiner.Auth.ChatGptSession("fixture", "", "", DateTimeOffset.UtcNow.AddHours(1))); }
    public static Task Retarget()
    {
        var session = new EditorSession();var original = Context();
        session.Start("retarget", "dictate", original, DateTimeOffset.UtcNow);
        session.Instruction = "Refactor the main function.";
        session.Processing("retarget");session.Propose("retarget",new("clarify","","Which function?"));
        var selected=original with { Start=0,End=11,SourceText="class C { }" };
        session.BeginClarification("retarget",selected);
        if(session.Context!=selected || session.Instruction!="Refactor the main function." || session.State!="recording")throw new Exception("Follow-up discarded the updated code selection or original intent.");
        session.Processing("retarget");selected=selected with {Start=6,End=7,SourceText="C"};session.RefreshContext("retarget",selected);
        if(session.Context!=selected || session.State!="processing" || session.Rounds!=1)throw new Exception("Pre-generation selection refresh lost state.");
        session.Propose("retarget",new("clarify","","Which condition?"));
        foreach(var invalid in new[]{selected with{Uri="file:///C:/other.cs"},selected with{Version=2},selected with{Language="python"}})
        {
            try{session.BeginClarification("retarget",invalid);throw new Exception("Retargeted a different or changed document.");}catch(ArgumentException){}
            if(session.Context!=selected || session.State!="clarify" || session.Rounds!=1)throw new Exception("Rejected selection changed the pending session.");
        }
        return Task.CompletedTask;
    }
    public static Task Sessions()
    {
        var session = new EditorSession(); var id = Guid.NewGuid().ToString();
        session.Start(id, "dictate", Context(), DateTimeOffset.UtcNow);
        if (session.CanApply(id)) throw new Exception("Recording can apply.");
        try { session.Start("other", "edit", Context(), DateTimeOffset.UtcNow); throw new Exception("Allowed concurrent sessions."); } catch (InvalidOperationException) { }
        session.Processing(id); session.Propose(id, new("insert", "int x;", ""));
        if (!session.CanApply(id) || session.CanApply("stale")) throw new Exception("Proposal identity failed.");
        session.BeginApply(id);
        if (session.State != "applying" || session.CanApply(id)) throw new Exception("Submitted edit remained cancelable as an unapproved proposal.");
        session.Complete(id); if (session.CanApply(id)) throw new Exception("Duplicate apply allowed.");
        session.Start("next", "edit", Context(), DateTimeOffset.UtcNow - TimeSpan.FromMinutes(6));
        if (!session.IsExpired(DateTimeOffset.UtcNow)) throw new Exception("Expired editor session retained.");
        session.Cancel(); if (session.Busy) throw new Exception("Cancel retained session.");
        session.Start("follow", "edit", Context(), DateTimeOffset.UtcNow);
        session.Instruction = "Wrap this code.\nClarification asked: Which condition?";
        session.Processing("follow"); session.Propose("follow", new("clarify", "", "Which condition?"));
        for (var round = 1; round <= 3; round++)
        {
            session.BeginClarification("follow");
            if (session.State != "recording" || session.Rounds != round || session.Context != Context() || !session.Instruction.Contains("Wrap this code.")) throw new Exception("Follow-up lost source/instruction or did not reopen its recording state.");
            session.Processing("follow"); session.Propose("follow", new("clarify", "", "Which condition?"));
        }
        try { session.BeginClarification("follow"); throw new Exception("Unlimited clarification rounds."); } catch (InvalidOperationException) { }
        session.Cancel();
        return Task.CompletedTask;
    }
}
