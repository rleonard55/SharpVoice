using System.Net;
using System.Text;
using System.Text.Json;
using SharpVoice.Core;
using SharpVoice.Refiner;

internal static class CleanupReasoningChecks
{
    public static async Task Routing()
    {
        foreach (var model in new[] { "auto", "gpt-6-luna", "gpt-6-sol", "gpt-6-astra", "gpt-6.1-sol", "custom-model", "gpt-6-luna-custom" })
        foreach (var route in new[] { "cleanup", "ask", "selection", "code", "editor", "smart", "connected", "conversation" })
        {
            var shouldUseLow = route == "cleanup" && model is "auto" or "gpt-6-luna" or "gpt-6-sol" or "gpt-6-astra" or "gpt-6.1-sol";
            using var http = new HttpClient(new StubHandler(request =>
            {
                if (request.Method == HttpMethod.Get)
                    return new(HttpStatusCode.OK) { Content = new StringContent("{\"models\":[{\"slug\":\"gpt-6-luna\",\"visibility\":\"list\",\"supported_in_api\":true}]}") };
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                var hasReasoning = body.RootElement.TryGetProperty("reasoning", out var reasoning);
                if (hasReasoning != shouldUseLow) throw new Exception($"Unexpected reasoning setting for {model}/{route}: {hasReasoning}.");
                if (hasReasoning && reasoning.GetProperty("effort").GetString() != "low") throw new Exception("Cleanup must request low effort.");
                if (body.RootElement.GetProperty("store").GetBoolean()) throw new Exception("Request storage changed.");
                return new(HttpStatusCode.OK) { Content = new StringContent("data: {\"type\":\"response.output_text.delta\",\"delta\":\"Clean text.\"}\n\ndata: {\"type\":\"response.completed\"}\n\n", Encoding.UTF8, "text/event-stream") };
            }));
            var config = new AppConfig { Backend = "chatgpt_oauth", RefinementModel = model, MinimumAiWords = 0, Mode = route == "code" ? "code" : "prose" };
            var result = await new UnifiedRefiner(http, new FixtureSessionProvider()).RefineAsync("please clean up this dictated sentence", "notepad", config, _ => { }, default,
                sourceText: route == "selection" ? "Original text" : null,
                askAi: route is "ask" or "smart" or "connected" or "conversation", smartAsk: route == "smart",
                editorRequest: route == "editor" ? new("dictate", "Add a constant", new("file:///test.cs", 1, "csharp", "test.cs", "", 0, 0, "", "", "    ", "\n")) : null,
                connectedPrompt: route == "connected" ? new("Connected instructions", "Fixed request") : null,
                answerConversation: route == "conversation" ? AnswerConversation.Start("Question", "Answer") : null);
            if (result != "Clean text.") throw new Exception("Completed response changed.");
        }
    }
}
