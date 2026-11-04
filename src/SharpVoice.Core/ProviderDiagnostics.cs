using System.Globalization;
using System.Reflection;
using System.Text;

namespace SharpVoice.Core;

public enum ProviderOperationKind { ChatGptSignIn, ChatGptRefresh, ChatGptModels, ChatGptInference, GeminiInference }
public enum ProviderFailureKind { Dns, Connection, Tls, Proxy, Http, Timeout, InterruptedResponse, InvalidResponse, ProviderRejected, OutputLimit, Unknown }

/// <summary>Only fixed identifiers and numbers may cross the diagnostic boundary.</summary>
public sealed record ProviderFailure(ProviderOperationKind Operation, ProviderFailureKind Kind, int? HttpStatus, int? RuntimeCode, long ElapsedMilliseconds)
{
    public string Provider => Operation == ProviderOperationKind.GeminiInference ? "Gemini API" : "ChatGPT";
    public string Endpoint => Operation switch
    {
        ProviderOperationKind.ChatGptSignIn or ProviderOperationKind.ChatGptRefresh => "auth.openai.com/oauth/token",
        ProviderOperationKind.ChatGptModels => "chatgpt.com/backend-api/codex/models",
        ProviderOperationKind.ChatGptInference => "chatgpt.com/backend-api/codex/responses",
        ProviderOperationKind.GeminiInference => "generativelanguage.googleapis.com/generateContent",
        _ => "unknown"
    };
    public string Explanation => Kind switch
    {
        ProviderFailureKind.Dns => "DNS lookup failed. Ask IT to check name resolution and proxy configuration.",
        ProviderFailureKind.Tls => "TLS handshake failed. Ask IT to check the certificate chain and TLS inspection logs; this does not establish a certificate trust failure.",
        ProviderFailureKind.Proxy => "The proxy tunnel failed or requires authentication. Ask IT to check proxy access and authentication.",
        ProviderFailureKind.Connection => "The network connection failed. Ask IT to check endpoint access and connection resets.",
        ProviderFailureKind.Timeout => "The provider operation timed out, including response reading. Check provider availability and proxy timeouts.",
        ProviderFailureKind.InterruptedResponse => "The response ended before completion. Check connection resets and streaming/idle timeout policies.",
        ProviderFailureKind.InvalidResponse => "The provider returned an invalid or unexpected response. Ask IT to check for rewritten or blocked responses.",
        ProviderFailureKind.OutputLimit => "The provider reached its output limit. Shorten the request and retry; partial text was not inserted.",
        ProviderFailureKind.ProviderRejected => "The provider did not complete the answer. Check provider access and retry with a shorter request.",
        ProviderFailureKind.Http => HttpStatus switch
        {
            400 => "HTTP 400: the provider rejected the request. Check the selected model and provider configuration.",
            401 => "HTTP 401: provider authentication failed. Sign in again or check the saved API key.",
            403 => "HTTP 403: access was denied. Check provider permissions and network filtering; this alone does not establish a bad credential.",
            404 => "HTTP 404: the model or endpoint is unavailable. Choose Auto or an available model ID.",
            407 => "HTTP 407: the proxy requires authentication. Ask IT to check proxy credentials and policy.",
            429 => "HTTP 429: quota or rate limit reached. Check provider quota/billing or retry later.",
            >= 500 => $"HTTP {HttpStatus}: a server or gateway failed. Retry later and check gateway logs.",
            _ => $"HTTP {HttpStatus}: the request did not succeed. Check provider access and network policy."
        },
        _ => "The provider operation failed for an unclassified reason. Retry the fixed connection test and share its sanitized diagnostics with IT."
    };
}

/// <summary>Preserves the I/O failure contract without retaining private stream exception details.</summary>
public sealed class ProviderStreamException(ProviderFailure failure)
    : IOException($"{failure.Provider} · {failure.Operation}: {failure.Explanation} Open AI & Models → Diagnostics for a sanitized report. Nothing was inserted.")
{
    public ProviderFailure Failure { get; } = failure;
}

public sealed class ProviderDiagnostics
{
    public static ProviderDiagnostics Shared { get; } = new();
    public const string IssueUrl = "https://github.com/rleonard55/SharpVoice/issues/new?template=provider-diagnostics.md";
    public const string PrivacyUrl = "https://github.com/rleonard55/SharpVoice/blob/main/docs/privacy.md";
    private readonly object gate = new();
    private readonly Queue<ProviderFailure> failures = new();
    public void Record(ProviderFailure failure)
    {
        // Normalize even public callers: enum ToString() on unknown values is not a schema field.
        var safe = failure with
        {
            Operation = Enum.IsDefined(failure.Operation) ? failure.Operation : ProviderOperationKind.ChatGptInference,
            Kind = Enum.IsDefined(failure.Kind) ? failure.Kind : ProviderFailureKind.Unknown,
            HttpStatus = failure.HttpStatus is >= 100 and <= 599 ? failure.HttpStatus : null,
            ElapsedMilliseconds = Math.Clamp(failure.ElapsedMilliseconds, 0, 86_400_000)
        };
        lock (gate) { if (failures.Count == 20) failures.Dequeue(); failures.Enqueue(safe); }
    }
    public ProviderFailure[] Snapshot() { lock (gate) return failures.ToArray(); }
    public void Clear() { lock (gate) failures.Clear(); }
    public string Report()
    {
        var rows = Snapshot();
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
        var os = Environment.OSVersion.Version;
        var text = new StringBuilder("SharpVoice sanitized provider diagnostics · schema 1\r\n");
        text.Append(CultureInfo.InvariantCulture, $"App: {version.Major}.{version.Minor}.{Math.Max(0, version.Build)}\r\nWindows: {os.Major}.{os.Minor}.{Math.Max(0, os.Build)}\r\nFailures: {rows.Length} (oldest first; session only)\r\n");
        foreach (var row in rows)
        {
            text.Append(CultureInfo.InvariantCulture, $"\r\nOperation: {row.Operation}\r\nProvider: {row.Provider}\r\nEndpoint: {row.Endpoint}\r\nCategory: {row.Kind}\r\nHTTP: {row.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "none"}\r\nRuntime code: {row.RuntimeCode?.ToString(CultureInfo.InvariantCulture) ?? "none"}\r\nElapsed ms: {row.ElapsedMilliseconds}\r\n");
        }
        return text.ToString(); // Fixed-size fields x 20 are below 16 KiB, including UTF-8 encoding.
    }
}
