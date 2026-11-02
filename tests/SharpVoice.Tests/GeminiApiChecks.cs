using System.Net;
using System.Text;
using System.Text.Json;
using SharpVoice.Core;
using SharpVoice.Refiner;
using SharpVoice.Refiner.Auth;
internal static class GeminiApiChecks
{
    public static Task Configuration()
    {
        new AppConfig { Backend="gemini_api" }.Validate();
        WritingTools.Validate(new AppConfig { Profiles=[new AppProfile("Google", "Code.exe", "gemini_api")] });
        return Task.CompletedTask;
    }

    private const string Key="fixture-google-api-key";
    private static HttpResponseMessage Reply(string text="Finished.", string finish="STOP") => new(HttpStatusCode.OK) {Content=new StringContent(ConnectedJson.Build(w=>{
        w.WriteStartObject();w.WriteString("modelVersion","gemini-fixture-version");w.WriteStartArray("candidates");w.WriteStartObject();w.WriteString("finishReason",finish);w.WriteStartObject("content");w.WriteStartArray("parts");w.WriteStartObject();w.WriteBoolean("thought",true);w.WriteString("text","PRIVATE_THOUGHT");w.WriteEndObject();w.WriteStartObject();w.WriteString("text",text);w.WriteEndObject();w.WriteEndArray();w.WriteEndObject();w.WriteEndObject();w.WriteEndArray();w.WriteEndObject();
    }),Encoding.UTF8,"application/json")};
    public static async Task Transport()
    {
        var calls=0;string actual="";var expectedInput="spoken input";
        using var http=new HttpClient(new StubHandler(request=>{
            calls++;
            if(request.RequestUri!.Host!="generativelanguage.googleapis.com"||request.RequestUri.Query.Length!=0||!request.Headers.TryGetValues("x-goog-api-key",out var values)||values.Single()!=Key)throw new Exception("API authentication used the wrong host or exposed the key in the URL.");
            if(!request.RequestUri.AbsolutePath.Contains("gemini-3.1-flash-lite:generateContent"))throw new Exception("Auto did not use the economical API model.");
            var text=request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if(text.Contains(Key))throw new Exception("The API key was included in prompt content.");
            using var body=JsonDocument.Parse(text);
            if(!body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!.Contains(expectedInput))throw new Exception("Gemini lost request/selection context.");
            if(body.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString()!.Length==0)throw new Exception("Gemini lost instructions.");
            return Reply();
        }));
        var api=new GeminiApiClient(http,()=>Key);var refiner=new UnifiedRefiner(http,new RejectSessionProvider(),geminiApi:api);refiner.ModelResolved+=m=>actual=m;
        var config=new AppConfig {Backend="gemini_api",MinimumAiWords=0};
        if(await refiner.RefineAsync(expectedInput,"",config,_=>{},default,askAi:true)!="Finished."||actual!="gemini-fixture-version")throw new Exception("Gemini failed to return only final text and the reported model.");
        expectedInput="selected source";
        await refiner.RefineAsync("edit request","",config,_=>{},default,sourceText:expectedInput);
        expectedInput="SHARPVOICE_OK";
        var test=await new ConnectionTester(http,new RejectSessionProvider(),api).TestAsync("gemini_api","auto",default);
        if(calls!=3||test.Model!="gemini-fixture-version")throw new Exception("Connection test did not use the selected Google API provider.");
    }
    public static async Task Failures()
    {
        foreach(var finish in new[]{"MAX_TOKENS","SAFETY","RECITATION","OTHER"})
        {
            using var http=new HttpClient(new StubHandler(_=>Reply("Partial private output",finish)));var preview=false;
            try{await new GeminiApiClient(http,()=>Key).RefineAsync("input","rules","auto",_=>preview=true,_=>{},default);throw new Exception("Incomplete Gemini output was accepted.");}catch(InvalidDataException){}
            if(preview)throw new Exception("Incomplete Gemini output reached the UI.");
        }
        foreach(var code in new[]{400,401,403,404,429,500,302})
        {
            using var http=new HttpClient(new StubHandler(_=>new((HttpStatusCode)code){Content=new StringContent(Key+" private provider error")}));
            try{await new GeminiApiClient(http,()=>Key).RefineAsync("input","rules","auto",_=>throw new Exception("Error previewed"),_=>{},default);throw new Exception("HTTP error was accepted.");}
            catch(InvalidOperationException ex){if(ex.Message.Contains(Key)||ex.Message.Contains("private provider error"))throw new Exception("Gemini HTTP error leaked its provider response.");}
        }
        using var noHttp=new HttpClient(new StubHandler(_=>throw new Exception("Invalid settings reached the network.")));
        foreach(var model in new[]{"https://invalid.example","gemini-x?key=bad","../gemini-x"})
            try{await new GeminiApiClient(noHttp,()=>Key).RefineAsync("input","rules",model,_=>{},_=>{},default);throw new Exception("Unsafe model was accepted.");}catch(InvalidOperationException){}
        try{await new GeminiApiClient(noHttp,()=>"").RefineAsync("input","rules","auto",_=>{},_=>{},default);throw new Exception("Missing key was accepted.");}catch(InvalidOperationException){}
        using var stop=new CancellationTokenSource();stop.Cancel();
        try{await new GeminiApiClient(noHttp,()=>Key).RefineAsync("input","rules","auto",_=>{},_=>{},stop.Token);throw new Exception("Canceled request was accepted.");}catch(OperationCanceledException){}
        var shortRefiner=new UnifiedRefiner(noHttp,new RejectSessionProvider(),geminiApi:new GeminiApiClient(noHttp,()=>throw new Exception("Skipped cleanup read a credential.")));
        if(await shortRefiner.RefineAsync("Thanks.","",new AppConfig {Backend="gemini_api"},_=>{},default)!="Thanks.")throw new Exception("Short dictation bypass stopped working.");
    }
    public static Task KeyStorage()
    {
        if(!OperatingSystem.IsWindows())return Task.CompletedTask;
        var folder=Path.Combine(Path.GetTempPath(),"SharpVoice-Gemini-"+Guid.NewGuid().ToString("N"));var path=Path.Combine(folder,"key.dpapi");
        try
        {
            var store=new GeminiApiKeyStore(path);if(store.IsConfigured||store.Load()!="")throw new Exception("Fresh store contains a key.");
            store.Save(Key);if(!store.IsConfigured||new GeminiApiKeyStore(path).Load()!=Key||Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(Key))throw new Exception("Google key did not round-trip as Windows-encrypted data.");
            store.Save("fixture-replacement-key");if(store.Load()!="fixture-replacement-key")throw new Exception("Key replacement failed.");
            File.WriteAllText(path,"corrupt fixture");try{store.Load();throw new Exception("Corrupted credential was accepted.");}catch(InvalidOperationException){}
            store.Remove();if(store.IsConfigured)throw new Exception("Key removal failed.");
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
        return Task.CompletedTask;
    }
}
