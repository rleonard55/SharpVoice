using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using SharpVoice.Core;
using SharpVoice.Refiner;
using SharpVoice.Refiner.Auth;

internal static class RefinementThresholdChecks
{
    public static async Task Bypass()
    {
        foreach (var backend in new[] { "chatgpt_oauth", "claude_code", "gemini_antigravity", "embedded" })
        {
            using var http = new HttpClient(new NoHttp());
            var runner = new NoProcess();
            var refiner = new UnifiedRefiner(http, new NoSession(), new ClaudeCodeClient(runner, () => "fixture.exe"),
                new AntigravityClient(runner, () => "fixture.exe", _ => throw new Exception("Bypassed text prepared Google agent")));
            var text = "  Yes, that works for me.\n";
            refiner.ModelResolved += _ => throw new Exception("Skipped cleanup reported AI model use");
            var result = await refiner.RefineAsync(text, "notepad", new AppConfig { Backend = backend }, _ => throw new Exception("Skipped cleanup previewed AI output"), default);
            Check(result == text, "Skipped cleanup must preserve the exact transcript.");
        }
    }
    public static Task PolicyAndPersistence()
    {
        var config = new AppConfig { Backend = "chatgpt_oauth" };
        Check(config.MinimumAiWords == 8, "New config must use eight words.");
        Check(RefinementPolicy.ShouldSkipCleanup("one two three four five six seven", config), "Seven words must skip.");
        Check(!RefinementPolicy.ShouldSkipCleanup("one two three four five six seven eight", config), "Eight words must refine.");
        Check(RefinementPolicy.CountWords("  don't follow-up ... \t42\n café\u00a0𐐀 😀 ") == 5, "Unicode whitespace/letters and punctuation word counting failed.");
        Check(!RefinementPolicy.ShouldSkipCleanup("yes", config with { MinimumAiWords = 0 }), "Zero must always refine.");
        Check(!RefinementPolicy.ShouldSkipCleanup("yes", config, askAi: true), "Ask AI must never skip.");
        Check(!RefinementPolicy.ShouldSkipCleanup("shorter", config, hasSelection: true), "Selected edits must never skip.");
        Check(!RefinementPolicy.ShouldSkipCleanup("yes", config with { Backend = "none" }), "Transcription-only backend must not be mislabeled as a threshold bypass.");
        foreach (var invalid in new[] { -1, 1001 })
        {
            var rejected = false;
            try { (config with { MinimumAiWords = invalid }).Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid threshold accepted.");
        }
        var path = Path.Combine(Path.GetTempPath(), "sharpvoice-threshold-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ConfigManager.Save(config with { MinimumAiWords = 0 }, path);
            Check(ConfigManager.Load(path).MinimumAiWords == 0, "Explicit zero was lost on reload.");
            using var source = JsonDocument.Parse(File.ReadAllText(path));
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartObject();
                foreach (var property in source.RootElement.EnumerateObject()) if (property.Name != "minimum_ai_words") property.WriteTo(writer);
                writer.WriteEndObject();
            }
            File.WriteAllText(path, Encoding.UTF8.GetString(bytes.ToArray()));
            Check(ConfigManager.Load(path).MinimumAiWords == 8, "Legacy files must gain the eight-word default.");
            ConfigManager.Save(config with { MinimumAiWords = 12 }, path);
            Check(ConfigManager.Load(path).MinimumAiWords == 12, "Customized threshold was lost.");
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }

    public static async Task BoundaryAndExemptions()
    {
        var calls = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            calls++;
            return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.output_text.delta\",\"delta\":\"Refined.\"}\n\ndata: {\"type\":\"response.completed\"}\n\n") };
        }));
        var refiner = new UnifiedRefiner(http, new FixtureSessionProvider());
        var config = new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "fixture-model" };
        Check(await refiner.RefineAsync("one two three four five six seven", "notepad", config, _ => { }, default) == "one two three four five six seven" && calls == 0, "Seven-word text reached inference.");
        Check(await refiner.RefineAsync("one two three four five six seven eight", "notepad", config, _ => { }, default) == "Refined." && calls == 1, "Eight-word text did not reach inference.");
        Check(await refiner.RefineAsync("yes", "notepad", config with { MinimumAiWords = 0 }, _ => { }, default) == "Refined." && calls == 2, "Disabling the threshold did not restore inference.");
        Check(await refiner.RefineAsync("why?", "notepad", config with { MinimumAiWords = 1000 }, _ => { }, default, askAi: true) == "Refined." && calls == 3, "Short Ask AI was bypassed.");
        Check(await refiner.RefineAsync("shorter", "notepad", config with { MinimumAiWords = 1000 }, _ => { }, default, sourceText: "Selected source.") == "Refined." && calls == 4, "Short selected edit was bypassed.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var rejected = false;
        try { await refiner.RefineAsync("yes", "notepad", config, _ => { }, cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
        Check(rejected && calls == 4, "Cancelled short dictation was accepted or reached inference.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class NoHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new Exception("Short dictation invoked HTTP");
    }
    private sealed class NoSession : IChatGptSessionProvider
    {
        public Task<ChatGptSession> GetSessionAsync(CancellationToken ct) => throw new Exception("Short dictation invoked authentication");
    }
    private sealed class NoProcess : IClaudeCodeProcessRunner
    {
        public Task<ClaudeCodeProcessResult> RunAsync(ProcessStartInfo start, string? input, CancellationToken ct) => throw new Exception("Short dictation invoked CLI");
    }
}
