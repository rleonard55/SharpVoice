using System.Text.Json;
using SharpVoice.Core;
internal static class ConnectedChecks
{
    internal static JsonElement Json(string text) {using var d=JsonDocument.Parse(text);return d.RootElement.Clone();}
    internal static ConnectedTool Send() => new("codex_apps","gmail_send_email","Send mail",Json("{\"type\":\"object\",\"properties\":{\"to\":{\"type\":\"string\"},\"subject\":{\"type\":\"string\"},\"body\":{\"type\":\"string\"},\"bcc\":{\"type\":\"string\"}},\"required\":[\"to\",\"subject\",\"body\"]}"),false,false);
    internal const string Mail="{\"to\":\"alex@example.test\",\"subject\":\"Tuesday\",\"body\":\"Tuesday works.\\nThanks!\",\"bcc\":\"audit@example.test\"}";
    public static async Task LargeCatalog()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!); start.ArgumentList.Add("--connected-large-catalog-fixture");
        await using(var rpc=new SharpVoice.Platform.Windows.CodexRpc(start))
        await using(var gateway=await SharpVoice.Platform.Windows.CodexGateway.OpenAsync(rpc,deadline.Token))
        {
            if(gateway.Tools.Count!=1200)throw new Exception("Large catalog lost metadata.");
            var round=0;
            await using var conversation=new SharpVoice.Refiner.ConnectedConversation(gateway,gateway.Tools.Where(x=>x.Supported).Select(x=>x.Key),(prompt,ct)=>
            {
                if(prompt.Input.Length>160000)throw new Exception("Bulk permissions overflowed the planning budget.");
                var input=Json(prompt.Input);
                if(round++==0)
                {
                    if(!input.GetProperty("tools").EnumerateArray().Any(x=>x.GetProperty("key").GetString()=="__sharpvoice_find_tools"))throw new Exception("Large allowed catalog has no bounded discovery path.");
                    return Task.FromResult("{\"kind\":\"tool\",\"tool\":\"__sharpvoice_find_tools\",\"arguments\":{\"query\":\"read_1199\"},\"text\":\"\"}");
                }
                if(round==2)
                {
                    if(!input.GetProperty("tools").EnumerateArray().Any(x=>x.GetProperty("key").GetString()=="fixture::read_1199"&&x.TryGetProperty("input_schema",out _)))throw new Exception("Tool discovery did not expose the requested complete schema.");
                    return Task.FromResult("{\"kind\":\"tool\",\"tool\":\"fixture::read_1199\",\"arguments\":{},\"text\":\"\"}");
                }
                return Task.FromResult("{\"kind\":\"answer\",\"tool\":\"\",\"arguments\":{},\"text\":\"Fixture read completed.\"}");
            },_=>{},(_,_)=>throw new Exception("Read-only catalog unexpectedly requested change approval."));
            if(await conversation.AskAsync("Read fixture1199",deadline.Token)!="Fixture read completed."||round!=3)throw new Exception("Bulk-enabled connected request did not complete.");
        }
        start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!);start.ArgumentList.Add("--connected-large-result-fixture");
        await using var bounded=new SharpVoice.Platform.Windows.CodexRpc(start);
        try{await bounded.CallAsync("mcpServer/tool/call",Json("{}"),deadline.Token);throw new Exception("Large tool response bypassed its original limit.");}catch(System.IO.InvalidDataException){}
    }
    public static async Task Transport()
    {
        var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!);start.ArgumentList.Add("--connected-fake-rpc");
        await using var rpc=new SharpVoice.Platform.Windows.CodexRpc(start);await using var gateway=await SharpVoice.Platform.Windows.CodexGateway.OpenAsync(rpc,default);
        if(gateway.Tools.Count!=3)throw new Exception("Real pipe transport lost tool metadata.");
        try{await rpc.CallAsync("turn/start",Json("{}"),default);throw new Exception("Autonomous turn escaped the transport whitelist.");}catch(InvalidOperationException){}
        using var stop=new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        try{await rpc.CallAsync("mcpServer/tool/call",Json("{\"arguments\":{\"hang\":true}}"),stop.Token);throw new Exception("Blocked tool RPC ignored cancellation.");}catch(OperationCanceledException){}
    }
    public static async Task Account()
    {
        var rpc=new ConnectedFakeRpc();await using var gateway=await SharpVoice.Platform.Windows.CodexGateway.OpenAsync(rpc,default);
        var reviews=0;
        await using var conversation=new SharpVoice.Refiner.ConnectedConversation(gateway,["codex_apps::gmail_send_email"],(p,ct)=>Task.FromResult("{\"kind\":\"tool\",\"tool\":\"codex_apps::gmail_send_email\",\"arguments\":"+Mail+",\"text\":\"\"}"),_=>{},(change,ct)=>{reviews++;return Task.FromResult(true);});
        try{await conversation.AskAsync("Send email",default);throw new Exception("Unidentified account could send an email.");}catch(InvalidOperationException){}
        if(reviews!=0||rpc.Invocations!=0)throw new Exception("Write was offered without an authoritative sending account.");
    }
    public static async Task Loop()
    {
        var rpc=new ConnectedFakeRpc();await using var gateway=await SharpVoice.Platform.Windows.CodexGateway.OpenAsync(rpc,default);
        var selected=gateway.Tools.Select(x=>x.Key).ToArray();
        var plans=new Queue<string>(new[]{"{\"kind\":\"tool\",\"tool\":\"codex_apps::gmail_search_emails\",\"arguments\":{},\"text\":\"\"}","{\"kind\":\"tool\",\"tool\":\"codex_apps::gmail_send_email\",\"arguments\":"+Mail+",\"text\":\"\"}","{\"kind\":\"answer\",\"tool\":\"\",\"arguments\":{},\"text\":\"The tool completed.\"}"});
        var offered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var decision=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);ConnectedChange? preview=null;
        await using var conversation=new SharpVoice.Refiner.ConnectedConversation(gateway,selected,(p,ct)=>Task.FromResult(plans.Dequeue()),_=>{},(change,ct)=>{preview=change;offered.SetResult();return decision.Task;});
        var running=conversation.AskAsync("Read Alex's latest email and reply Tuesday works",default);
        await offered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if(rpc.Invocations!=2||preview is null||(!preview.After.Contains("Tuesday works")||!preview.After.Contains("owner@example.test")))throw new Exception("Write ran before a complete diff was offered.");
        decision.SetResult(true);var answer=await running;
        if(rpc.Invocations!=4||!answer.Contains("Confirmed by Service"))throw new Exception("Reviewed tool loop did not execute exactly one confirmed write.");
        plans.Enqueue("{\"kind\":\"answer\",\"tool\":\"\",\"arguments\":{},\"text\":\"The previous tool receipt is available.\"}");
        var followup=await conversation.AskAsync("What did the previous receipt say?",default);
        if(followup!="The previous tool receipt is available."||rpc.Invocations!=4)throw new Exception("Follow-up could not use actual prior tool results without repeating the read/write.");
        foreach(var invalid in new[]{"{}","{\"kind\":\"tool\",\"tool\":\"x\",\"arguments\":{},\"text\":\"also answer\"}","```json\n{}\n```"})
            try{ConnectedStep.Parse(invalid);throw new Exception("Malformed connected step accepted.");}catch(Exception e)when(e is System.IO.InvalidDataException or System.Text.Json.JsonException){}
    }
    public static async Task Gateway()
    {
        var rpc=new ConnectedFakeRpc();await using var gateway=await SharpVoice.Platform.Windows.CodexGateway.OpenAsync(rpc,default);
        var read=gateway.Tools.Single(x=>x.Name=="gmail_search_emails");var send=gateway.Tools.Single(x=>!x.CanRead);var allowed=new HashSet<string>{read.Key,send.Key};
        try{await gateway.InvokeAsync(send,Json(Mail),allowed,false,default);throw new Exception("Unconfirmed action ran.");}catch(InvalidOperationException){}
        try{await gateway.InvokeAsync(read,Json("{}"),new HashSet<string>(),false,default);throw new Exception("Unselected tool ran.");}catch(InvalidOperationException){}
        if(rpc.Invocations!=0)throw new Exception("Rejected calls crossed the gateway.");
        await gateway.InvokeAsync(read,Json("{}"),allowed,false,default);
        await gateway.InvokeAsync(send,Json(Mail),allowed,true,default);
        if(rpc.Invocations!=2||rpc.Methods.Any(x=>x is "turn/start" or "config/value/write"))throw new Exception("Gateway ran an autonomous agent or changed user configuration.");
    }
    public static async Task Interruptions()
    {
        foreach(var scenario in new[]{"cancel", "account_changed", "transport_lost", "oversized_receipt"})
        {
            var rpc=new ConnectedFakeRpc();var profiles=0;var writes=0;
            rpc.Result=p=>
            {
                if(p.GetProperty("tool").GetString()=="gmail_get_profile")
                {profiles++;return Json("{\"content\":[],\"structuredContent\":{\"emailAddress\":\""+(scenario=="account_changed"&&profiles>1?"other":"owner")+"@example.test\"}}");}
                writes++;if(scenario=="oversized_receipt")return Json("{\"content\":[],\"structuredContent\":{\""+new string('k',5000)+"\":\"synthetic\"}}");throw new IOException("Synthetic connection lost after accepting the request");
            };
            await using var gateway=await SharpVoice.Platform.Windows.CodexGateway.OpenAsync(rpc,default);
            using var stop=new CancellationTokenSource();var views=new List<ConnectedView>();
            await using var conversation=new SharpVoice.Refiner.ConnectedConversation(gateway,gateway.Tools.Select(x=>x.Key),
                (p,ct)=>Task.FromResult("{\"kind\":\"tool\",\"tool\":\"codex_apps::gmail_send_email\",\"arguments\":"+Mail+",\"text\":\"\"}"),views.Add,
                (change,ct)=>{if(scenario=="cancel")stop.Cancel();return Task.FromResult(true);});
            try{await conversation.AskAsync("Send a message",stop.Token);throw new Exception("Interrupted request reported success.");}
            catch(Exception e)when(e is IOException or InvalidOperationException or OperationCanceledException){}
            if(writes!=(scenario is "transport_lost" or "oversized_receipt"?1:0))throw new Exception("Canceled or changed-account write executed or was retried.");
            if(scenario is "transport_lost" or "oversized_receipt"&&!views.Last().Status.StartsWith("Outcome Unknown"))throw new Exception("Lost write receipt was reported as an ordinary failure.");
        }
    }
    public static Task Diffs()
    {
        var change=ConnectedPolicy.Preview(Send(),Json(Mail));
        if(!change.Before.Contains("Not Sent")||!change.After.Contains("alex@example.test")||!change.After.Contains("audit@example.test")||!change.After.Contains("Tuesday works.\nThanks!")||change.Label!="Send Email")throw new Exception("Creation diff omitted exact recipients/body or actionable label.");
        foreach(var tool in new[]{Send() with{Name="gmail_delete_emails",Destructive=true},Send() with{Name="gmail_update_draft"},Send() with{Name="arbitrary_write"}})
            try{ConnectedPolicy.Preview(tool,Json(Mail));throw new Exception("Unverifiable before-state action received approval preview.");}catch(InvalidOperationException){}
        foreach(var bad in new[]{"{\"to\":\"x\"}","{\"to\":3,\"subject\":\"x\",\"body\":\"x\"}","{\"to\":\"x\",\"subject\":\"x\",\"body\":\"x\",\"hidden\":true}","{\"to\":\"x\\nBcc: hidden\",\"subject\":\"x\",\"body\":\"x\"}","{\"to\":\"x\",\"subject\":\"x\",\"body\":\"\\u202Ehidden\"}"})
            try{ConnectedPolicy.Preview(Send(),Json(bad));throw new Exception("Invalid or hidden action fields accepted.");}catch(InvalidOperationException){}
        return Task.CompletedTask;
    }
    public static Task Hints()
    {
        var contradictory=Send() with{ReadOnly=true};
        if(contradictory.CanRead||contradictory.CreationLabel!="Send Email")throw new Exception("A known write annotation bypassed mandatory review.");
        ConnectedPolicy.Preview(contradictory,Json(Mail));
        return Task.CompletedTask;
    }
    public static Task Preferences()
    {
        var folder=Path.Combine(Path.GetTempPath(),"SharpVoice-Connected-Prefs-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var path=Path.Combine(folder,"config.json");
        try
        {
            var saved=new AppConfig{ConnectedAsk=true,ConnectedCodexPath=@"C:\Synthetic\codex.exe",ConnectedTools=Enumerable.Range(0,100).Select(i=>$"fixture::read_{i}").ToArray()};
            ConfigManager.Save(saved,path);var loaded=ConfigManager.Load(path);
            if(!loaded.ConnectedAsk||loaded.ConnectedCodexPath!=saved.ConnectedCodexPath||!loaded.ConnectedTools.SequenceEqual(saved.ConnectedTools))throw new Exception("Connected preferences were not recalled.");
            var original=Json(File.ReadAllText(path));File.WriteAllText(path,ConnectedJson.Build(w=>{w.WriteStartObject();foreach(var field in original.EnumerateObject())if(!field.Name.StartsWith("connected_",StringComparison.Ordinal)){w.WritePropertyName(field.Name);field.Value.WriteTo(w);}w.WriteEndObject();}));
            loaded=ConfigManager.Load(path);if(loaded.ConnectedAsk||loaded.ConnectedTools.Length!=0||loaded.ConnectedCodexPath.Length!=0)throw new Exception("Older settings silently enabled connected access.");
        }
        finally{Directory.Delete(folder,true);}
        return Task.CompletedTask;
    }
    public static Task BoundedRendering()
    {
        var key=new string('k',5000);
        var value=Json("{\""+key+"\":{\"value\":\"synthetic\"}}");
        try{ConnectedJson.Format(value);throw new Exception("Untrusted prefixes can expand beyond the display bound.");}catch(InvalidOperationException){}
        return Task.CompletedTask;
    }
    public static Task CompleteReview()
    {
        var accepted=new List<string>();
        void Reject(ConnectedTool tool,string args)
        {try{ConnectedPolicy.Preview(tool,Json(args));accepted.Add(tool.Name+": "+args);}catch(InvalidOperationException){}}
        var calendar=Send() with{Name="gcal_create_event",Schema=Json("{\"type\":\"object\",\"properties\":{\"title\":{\"type\":\"string\"},\"start\":{\"type\":\"string\"}}}")};
        Reject(calendar,"{\"title\":\"Meeting\",\"start\":\"2026-11-02T09:00:00\"}");
        var hiddenDefault=Send() with{Schema=Json(Send().Schema.GetRawText().Replace("\"bcc\":{\"type\":\"string\"}","\"bcc\":{\"type\":\"string\",\"default\":\"hidden@example.test\"}"))};
        Reject(hiddenDefault,"{\"to\":\"alex@example.test\",\"subject\":\"x\",\"body\":\"x\"}");
        var hostile=Send() with{Schema=Json("{\"type\":\"object\",\"properties\":{\"to\":{\"type\":\"string\"},\"subject\":{\"type\":\"string\"},\"body\":{\"type\":\"string\"},\"note\\u0000\":{\"type\":\"string\"},\"bcc\":{\"type\":\"string\"}}}")};
        Reject(hostile,"{\"to\":\"alex@example.test\",\"subject\":\"x\",\"body\":\"x\",\"note\\u0000\":\"x\",\"bcc\":\"hidden@example.test\"}");
        var completeCalendar=calendar with{Schema=Json("{\"type\":\"object\",\"properties\":{\"calendar_id\":{\"type\":\"string\"},\"summary\":{\"type\":\"string\"},\"start\":{\"type\":\"object\",\"properties\":{\"dateTime\":{\"type\":\"string\"},\"timeZone\":{\"type\":\"string\"}}},\"end\":{\"type\":\"object\",\"properties\":{\"dateTime\":{\"type\":\"string\"},\"timeZone\":{\"type\":\"string\"}}}}}")};
        var eventDiff=ConnectedPolicy.Preview(completeCalendar,Json("{\"calendar_id\":\"team@example.test\",\"summary\":\"Meeting\",\"start\":{\"dateTime\":\"2026-11-02T09:00:00-06:00\",\"timeZone\":\"America/Chicago\"},\"end\":{\"dateTime\":\"2026-11-02T10:00:00-06:00\",\"timeZone\":\"America/Chicago\"}}"));
        if(!eventDiff.After.Contains("team@example.test")||!eventDiff.After.Contains("America/Chicago")||eventDiff.Label!="Create Event")throw new Exception("Explicit calendar destination/timezone lost in review.");
        if(accepted.Count>0)throw new Exception("Incomplete reviews accepted: "+string.Join("; ",accepted));
        return Task.CompletedTask;
    }
    public static async Task Outcomes()
    {
        foreach(var scenario in new[]{"no_write","declined","failed","unknown_receipt"})
        {
            var rpc=new ConnectedFakeRpc();var writes=0;var views=new List<ConnectedView>();var calls=0;
            rpc.Result=p=>p.GetProperty("tool").GetString()=="gmail_get_profile"?Json("{\"content\":[],\"structuredContent\":{\"emailAddress\":\"owner@example.test\"}}"):
                (++writes>0?Json(scenario=="failed"?"{\"isError\":true,\"content\":[{\"type\":\"text\",\"text\":\"Service rejected message\"}]}":"{\"content\":[]}"):Json("{}"));
            await using var gateway=await SharpVoice.Platform.Windows.CodexGateway.OpenAsync(rpc,default);
            await using var conversation=new SharpVoice.Refiner.ConnectedConversation(gateway,gateway.Tools.Select(x=>x.Key),(p,ct)=>
                Task.FromResult(++calls==1&&scenario!="no_write"?"{\"kind\":\"tool\",\"tool\":\"codex_apps::gmail_send_email\",\"arguments\":"+Mail+",\"text\":\"\"}":"{\"kind\":\"answer\",\"tool\":\"\",\"arguments\":{},\"text\":\"I sent the email.\"}"),views.Add,(c,ct)=>Task.FromResult(scenario!="declined"));
            var result=await conversation.AskAsync("Send a message",default);
            if(result.Contains("I sent the email")||views.Last().Answer.Contains("I sent the email"))throw new Exception("Unverified model success was displayed as an action outcome.");
            if(scenario is "no_write" or "declined" && writes!=0)throw new Exception("Unapproved write executed.");
            if(scenario=="unknown_receipt"&&!views.Last().Status.StartsWith("Outcome Unknown"))throw new Exception("Unverifiable receipt was treated as success.");
        }
    }
    public static Task Approvals()
    {
        var gate=new ConnectedApproval();var change=ConnectedPolicy.Preview(Send(),Json(Mail));gate.Offer(change);
        if(gate.Confirm(Guid.NewGuid(),change.Arguments)||gate.Confirm(change.Id,"{}"))throw new Exception("Stale or altered action approved.");
        if(!gate.Confirm(change.Id,change.Arguments)||gate.Confirm(change.Id,change.Arguments))throw new Exception("Confirmation was not exactly once.");
        gate.Offer(change);gate.Cancel();if(gate.Confirm(change.Id,change.Arguments))throw new Exception("Closed review approved a write.");
        return Task.CompletedTask;
    }
}

