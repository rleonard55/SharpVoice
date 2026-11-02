using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpVoice.Core;

public static class EditorProtocol { public const int Version = 3; }

public sealed record EditorContext(string Uri, int Version, string Language, string Name, string ContextText, int Start, int End, string SourceText, string Diagnostics, string Indent, string Eol, string? TargetKind = null, int? CursorOffset = null);
public sealed record EditorRequest(string Mode, string Instruction, EditorContext Context, ConversationView? History = null);
public sealed record EditorResult(string Kind, string Text = "", string Question = "");
public sealed record EditorFrame(string Type, string Id = "", string? Mode = null, EditorContext? Context = null, string? Instruction = null, EditorResult? Result = null, string? Model = null, string? Message = null, string? Token = null, int Version = EditorProtocol.Version, string? HostId = null, string? ClientId = null, string? Workspace = null, string? ConversationId = null, string? TurnId = null, int? Sequence = null, ConversationView? Conversation = null, ConversationSummary[]? Conversations = null, string? State = null, bool IncludeFile = false, [property: JsonIgnore] string? ClientScope = null);
public sealed record EditorDiscovery(int Version, string Pipe, string Token, int Pid);

public static class EditorValidation
{
    public const int MaxContext = 32_000, MaxFrame = 1_048_576;
    public static void ValidateContext(EditorContext c)
    {
        if (c is null || c.Uri is null || c.Uri.Length > 4096 || !System.Uri.TryCreate(c.Uri, UriKind.Absolute, out var uri) || uri.Scheme is not ("file" or "untitled")
            || c.Version < 1 || c.Language is null || c.Language.Length is < 1 or > 128 || c.Name is null || c.Name.Length > 512
            || c.ContextText is null || c.ContextText.Length > MaxContext || c.SourceText is null || c.SourceText.Length > MaxContext
            || c.Start < 0 || c.End < c.Start || (long)c.End - c.Start != c.SourceText.Length
            || c.TargetKind is not (null or "selection" or "document" or "nearby" or "cursor")
            || c.TargetKind is null && c.CursorOffset is not null
            || c.TargetKind is "document" or "nearby" && (c.CursorOffset is null || c.CursorOffset < 0 || c.CursorOffset > c.SourceText.Length)
            || c.TargetKind == "cursor" && (c.Start != c.End || c.CursorOffset != 0)
            || c.TargetKind == "selection" && (c.Start == c.End || c.CursorOffset is not null)
            || c.Diagnostics is null || c.Diagnostics.Length > 4000 || c.Indent is null || c.Indent.Length > 16 || c.Eol is not ("\n" or "\r\n"))
            throw new ArgumentException("Unsupported or excessive editor context. Select a smaller region in a local document.");
    }
    public static EditorResult ParseResult(string json, string mode, EditorContext context)
    {
        ValidateContext(context);
        try
        {
            if (json.Length > 220_000) throw new InvalidDataException("Editor response is too large.");
            using var envelope = JsonDocument.Parse(json);
            if (envelope.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Editor result must be an object.");
            var properties = envelope.RootElement.EnumerateObject().ToArray();
            if (properties.Length != 3 || properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 3
                || properties.Any(p => p.Name is not ("kind" or "text" or "question") || p.Value.ValueKind != JsonValueKind.String))
                throw new InvalidDataException("Editor result must explicitly include kind, text and question.");
            var result = JsonSerializer.Deserialize(json, EditorJsonContext.Default.EditorResult) ?? throw new InvalidDataException("Empty editor result.");
            if (result.Text is null || result.Question is null || result.Text.Length > 200_000 || result.Question.Length > 2000
                || result.Kind is not ("insert" or "replace" or "clarify" or "unsupported")) throw new InvalidDataException("Malformed editor result.");
            if (result.Kind is "clarify" or "unsupported")
            {
                if (string.IsNullOrWhiteSpace(result.Question) || result.Text.Length != 0) throw new InvalidDataException("Missing clarification or unsupported reason.");
            }
            else if (result.Question.Length != 0 || result.Kind == "insert" && (mode != "dictate" || context.Start != context.End || string.IsNullOrWhiteSpace(result.Text)))
                throw new InvalidDataException("The response attempted an invalid insertion or mixed code and commentary.");
            return result;
        }
        catch (JsonException ex) { throw new InvalidDataException("The model did not return a complete structured code result. Nothing was inserted.", ex); }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EditorFrame))]
[JsonSerializable(typeof(EditorDiscovery))]
[JsonSerializable(typeof(EditorRequest))]
[JsonSerializable(typeof(EditorResult))]
public partial class EditorJsonContext : JsonSerializerContext;
