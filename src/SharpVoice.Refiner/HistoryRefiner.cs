using SharpVoice.Core;
using SharpVoice.Refiner.Auth;

namespace SharpVoice.Refiner;

public sealed record HistoryRetryResult(string Text, string Model);

/// <summary>Explicit retry of retained text/context; intentionally has no target or insertion dependency.</summary>
public sealed class HistoryRefiner(HttpClient http, IChatGptSessionProvider auth)
{
    public async Task<HistoryRetryResult> RetryAsync(DictationEntry entry, string backend, string model, CancellationToken ct)
    {
        DictationHistory.Validate(entry);
        var config = entry.Settings with { Backend = backend, RefinementModel = model };
        config.Validate();
        var refiner = new UnifiedRefiner(http, auth);
        var actualModel = backend == "none" ? "" : model;
        refiner.ModelResolved += actual => actualModel = actual;
        var text = await refiner.RefineAsync(entry.PreparedText, entry.AppName, config, _ => { }, ct, entry.SourceText, entry.AskAi, allowModelDownload: false, forceCleanup: true);
        ct.ThrowIfCancellationRequested();
        return new(text, actualModel);
    }
}
