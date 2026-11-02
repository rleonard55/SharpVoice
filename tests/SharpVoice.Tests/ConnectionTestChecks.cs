using System.Net;
using System.Text;
using System.Text.Json;
using SharpVoice.Core;
using SharpVoice.Refiner;
using SharpVoice.Refiner.Auth;

internal static class ConnectionTestChecks
{
    public static async Task ExplicitModel()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            Check(request.Method == HttpMethod.Post, "Explicit test performed unrelated discovery.");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var root = body.RootElement;
            Check(root.GetProperty("model").GetString() == "draft-model", "Test used applied/default model instead of draft.");
            Check(root.GetProperty("store").GetBoolean() == false, "Test requested stored output.");
            Check(root.GetProperty("input")[0].GetProperty("content").GetString()!.Contains("SHARPVOICE_OK"), "Test did not use fixed public input.");
            return Stream("SHARPVOICE_OK", "actual-response-model");
        }));
        var result = await new ConnectionTester(http, new Session()).TestAsync("chatgpt_oauth", "draft-model", default);
        Check(calls == 1 && result.Model == "actual-response-model" && result.Elapsed >= TimeSpan.Zero, "Successful probe lost model or timing.");
    }
    public static async Task AutoModel()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"models":[{"slug":"available-test-model","visibility":"list","priority":1,"supported_in_api":true}]}""") };
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Check(body.RootElement.GetProperty("model").GetString() == "available-test-model", "Auto did not resolve an available model.");
            return Stream("SHARPVOICE_OK", "available-test-model");
        }));
        var result = await new ConnectionTester(http, new Session()).TestAsync("chatgpt_oauth", "auto", default);
        Check(calls == 2 && result.Model == "available-test-model", "Auto test skipped discovery or inference.");
    }
    public static async Task CompletedReply()
    {
        using var http = new HttpClient(new Handler(_ => Stream("SHARPVOICE_OK.", "test-model")));
        var result = await new ConnectionTester(http, new Session()).TestAsync("chatgpt_oauth", "test-model", default);
        Check(result.Model == "test-model", "A completed model reply was misreported as a connection failure.");
    }
    public static async Task Failures()
    {
        using var http = new HttpClient(new Handler(_ => Stream("", "test-model")));
        await Reject<InvalidDataException>(() => new ConnectionTester(http, new Session()).TestAsync("chatgpt_oauth", "test-model", default));
        await Reject<InvalidOperationException>(() => new ConnectionTester(http, new Session()).TestAsync("none", "auto", default));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Reject<OperationCanceledException>(() => new ConnectionTester(http, new Session()).TestAsync("chatgpt_oauth", "auto", cancel.Token));
        using var denied = new HttpClient(new Handler(_ => new(HttpStatusCode.Unauthorized) { Content = new StringContent("SECRET_PROVIDER_BODY") }));
        try { await new ConnectionTester(denied, new Session()).TestAsync("chatgpt_oauth", "test-model", default); throw new Exception("Unauthorized probe passed."); }
        catch (InvalidOperationException ex) { Check(!ex.Message.Contains("SECRET_PROVIDER_BODY"), "Provider body exposed."); }
    }
    public static async Task LocalNoDownload()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(_ => { requests++; throw new Exception("Local connection probe tried to download weights."); }));
        var path = Path.Combine(Path.GetTempPath(), "sharpvoice-probe-" + Guid.NewGuid().ToString("N") + ".gguf");
        try
        {
            var local = new EmbeddedRefiner(http);
            await Reject<InvalidOperationException>(() => local.EnsureModelAsync(_ => { }, default, false, path));
            await File.WriteAllTextAsync(path, "invalid test weights");
            await Reject<InvalidOperationException>(() => local.EnsureModelAsync(_ => { }, default, false, path));
            Check(requests == 0 && !File.Exists(path + ".download"), "Connection probe downloaded or modified model weights.");
        }
        finally { File.Delete(path); }
    }
    private static HttpResponseMessage Stream(string text, string model) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("data: {\"type\":\"response.output_text.delta\",\"delta\":\"" + text + "\"}\n\ndata: {\"type\":\"response.completed\",\"response\":{\"model\":\"" + model + "\"}}\n\n", Encoding.UTF8, "text/event-stream")
    };
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Reject<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
    private sealed class Session : IChatGptSessionProvider
    {
        public Task<ChatGptSession> GetSessionAsync(CancellationToken ct) => Task.FromResult(new ChatGptSession("fixture-token", "fixture-refresh", "fixture-account", DateTimeOffset.UtcNow.AddHours(1)));
    }
}
