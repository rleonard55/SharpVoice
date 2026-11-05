namespace SharpVoice.Core;

/// <summary>Exact editor commands. Model output cannot invoke these native actions.</summary>
public static class EditorShortcuts
{
    public const string DocumentInstruction = "Add complete, idiomatic documentation for the supplied source_text using the document language's standard documentation syntax (for example C# XML documentation, JSDoc, or Python docstrings). Document the selected code or every declaration in the supplied file as appropriate. Preserve executable behavior, existing meaningful comments, formatting and unrelated code. Return the complete replacement for source_text. If the file format has no valid inline documentation syntax, return unsupported with a short reason. Never invent metadata fields or change the language to simulate documentation.";
    public const string ExplainInstruction = "Explain the supplied current source_text clearly: its purpose, control flow, important inputs/outputs and noteworthy edge cases. Use the supplied language context. Display an explanation only; do not rewrite code or claim any changes were made.";
    public static string? Match(string text) => text.Trim().TrimEnd('.', '!', '?').TrimEnd().ToLowerInvariant() switch
    { "document" => "document", "comment" => "comment", "uncomment" => "uncomment", "explain" => "explain", "format" => "format", _ => null };
    public static EditorResult? Action(string? shortcut, EditorContext context)
    {
        var reason = Rejection(shortcut, context);
        if (reason is not null) return new("unsupported", Question: reason);
        return shortcut == "format" ? new("format") : shortcut is "comment" or "uncomment"
            ? new(context.TargetKind == "selection" && context.End > context.Start ? shortcut : "noop") : null;
    }
    public static string? Rejection(string? shortcut, EditorContext context)
    {
        if (shortcut == "comment" && (context.TargetKind != "selection" || context.Start == context.End)) return null;
        if (shortcut is not ("comment" or "document")) return null;
        var language = context.Language.ToLowerInvariant(); var file = context.Name.ToLowerInvariant();
        var format = language is "jsonl" or "jsonlines" or "ndjson" || file.EndsWith(".jsonl") || file.EndsWith(".ndjson") ? "JSON Lines"
            : language == "json" || language != "jsonc" && file.EndsWith(".json") ? "Strict JSON"
            : language == "csv" || file.EndsWith(".csv") ? "CSV" : language == "tsv" || file.EndsWith(".tsv") ? "TSV" : null;
        if (format is null) return null;
        return shortcut == "comment" ? $"{format} does not permit comments. Nothing changed. Uncomment can remove existing invalid comments."
            : $"{format} has no inline documentation syntax. Use Explain instead; the file was left unchanged.";
    }
}
