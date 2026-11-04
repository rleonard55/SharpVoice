using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpVoice.Core;

namespace SharpVoice.Refiner.Auth;

public sealed record ChatGptSession(string AccessToken, string RefreshToken, string AccountId, DateTimeOffset ExpiresAt);
public sealed record TokenResponse(string AccessToken, string? RefreshToken, string? IdToken, int ExpiresIn = 3600);

public interface IChatGptSessionProvider
{
    Task<ChatGptSession> GetSessionAsync(CancellationToken ct);
}

public static class Pkce
{
    public static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string Challenge(string verifier) => Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}

/// <summary>Experimental Codex subscription transport; independent of any installed Codex session.</summary>
public sealed class ChatGptAuthManager : IChatGptSessionProvider, IDisposable
{
    private readonly HttpClient http;
    private readonly Action<string> openBrowser;
    private readonly Action<ChatGptSession>? persistOverride;
    private readonly Func<TcpListener> createListener;
    public ChatGptAuthManager(HttpClient http) : this(http,
        url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }), null,
        () => new TcpListener(IPAddress.Loopback, 1455)) { }
    internal ChatGptAuthManager(HttpClient http, Action<string> openBrowser, Action<ChatGptSession>? persistOverride, Func<TcpListener> createListener)
        => (this.http, this.openBrowser, this.persistOverride, this.createListener) = (http, openBrowser, persistOverride, createListener);

    public const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    public const string RedirectUri = "http://127.0.0.1:1455/auth/callback";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private Task? refreshLoop;
    private ChatGptSession? session;
    public bool IsSignedIn => session is not null;
    public event Action<string>? RefreshFailed;

    public void Initialize()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI is required.");
        if (File.Exists(AppPaths.Session))
        {
            var encrypted = File.ReadAllBytes(AppPaths.Session);
            var plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            try { session = JsonSerializer.Deserialize(plaintext, AuthJsonContext.Default.ChatGptSession); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        refreshLoop = RefreshLoopAsync(lifetime.Token);
    }

    public async Task SignInAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var token = timeout.Token;
            var verifier = Pkce.Encode(RandomNumberGenerator.GetBytes(64));
            var state = Pkce.Encode(RandomNumberGenerator.GetBytes(32));
            using var listener = createListener();
            listener.Start(); // Bind before launching browser; never kill another listener.
            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = ClientId, ["response_type"] = "code", ["redirect_uri"] = RedirectUri,
                ["scope"] = "openid profile email offline_access", ["state"] = state,
                ["code_challenge"] = Pkce.Challenge(verifier), ["code_challenge_method"] = "S256",
                ["id_token_add_organizations"] = "true", ["codex_cli_simplified_flow"] = "true",
                ["originator"] = "sharpvoice"
            };
            var url = "https://auth.openai.com/oauth/authorize?" + string.Join('&', parameters.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));
            openBrowser(url);
            while (true)
            {
                using var connection = await listener.AcceptTcpClientAsync(token);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var stream = connection.GetStream();
                string? requestLine;
                try { requestLine = await ReadRequestLineAsync(stream, requestTimeout.Token); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidDataException) { continue; }
                var parts = requestLine?.Split(' ');
                var valid = parts is { Length: 3 } && parts[0] == "GET" && parts[1].StartsWith('/');
                Uri? uri = null;
                valid = valid && Uri.TryCreate("http://127.0.0.1:1455" + parts![1], UriKind.Absolute, out uri) && uri.AbsolutePath == "/auth/callback";
                var query = valid ? ParseQuery(uri!.Query) : [];
                valid = valid && query.GetValueOrDefault("state") == state;
                if (!valid) { await OAuthCallbackPage.TryWriteAsync(stream, 400, OAuthCallbackOutcome.Invalid); continue; }
                if (query.ContainsKey("error"))
                {
                    await OAuthCallbackPage.TryWriteAsync(stream, 400, OAuthCallbackOutcome.Declined);
                    throw new InvalidOperationException("ChatGPT sign-in was declined.");
                }
                var code = query.GetValueOrDefault("code");
                if (string.IsNullOrEmpty(code)) { await OAuthCallbackPage.TryWriteAsync(stream, 400, OAuthCallbackOutcome.Invalid); continue; }
                try
                {
                    var next = await ExchangeAsync(new()
                    {
                        ["grant_type"] = "authorization_code", ["code"] = code,
                        ["redirect_uri"] = RedirectUri, ["code_verifier"] = verifier, ["client_id"] = ClientId
                    }, null, token);
                    token.ThrowIfCancellationRequested();
                    Save(next);
                    session = next;
                }
                catch
                {
                    await OAuthCallbackPage.TryWriteAsync(stream, 500, OAuthCallbackOutcome.Failed);
                    throw;
                }
                // A closed browser tab cannot turn a successfully saved login into a failure.
                await OAuthCallbackPage.TryWriteAsync(stream, 200, OAuthCallbackOutcome.Success);
                return;
            }
        }
        finally { gate.Release(); }
    }

    public async Task<ChatGptSession> GetSessionAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (session is null) throw new InvalidOperationException("Sign in to ChatGPT from the tray menu first.");
            if (session.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(5))
            {
                session = await ExchangeAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = session.RefreshToken, ["client_id"] = ClientId }, session, ct);
                Save();
            }
            return session;
        }
        finally { gate.Release(); }
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { session = null; if (File.Exists(AppPaths.Session)) File.Delete(AppPaths.Session); }
        finally { gate.Release(); }
    }

    private Task<ChatGptSession> ExchangeAsync(Dictionary<string, string> fields, ChatGptSession? previous, CancellationToken ct)
        => ProviderOperation.RunAsync(previous is null ? ProviderOperationKind.ChatGptSignIn : ProviderOperationKind.ChatGptRefresh, token => ExchangeCoreAsync(fields, previous, token), ct, TimeSpan.FromSeconds(60));

    private async Task<ChatGptSession> ExchangeCoreAsync(Dictionary<string, string> fields, ChatGptSession? previous, CancellationToken ct)
    {
        using var response = await http.PostAsync("https://auth.openai.com/oauth/token", new FormUrlEncodedContent(fields), ct);
        if (!response.IsSuccessStatusCode) throw new ProviderHttpException(response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        try
        {
            var result = JsonSerializer.Deserialize(bytes, AuthJsonContext.Default.TokenResponse) ?? throw new InvalidDataException("Empty token response.");
            if (string.IsNullOrWhiteSpace(result.AccessToken)) throw new InvalidDataException("Authorization response has no access token.");
            var account = AccountId(result.IdToken) ?? AccountId(result.AccessToken) ?? previous?.AccountId ?? "";
            return new(result.AccessToken, result.RefreshToken ?? previous?.RefreshToken ?? "", account, DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn > 0 ? result.ExpiresIn : 3600));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static string? AccountId(string? jwt)
    {
        try
        {
            if (jwt is null) return null;
            var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            // Read routing metadata only. The backend validates the access token.
            return document.RootElement.TryGetProperty("https://api.openai.com/auth", out var auth)
                && auth.TryGetProperty("chatgpt_account_id", out var id) ? id.GetString() : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or IndexOutOfRangeException) { return null; }
    }

    private void Save(ChatGptSession? next = null)
    {
        var value = next ?? session!;
        if (persistOverride is not null) { persistOverride(value); return; }
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Directory.CreateDirectory(AppPaths.Root);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, AuthJsonContext.Default.ChatGptSession);
        try
        {
            var encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            var temporary = AppPaths.Session + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, AppPaths.Session, true);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!IsSignedIn) continue;
                try { await GetSessionAsync(ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch { RefreshFailed?.Invoke("ChatGPT session refresh failed. Sign in again if refinement fails."); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            result[Uri.UnescapeDataString(kv[0])] = kv.Length == 2 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
        }
        return result;
    }

    private static async Task<string?> ReadRequestLineAsync(Stream stream, CancellationToken ct)
    {
        var bytes = new byte[8192];
        int length = 0;
        while (length < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(length, 1), ct);
            if (read == 0) return null;
            length++;
            if (length >= 4 && bytes.AsSpan(length - 4, 4).SequenceEqual("\r\n\r\n"u8))
            {
                var end = Array.IndexOf(bytes, (byte)'\r', 0, length);
                return Encoding.ASCII.GetString(bytes, 0, end);
            }
        }
        throw new InvalidDataException("Callback request too long.");
    }

    public void Dispose()
    {
        lifetime.Cancel();
        refreshLoop?.GetAwaiter().GetResult();
        lifetime.Dispose();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ChatGptSession))]
[JsonSerializable(typeof(TokenResponse))]
internal partial class AuthJsonContext : JsonSerializerContext;
