using System.Net;
using System.Net.Sockets;
using System.Text;
using SharpVoice.Refiner.Auth;

internal static class OAuthCallbackChecks
{
    private const string Tokens = "{\"access_token\":\"fixture-access\",\"refresh_token\":\"fixture-refresh\",\"expires_in\":3600}";
    public static async Task CompletionOrder()
    {
        var tokenRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(async ct =>
        {
            tokenRequest.TrySetResult();
            await release.Task.WaitAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(Tokens) };
        });
        var signingIn = fixture.Auth.SignInAsync(fixture.Timeout.Token);
        var callback = await fixture.CallbackAsync();
        var browser = fixture.Browser.GetAsync(callback);
        try
        {
            await tokenRequest.Task.WaitAsync(fixture.Timeout.Token);
            if (await Task.WhenAny(browser, Task.Delay(150)) == browser)
                throw new Exception("Browser received a success response before token exchange and persistence completed.");
            Check(!fixture.Auth.IsSignedIn && !fixture.Saved, "Login must not be published before persistence.");
        }
        finally { release.TrySetResult(); }
        await signingIn;
        using var response = await browser;
        var html = await response.Content.ReadAsStringAsync();
        Check(fixture.Saved && fixture.Auth.IsSignedIn && response.StatusCode == HttpStatusCode.OK, "Success must mean credentials are saved.");
        Check(response.Content.Headers.ContentType?.MediaType == "text/html" && html.Contains("Sign-in successful"), "Browser needs a clear HTML success page.");
    }

    public static async Task Failures()
    {
        foreach (var saveFailure in new[] { false, true })
        {
            using var fixture = new Fixture(_ => Task.FromResult(new HttpResponseMessage(saveFailure ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)
            { Content = new StringContent(saveFailure ? Tokens : "PRIVATE_PROVIDER_DETAIL") }),
                saveFailure ? () => throw new IOException("PRIVATE_SAVE_DETAIL") : null);
            var signingIn = fixture.Auth.SignInAsync(fixture.Timeout.Token);
            using var response = await fixture.Browser.GetAsync(await fixture.CallbackAsync());
            var failed = false;
            try { await signingIn; } catch (Exception ex) when (ex is IOException or InvalidOperationException) { failed = true; }
            var html = await response.Content.ReadAsStringAsync();
            Check(failed && !fixture.Auth.IsSignedIn && !fixture.Saved && response.StatusCode == HttpStatusCode.InternalServerError,
                "Exchange/persistence failure must not publish a connected state.");
            Check(html.Contains("Sign-in failed") && !html.Contains("PRIVATE_") && !html.Contains("fixture-access"), "Browser failures must be clear without leaking diagnostics or credentials.");
        }
        using (var fixture = new Fixture(_ => throw new Exception("Declined sign-in must not exchange tokens.")))
        {
            var signingIn = fixture.Auth.SignInAsync(fixture.Timeout.Token);
            using var response = await fixture.Browser.GetAsync(await fixture.CallbackAsync("error=access_denied&error_description=PRIVATE_PROVIDER_DETAIL"));
            var declined = false;
            try { await signingIn; } catch (InvalidOperationException) { declined = true; }
            var html = await response.Content.ReadAsStringAsync();
            Check(declined && response.StatusCode == HttpStatusCode.BadRequest && html.Contains("Sign-in cancelled") && !html.Contains("PRIVATE_"), "Decline must have a branded cancellation result.");
        }
        using (var fixture = new Fixture(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Tokens) })))
        {
            var signingIn = fixture.Auth.SignInAsync(fixture.Timeout.Token);
            var callback = await fixture.CallbackAsync();
            using var wrong = await fixture.Browser.GetAsync(callback.Replace("?state=", "?state=wrong-"));
            Check(wrong.StatusCode == HttpStatusCode.BadRequest && (await wrong.Content.ReadAsStringAsync()).Contains("Invalid sign-in link") && !signingIn.IsCompleted,
                "Invalid state must show a safe failure without ending the valid pending sign-in.");
            using var missing = await fixture.Browser.GetAsync(await fixture.CallbackAsync("unused=value"));
            Check(missing.StatusCode == HttpStatusCode.BadRequest && !fixture.Auth.IsSignedIn, "Missing codes must not complete sign-in.");
            using var success = await fixture.Browser.GetAsync(callback);
            await signingIn;
            Check(success.StatusCode == HttpStatusCode.OK && fixture.Saved, "Valid callback must still work after invalid requests.");
        }
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var fixture = new Fixture(async ct =>
        {
            requested.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new Exception("Cancelled exchange resumed.");
        }))
        {
            var signingIn = fixture.Auth.SignInAsync(fixture.Timeout.Token);
            var browser = fixture.Browser.GetAsync(await fixture.CallbackAsync());
            await requested.Task.WaitAsync(fixture.Timeout.Token);
            fixture.Timeout.Cancel();
            var cancelled = false;
            try { await signingIn; } catch (OperationCanceledException) { cancelled = true; }
            using var response = await browser;
            Check(cancelled && response.StatusCode == HttpStatusCode.InternalServerError && !fixture.Saved, "Cancellation must not produce browser success.");
        }
    }

    public static async Task DisconnectedBrowser()
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new Fixture(async ct =>
        {
            requested.TrySetResult(); await release.Task.WaitAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(Tokens) };
        });
        var signingIn = fixture.Auth.SignInAsync(fixture.Timeout.Token);
        var callback = new Uri(await fixture.CallbackAsync());
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, callback.Port);
            var request = Encoding.ASCII.GetBytes("GET " + callback.PathAndQuery + " HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
            await client.GetStream().WriteAsync(request);
            await requested.Task.WaitAsync(fixture.Timeout.Token);
            client.Client.LingerState = new LingerOption(true, 0);
        }
        release.TrySetResult();
        await signingIn;
        Check(fixture.Saved && fixture.Auth.IsSignedIn, "Browser disconnect after token exchange must not invalidate a saved login.");
    }

    public static async Task Reconnect()
    {
        foreach (var outcome in new[] { "success", "declined", "exchange", "save" })
        {
            var attempt = 0;
            using var fixture = new Fixture(_ => Task.FromResult(new HttpResponseMessage(attempt == 1 && outcome == "exchange" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
            { Content = new StringContent(attempt == 0 ? Tokens : Tokens.Replace("fixture-access", "replacement-access")) }),
                () => { if (attempt == 1 && outcome == "save") throw new IOException("Fixture save failure"); });
            var initial = fixture.Auth.SignInAsync(fixture.Timeout.Token);
            using var firstResponse = await fixture.Browser.GetAsync(await fixture.CallbackAsync());
            await initial;
            var original = await fixture.Auth.GetSessionAsync(fixture.Timeout.Token);
            attempt = 1;
            fixture.NextAttempt();
            var reconnect = fixture.Auth.SignInAsync(fixture.Timeout.Token);
            var callback = await fixture.CallbackAsync(outcome == "declined" ? "error=access_denied" : null);
            Check(fixture.Auth.IsSignedIn && ReferenceEquals(original, fixture.StoredSession), "Starting reconnect must preserve the current login.");
            using var response = await fixture.Browser.GetAsync(callback);
            var failed = false;
            try { await reconnect; } catch (Exception ex) when (ex is IOException or InvalidOperationException) { failed = true; }
            var current = await fixture.Auth.GetSessionAsync(fixture.Timeout.Token);
            Check(fixture.Auth.IsSignedIn && failed == (outcome != "success"), "Reconnect outcome was incorrect.");
            Check(outcome == "success"
                ? fixture.Saves == 2 && !ReferenceEquals(original, current) && ReferenceEquals(current, fixture.StoredSession)
                : fixture.Saves == 1 && ReferenceEquals(original, current) && ReferenceEquals(original, fixture.StoredSession),
                "Only a successfully saved replacement may replace the existing session.");
        }
    }

    public static async Task Pages()
    {
        foreach (var (status, outcome) in new[] { (200, OAuthCallbackOutcome.Success), (400, OAuthCallbackOutcome.Declined), (400, OAuthCallbackOutcome.Invalid), (500, OAuthCallbackOutcome.Failed) })
        {
            using var stream = new MemoryStream();
            await OAuthCallbackPage.WriteAsync(stream, status, outcome, default);
            var bytes = stream.ToArray();
            var wire = Encoding.UTF8.GetString(bytes);
            var end = wire.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var header = wire[..end]; var html = wire[(end + 4)..];
            var lengthLine = header.Split("\r\n").Single(x => x.StartsWith("Content-Length:"));
            Check(int.Parse(lengthLine.Split(':')[1]) == Encoding.UTF8.GetByteCount(html), "HTTP content length must count UTF-8 bytes.");
            Check(header.StartsWith($"HTTP/1.1 {status} ") && header.Contains("text/html; charset=utf-8") && header.Contains("Cache-Control: no-store")
                && header.Contains("Referrer-Policy: no-referrer") && header.Contains("X-Content-Type-Options: nosniff"), "Callback response headers are incomplete.");
            var script = html.Split("<script>")[1].Split("</script>")[0];
            var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(script)));
            Check(header.Contains("script-src 'sha256-" + hash + "'") && header.Contains("default-src 'none'") && script.Contains("history.replaceState"),
                "Only the fixed callback-history cleanup script should be permitted.");
            Check(html.Contains("SharpVoice") && html.Contains("prefers-color-scheme:dark") && html.Contains("name=\"viewport\"")
                && html.Contains("aria-labelledby=\"result-heading\"") && !html.Contains("{{"), "Branded page must be responsive, themed and accessible.");
            Check(!html.Contains("https://") && !html.Contains("fixture-code") && !html.Contains("fixture-access"), "Result page must not fetch external assets or expose credentials.");
            Check(html.Contains(outcome == OAuthCallbackOutcome.Success ? "data-outcome=\"success\"" : "data-outcome=\"failure\""), "Visual result must match the actual outcome.");
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Fixture : IDisposable
    {
        private TcpListener? listener;
        public readonly CancellationTokenSource Timeout = new(TimeSpan.FromSeconds(10));
        public readonly HttpClient Browser = new();
        private readonly HttpClient exchange;
        private TaskCompletionSource<string> opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ChatGptAuthManager Auth;
        public bool Saved;
        public int Saves;
        public ChatGptSession? StoredSession;
        public void NextAttempt() => opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Fixture(Func<CancellationToken, Task<HttpResponseMessage>> tokenResponse, Action? save = null)
        {
            exchange = new HttpClient(new Handler(tokenResponse));
            Auth = new(exchange, url => opened.TrySetResult(url), next => { save?.Invoke(); Saved = true; Saves++; StoredSession = next; }, () => listener = new(IPAddress.Loopback, 0));
        }
        public async Task<string> CallbackAsync(string? extra = null)
        {
            var url = new Uri(await opened.Task.WaitAsync(Timeout.Token));
            var state = ChatGptAuthManager.ParseQuery(url.Query)["state"];
            var port = ((IPEndPoint)listener!.LocalEndpoint).Port;
            return $"http://127.0.0.1:{port}/auth/callback?state={Uri.EscapeDataString(state)}&" + (extra ?? "code=fixture-code");
        }
        public void Dispose() { Timeout.Cancel(); Auth.Dispose(); Browser.Dispose(); exchange.Dispose(); listener?.Stop(); Timeout.Dispose(); }
    }
    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri?.AbsolutePath == "/oauth/token", "Unexpected authentication endpoint.");
            return respond(ct);
        }
    }
}
