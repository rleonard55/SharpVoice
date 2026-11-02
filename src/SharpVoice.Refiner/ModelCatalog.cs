using System.Text.Json;

namespace SharpVoice.Refiner;

public sealed record ModelChoice(string Id, string Label, string Description, bool Recommended);

/// <summary>Account-visible catalog policy. Catalog priority is a fallback, not a latency estimate.</summary>
public static class ModelCatalog
{
    // Relative ChatGPT credit costs verified 2026-11-01. Only rank exact known IDs;
    // unfamiliar models may have different costs even when their names look similar.
    private static int CostRank(string id) => id switch
    {
        "gpt-6-luna" => 0,
        "gpt-5.6-luna" => 1,
        "gpt-6.1-terra" => 2,
        "gpt-6.1-sol" => 3,
        "gpt-6-astra" => 4,
        _ => int.MaxValue
    };

    public static IReadOnlyList<ModelChoice> Parse(JsonElement root)
    {
        var candidates = new List<(ModelChoice Choice, int Priority)>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return Array.Empty<ModelChoice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in models.EnumerateArray())
        {
            if (model.ValueKind != JsonValueKind.Object || String(model, "visibility") != "list") continue;
            if (model.TryGetProperty("supported_in_api", out var supported) && supported.ValueKind == JsonValueKind.False) continue;
            var id = String(model, "slug");
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
            var name = String(model, "display_name");
            var label = string.IsNullOrWhiteSpace(name) || name == id ? id : $"{name} ({id})";
            var description = String(model, "description");
            var cost = CostRank(id) == int.MaxValue ? "Relative cost and speed unknown." : "Known ChatGPT credit cost; actual speed varies.";
            description = string.IsNullOrWhiteSpace(description) ? cost : description + " " + cost;
            var priority = model.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var value) ? value : int.MaxValue;
            candidates.Add((new(id, label, description, false), priority));
        }
        var ordered = candidates.OrderBy(x => x.Priority).Select(x => x.Choice).ToArray();
        var selected = SelectEconomical(ordered);
        return Array.AsReadOnly(ordered.Select(x => x.Id == selected && CostRank(x.Id) != int.MaxValue
            ? x with { Recommended = true, Description = x.Description + " Recommended: lowest known credit cost among available models." }
            : x).ToArray());
    }

    public static string? SelectEconomical(IReadOnlyList<ModelChoice> models) => models.OrderBy(x => CostRank(x.Id)).FirstOrDefault()?.Id;

    private static string? String(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