internal sealed class ConnectedFakeRpc : SharpVoice.Platform.Windows.IConnectedRpc
{
    public readonly List<string> Methods=[];public int Invocations;public Func<JsonElement,JsonElement>? Result;
    public Task<JsonElement> CallAsync(string method,JsonElement p,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();Methods.Add(method);
        if(method=="initialize")return Task.FromResult(ConnectedChecks.Json("{}"));
        if(method=="thread/start")return Task.FromResult(ConnectedChecks.Json("{\"thread\":{\"id\":\"fixture\"}}"));
        if(method=="mcpServerStatus/list")return Task.FromResult(ConnectedChecks.Json("{\"data\":[{\"name\":\"codex_apps\",\"runtimeStatus\":\"connected\",\"tools\":{\"profile\":{\"name\":\"gmail_get_profile\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}},\"annotations\":{\"readOnlyHint\":true}},\"read\":{\"name\":\"gmail_search_emails\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}},\"annotations\":{\"readOnlyHint\":true}},\"send\":{\"name\":\"gmail_send_email\",\"inputSchema\":"+ConnectedChecks.Send().Schema.GetRawText()+",\"annotations\":{\"readOnlyHint\":false,\"destructiveHint\":false}}}}],\"nextCursor\":null}"));
        if(method=="mcpServer/tool/call"){Invocations++;if(Result is not null)return Task.FromResult(Result(p));if(p.TryGetProperty("tool",out var profile)&&profile.GetString()=="gmail_get_profile")return Task.FromResult(ConnectedChecks.Json("{\"structuredContent\":{\"emailAddress\":\"owner@example.test\"},\"content\":[]} "));return Task.FromResult(ConnectedChecks.Json("{\"content\":[{\"type\":\"text\",\"text\":\"Fixture result\"}],\"structuredContent\":{\"id\":\"fixture-receipt\"},\"isError\":false}"));}
        throw new Exception("Unexpected runtime call: "+method);
    }
    public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
}
