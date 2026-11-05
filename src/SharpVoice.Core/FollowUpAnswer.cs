using System.Text.Json;

namespace SharpVoice.Core;

/// <summary>Bounded reply suggestions are text, never commands or permission to act.</summary>
public static class FollowUpChoices
{
    public static void Validate(string[]? choices)
    {
        if (choices is null || choices.Length > 4 || choices.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200)
            || choices.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != choices.Length)
            throw new InvalidDataException("Invalid suggested replies.");
    }
}
public sealed record FollowUpAnswer(string Kind, string Text, string[] Choices)
{
    /// <summary>Accept on the UI thread only while this request is still live.</summary>
    public async Task DeliverAsync(Action<Action> post, Action<FollowUpAnswer> accept, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        post(() =>
        {
            try { ct.ThrowIfCancellationRequested(); accept(this); delivered.TrySetResult(); }
            catch (OperationCanceledException) { delivered.TrySetCanceled(); }
            catch (Exception ex) { delivered.TrySetException(ex); }
        });
        await delivered.Task.WaitAsync(ct).ConfigureAwait(false);
    }
    public static FollowUpAnswer Parse(string json)
    {
        try
        {
            if (json.Length > 220000) throw new InvalidDataException("Answer is too large.");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected a structured answer.");
            var props = doc.RootElement.EnumerateObject().ToArray();
            if (props.Length != 3 || props.Select(p => p.Name).Distinct().Count() != 3 || props.Any(p => p.Name is not ("kind" or "text" or "choices")))
                throw new InvalidDataException("Malformed structured answer.");
            var root = doc.RootElement;
            if (root.GetProperty("kind").ValueKind != JsonValueKind.String || root.GetProperty("text").ValueKind != JsonValueKind.String || root.GetProperty("choices").ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Malformed answer fields.");
            var kind = root.GetProperty("kind").GetString()!; var text = root.GetProperty("text").GetString()!;
            var choices = root.GetProperty("choices").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : throw new InvalidDataException("Invalid reply choice.")).ToArray();
            FollowUpChoices.Validate(choices);
            if (kind is not ("answer" or "clarify") || string.IsNullOrWhiteSpace(text) || text.Length > (kind == "clarify" ? 2000 : 200000) || kind == "answer" && choices.Length != 0)
                throw new InvalidDataException("Invalid answer or clarification.");
            return new(kind, text, choices);
        }
        catch (JsonException ex) { throw new InvalidDataException("The AI did not return a complete structured answer. Try again.", ex); }
    }
}
