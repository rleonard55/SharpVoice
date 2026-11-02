using System.Net;
using System.Text;
using System.Text.Json;
using SharpVoice.Core;
using SharpVoice.Refiner;
using SharpVoice.Refiner.Auth;

static class ModelEditingChecks
{
    public static Task Catalog()
    {
        using var document = JsonDocument.Parse("""
            {"models":[
              {"slug":"gpt-6-astra","display_name":"GPT-6 Astra","visibility":"list","priority":0},
              {"slug":"gpt-6-luna","display_name":"GPT-6 Luna","description":"Efficient transformations","visibility":"list","priority":99},
              {"slug":"gpt-5.6-luna","visibility":"list","priority":1},
              {"slug":"hidden","visibility":"hide","priority":0},
              {"slug":"disabled","visibility":"list","supported_in_api":false},
              {"slug":"unknown","display_name":7,"description":false,"visibility":"list","priority":"bad"},
              {"slug":"bad-visibility","visibility":7},null]}
            """);
        var models = ModelCatalog.Parse(document.RootElement);
        Check(models.Count == 4, "Catalog must filter hidden, unsupported and malformed models.");
        Check(ModelCatalog.SelectEconomical(models) == "gpt-6-luna", "Known economical model must beat catalog priority.");
        var luna = models.Single(x => x.Id == "gpt-6-luna");
        Check(luna.Label == "GPT-6 Luna (gpt-6-luna)" && luna.Recommended && luna.Description.Contains("Efficient transformations"), "Preserve provider names and recommendation.");
        Check(models.Single(x => x.Id == "unknown").Description.Contains("unknown", StringComparison.OrdinalIgnoreCase), "Do not claim unknown pricing.");
        using var unknown = JsonDocument.Parse("""{"models":[{"slug":"second","visibility":"list","priority":2},{"slug":"first","visibility":"list","priority":1}]}""");
        Check(ModelCatalog.SelectEconomical(ModelCatalog.Parse(unknown.RootElement)) == "first", "Unknown models fall back to catalog priority.");
        return Task.CompletedTask;
    }

    public static async Task EditingAndResolvedModel()
    {
        var catalogs = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                catalogs++;
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"models":[{"slug":"gpt-6-luna","visibility":"list"}]}""") };
            }
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(payload.RootElement.GetProperty("model").GetString() == "gpt-6-luna", "Actual request uses resolved model.");
            var instructions = payload.RootElement.GetProperty("instructions").GetString()!;
            Check(!instructions.Contains(DefaultPrompts.MasterPrompt, StringComparison.Ordinal), "Editing must not include the dictation instruction prohibition.");
            var input = payload.RootElement.GetProperty("input")[0].GetProperty("content").GetString()!;
            using var edit = JsonDocument.Parse(input);
            Check(edit.RootElement.GetProperty("source_text").GetString() == "Original paragraph.", "Source is separate data.");
            Check(edit.RootElement.GetProperty("editing_instructions").GetString() == "make it shorter", "Speech supplies editing instructions.");
            return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.output_text.delta\",\"delta\":\"Short paragraph.\"}\n\ndata: {\"type\":\"response.completed\",\"response\":{\"model\":\"gpt-6-luna-resolved\"}}\n\n", Encoding.UTF8, "text/event-stream") };
        }));
        var sessions = new Sessions();
        var refiner = new UnifiedRefiner(http, sessions);
        var resolved = new List<string>();
        refiner.ModelResolved += resolved.Add;
        Check((await refiner.GetModelsAsync(default)).Count == 1, "Public model list loads.");
        var config = new AppConfig { Backend = "chatgpt_oauth" };
        Check(await refiner.RefineAsync("make it shorter", "notepad", config, _ => { }, default, "Original paragraph.") == "Short paragraph.", "Returns edited source.");
        Check(resolved.SequenceEqual(new[] { "gpt-6-luna", "gpt-6-luna-resolved" }) && config.RefinementModel == "auto" && catalogs == 1, "Report actual model, preserve auto and share cached discovery.");
        sessions.Account = "another-account";
        await refiner.GetModelsAsync(default);
        Check(catalogs == 2, "Catalog cache is account bound.");
    }

    public static async Task Guards()
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("Guard made a network request.")));
        var refiner = new UnifiedRefiner(http, new RejectSessions());
        Check(await refiner.RefineAsync("  ", "notepad", new AppConfig(), _ => { }, default, "selected") == "", "Empty speech performs no editing or authentication.");
        var oversizedRejected = false;
        try { await refiner.RefineAsync("shorten", "notepad", new AppConfig(), _ => { }, default, new string('x', 32_769)); }
        catch (InvalidOperationException) { oversizedRejected = true; }
        Check(oversizedRejected, "Oversized source was accepted.");
        try
        {
            await refiner.RefineAsync("shorten", "notepad", new AppConfig { Backend = "none" }, _ => { }, default, "selected");
        }
        catch (InvalidOperationException) { return; }
        throw new Exception("Transcription-only backend cannot apply selected-text edits.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
    private sealed class Sessions : IChatGptSessionProvider
    {
        public string Account = "test-account";
        public Task<ChatGptSession> GetSessionAsync(CancellationToken ct) => Task.FromResult(new ChatGptSession("token", "refresh", Account, DateTimeOffset.UtcNow.AddHours(1)));
    }
    private sealed class RejectSessions : IChatGptSessionProvider
    {
        public Task<ChatGptSession> GetSessionAsync(CancellationToken ct) => throw new Exception("Guard accessed authentication.");
    }
}
