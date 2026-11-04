using System.Net;
using System.Text;
using System.Text.Json;
using SharpVoice.Core;
using SharpVoice.Refiner.Auth;

namespace SharpVoice.Refiner;

public sealed class GeminiApiClient
{
    public const string DefaultModel = "gemini-3.1-flash-lite";
    // API keys are sent in a header, never a URL, and must not follow redirects to another host.
    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All, ConnectTimeout = TimeSpan.FromSeconds(20) }) { Timeout = TimeSpan.FromMinutes(3) };
    private readonly HttpClient http;
    private readonly Func<string> readKey;
    public GeminiApiClient() : this(SharedHttp, new GeminiApiKeyStore().Load) { }
    public GeminiApiClient(HttpClient http, Func<string> readKey) => (this.http, this.readKey) = (http, readKey);

    public Task<string> RefineAsync(string input, string instructions, string selected, Action<string> preview, Action<string> resolved, CancellationToken ct)
        => ProviderOperation.RunAsync(ProviderOperationKind.GeminiInference, token => RefineCoreAsync(input, instructions, selected, preview, resolved, token), ct);

    private async Task<string> RefineCoreAsync(string input, string instructions, string selected, Action<string> preview, Action<string> resolved, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var model = selected == "auto" ? DefaultModel : selected;
        if (!model.StartsWith("gemini-", StringComparison.Ordinal) || model.Length > 200 || model.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.' and not '_'))
            throw new InvalidOperationException("Choose Auto or an exact Gemini API model ID from Google AI Studio.");
        var saved = readKey();
        if (string.IsNullOrWhiteSpace(saved)) throw new InvalidOperationException("Add your Google AI Studio key in AI & Models → Manage API Key, then test the connection.");
        var key = GeminiApiKeyStore.Validate(saved);
        var body = ConnectedJson.Build(w =>
        {
            w.WriteStartObject();
            w.WriteStartObject("systemInstruction"); WriteParts(w, instructions); w.WriteEndObject();
            w.WriteStartArray("contents"); w.WriteStartObject(); w.WriteString("role", "user"); WriteParts(w, input); w.WriteEndObject(); w.WriteEndArray();
            w.WriteStartObject("generationConfig"); w.WriteNumber("candidateCount", 1); w.WriteNumber("maxOutputTokens", 16384); w.WriteEndObject();
            w.WriteEndObject();
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/models/" + model + ":generateContent");
        request.Headers.Add("x-goog-api-key", key);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new ProviderHttpException(response.StatusCode);
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) != 0)
            {
                if (bytes.Length + count > 2 * 1_048_576) throw new InvalidDataException("Gemini response exceeded the complete response limit. Nothing was inserted.");
                bytes.Write(buffer, 0, count);
            }
            using var json = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            var root = json.RootElement;
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() != 1)
                throw ProviderOperation.RejectedAnswer();
            var candidate = candidates[0];
            var reason = candidate.TryGetProperty("finishReason", out var finish) && finish.ValueKind == JsonValueKind.String ? finish.GetString() : "";
            if (reason != "STOP") throw ProviderOperation.RejectedAnswer(reason == "MAX_TOKENS");
            if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Gemini returned no answer text. Nothing was inserted.");
            var text = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
                if (!part.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Gemini returned unsupported non-text output. Nothing was inserted.");
                text.Append(value.GetString());
            }
            var answer = text.ToString().Trim();
            if (answer.Length == 0) throw new InvalidDataException("Gemini returned an empty answer. Nothing was inserted.");
            ct.ThrowIfCancellationRequested();
            var actual = root.TryGetProperty("modelVersion", out var version) && version.ValueKind == JsonValueKind.String ? version.GetString() : null;
            resolved(actual is { Length: > 0 and <= 200 } && actual.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_') ? actual : model);
            preview(answer); return answer;
        }
        catch (JsonException) { throw new InvalidDataException("Gemini returned an invalid or incomplete response. Nothing was inserted."); }
        catch (InvalidOperationException ex) when (ex.Source == "System.Text.Json") { throw new InvalidDataException("Gemini returned an invalid response. Nothing was inserted."); }
    }
    private static void WriteParts(Utf8JsonWriter writer, string text)
    { writer.WriteStartArray("parts"); writer.WriteStartObject(); writer.WriteString("text", text); writer.WriteEndObject(); writer.WriteEndArray(); }
}
