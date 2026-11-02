using System.Net;
using System.Text;
using System.Text.Json;
using SharpVoice.Core;
using SharpVoice.Refiner;
using SharpVoice.Refiner.Auth;

internal static class HistoryRefinerChecks
{
    public static async Task ExplicitRetry()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            requests++;
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            if (payload.RootElement.GetProperty("model").GetString() != "chosen-model") throw new Exception("Retry ignored the chosen model.");
            return Reply("Clean result.");
        }));
        var service = new HistoryRefiner(http, new Sessions());
        var entry = Entry() with { Settings = new AppConfig { Backend = "none", MinimumAiWords = 1000 } };
        var result = await service.RetryAsync(entry, "chatgpt_oauth", "chosen-model", default);
        if (requests != 1 || result.Text != "Clean result." || result.Model != "actual-model") throw new Exception("Explicit retry was skipped by the cleanup threshold or lost model metadata.");
        if (entry.Result != "Original result." || entry.Settings.Backend != "none") throw new Exception("Retry mutated the retained source/configuration.");
    }
    public static async Task SelectedContext()
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            using var input = JsonDocument.Parse(payload.RootElement.GetProperty("input")[0].GetProperty("content").GetString()!);
            if (input.RootElement.GetProperty("source_text").GetString() != "Original selected source." || input.RootElement.GetProperty("editing_instructions").GetString() != "Shorten this.") throw new Exception("History retry lost selected-edit context.");
            return Reply("Short source.");
        }));
        var service = new HistoryRefiner(http, new Sessions());
        var result = await service.RetryAsync(Entry() with { SourceText = "Original selected source." }, "chatgpt_oauth", "chosen-model", default);
        if (result.Text != "Short source.") throw new Exception("Selected retry returned the wrong result.");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await service.RetryAsync(Entry(), "chatgpt_oauth", "chosen-model", canceled.Token); throw new Exception("Canceled retry returned a result."); }
        catch (OperationCanceledException) { }
    }
    private static DictationEntry Entry() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, "notepad", "Shorten this.", "Shorten this.", "Original result.", null, false, new AppConfig(), "", "Inserted", "");
    private static HttpResponseMessage Reply(string text) => new(HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.output_text.delta\",\"delta\":\"" + text + "\"}\n\ndata: {\"type\":\"response.completed\",\"response\":{\"model\":\"actual-model\"}}\n\n", Encoding.UTF8, "text/event-stream") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request); }
    private sealed class Sessions : IChatGptSessionProvider
    { public Task<ChatGptSession> GetSessionAsync(CancellationToken ct) => Task.FromResult(new ChatGptSession("test", "refresh", "", DateTimeOffset.UtcNow.AddHours(1))); }
}
