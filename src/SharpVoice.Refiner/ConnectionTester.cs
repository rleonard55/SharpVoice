using System.Diagnostics;
using SharpVoice.Core;
using SharpVoice.Refiner.Auth;

namespace SharpVoice.Refiner;

/// <summary>A fixed public probe; independent model events and no dictation state.</summary>
public sealed class ConnectionTester(HttpClient http, IChatGptSessionProvider auth, GeminiApiClient? geminiApi = null)
{
    public async Task<ConnectionTestResult> TestAsync(string backend, string model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (backend == "none") throw new InvalidOperationException("Choose an AI provider before testing a connection.");
        var settings = new AppConfig { Backend = backend, RefinementModel = model, MinimumAiWords = 0 };
        settings.Validate();
        var probe = new UnifiedRefiner(http, auth, geminiApi: geminiApi);
        var actualModel = "";
        probe.ModelResolved += actual => actualModel = actual;
        var watch = Stopwatch.StartNew();
        var answer = await probe.RefineAsync("Reply with exactly SHARPVOICE_OK", "", settings, _ => { }, ct,
            askAi: true, allowModelDownload: false);
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(answer)) throw new InvalidDataException("The provider returned no test response.");
        return new(actualModel.Length != 0 ? actualModel : model == "auto" ? "Auto (Model Not Reported)" : model, watch.Elapsed);
    }
}
