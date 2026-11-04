using SharpVoice.Refiner;
using SharpVoice.Core;
using System.Net;
using System.Text;
using System.Text.Json;

internal static class ProviderDiagnosticChecks
{
    public static async Task TlsExplanation()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException(HttpRequestError.SecureConnectionError, "private-host.corp fixture-secret")));
        try
        {
            await new GeminiApiClient(http, () => "fixture-google-api-key").RefineAsync("private input", "private rules", "auto", _ => { }, _ => { }, default);
            throw new Exception("TLS failure was accepted.");
        }
        catch (InvalidOperationException ex)
        {
            if (!ex.Message.Contains("TLS", StringComparison.Ordinal)) throw new Exception("Gemini hides the TLS failure category.");
            if (ex.Message.Contains("private-host") || ex.Message.Contains("fixture-secret")) throw new Exception("TLS diagnostics exposed private exception text.");
        }
    }

    public static async Task All()
    {
        await TlsExplanation(); await ResponseMeaning(); await RejectionGuidance(); await NestedDeadline(); await OAuthCallbackChecks.Diagnostics(); await JournalPrivacy(); await Categories(); await Lifetimes(); await ChatGptIntegration(); await GeminiIntegration();
        Console.WriteLine("PASS provider diagnostics: categories, privacy, retention, cancellation, deadlines, nested operations and production transports.");
    }
    public static Task JournalPrivacy()
    {
        var journal = new ProviderDiagnostics();
        for (var i = 0; i < 25; i++) journal.Record(new(ProviderOperationKind.GeminiInference, ProviderFailureKind.Tls, null, i, i));
        var rows = journal.Snapshot();
        if (rows.Length != 20 || rows[0].RuntimeCode != 5 || rows[^1].RuntimeCode != 24) throw new Exception("Diagnostic retention/order failed.");
        var report = journal.Report();
        if (!report.Contains("Failures: 20") || Encoding.UTF8.GetByteCount(report) > 16384) throw new Exception("Report is incomplete or oversized.");
        if (new Uri(ProviderDiagnostics.IssueUrl).Query.Contains("body", StringComparison.OrdinalIgnoreCase)) throw new Exception("Issue URL carries report data.");
        journal.Clear(); if (journal.Snapshot().Length != 0 || journal.Report().Contains("Operation:")) throw new Exception("Clear retained diagnostic records.");
        return Task.CompletedTask;
    }
    public static async Task Categories()
    {
        foreach (var (error, expected) in new[] {
            (HttpRequestError.NameResolutionError, ProviderFailureKind.Dns),
            (HttpRequestError.SecureConnectionError, ProviderFailureKind.Tls),
            (HttpRequestError.ProxyTunnelError, ProviderFailureKind.Proxy),
            (HttpRequestError.ConnectionError, ProviderFailureKind.Connection),
            (HttpRequestError.ResponseEnded, ProviderFailureKind.InterruptedResponse),
            (HttpRequestError.InvalidResponse, ProviderFailureKind.InvalidResponse) })
        {
            ProviderDiagnostics.Shared.Clear();
            await ExpectFailure(() => ProviderOperation.RunAsync<string>(ProviderOperationKind.ChatGptInference,
                _ => Task.FromException<string>(new HttpRequestException(error, "fixture-secret private-host.corp C:\\Users\\Private")), default));
            var record = ProviderDiagnostics.Shared.Snapshot().Single();
            if (record.Kind != expected) throw new Exception("Wrong network category: " + error);
            AssertPrivate(ProviderDiagnostics.Shared.Report());
        }
        ProviderDiagnostics.Shared.Clear();
        await ExpectFailure(() => ProviderOperation.RunAsync<string>(ProviderOperationKind.ChatGptInference,
            ct => ProviderOperation.RunAsync<string>(ProviderOperationKind.ChatGptRefresh,
                _ => Task.FromException<string>(new HttpRequestException(HttpRequestError.NameResolutionError, "fixture-secret")), ct), default));
        if (ProviderDiagnostics.Shared.Snapshot() is not [{ Operation: ProviderOperationKind.ChatGptRefresh }]) throw new Exception("Nested refresh was attributed twice or to inference.");
    }
    public static async Task ResponseMeaning()
    {
        ProviderDiagnostics.Shared.Clear();
        using (var http = new HttpClient(new StubHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent("<html>fixture-secret</html>", Encoding.UTF8, "text/html") })))
            await ExpectFailure(() => new UnifiedRefiner(http, new FixtureSessionProvider()).RefineAsync("source", "", new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "gpt-fixture", MinimumAiWords = 0 }, _ => { }, default, askAi: true));
        if (ProviderDiagnostics.Shared.Snapshot() is not [{ Kind: ProviderFailureKind.InvalidResponse }]) throw new Exception("HTML block page was incorrectly classified as interrupted SSE.");
    }
    public static async Task RejectionGuidance()
    {
        foreach (var (provider, payload, category, guidance) in new[] {
            ("gemini_api", "{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\",\"content\":{\"parts\":[{\"text\":\"fixture-secret\"}]}}]}", "OutputLimit", "Shorten"),
            ("gemini_api", "{\"candidates\":[{\"finishReason\":\"SAFETY\"}]}", "ProviderRejected", "did not complete"),
            ("chatgpt_oauth", "data: {\"type\":\"response.incomplete\"}\n\n", "ProviderRejected", "did not complete"),
            ("chatgpt_oauth", "data: {\"type\":\"response.failed\"}\n\n", "ProviderRejected", "did not complete") })
        {
            ProviderDiagnostics.Shared.Clear();
            using var http = new HttpClient(new StubHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, provider == "gemini_api" ? "application/json" : "text/event-stream") }));
            var refiner = new UnifiedRefiner(http, new FixtureSessionProvider(), geminiApi: new GeminiApiClient(http, () => "fixture-google-api-key"));
            try { await refiner.RefineAsync("fixture-secret", "", new AppConfig { Backend = provider, RefinementModel = provider == "gemini_api" ? "auto" : "gpt-fixture", MinimumAiWords = 0 }, _ => { }, default, askAi: true); throw new Exception("Incomplete answer accepted."); }
            catch (InvalidDataException ex) { if (!ex.Message.Contains(guidance, StringComparison.OrdinalIgnoreCase)) throw new Exception("Fixed provider rejection guidance was lost."); AssertPrivate(ex.Message); }
            if (ProviderDiagnostics.Shared.Snapshot() is not [var record] || record.Kind.ToString() != category) throw new Exception("Provider rejection was mislabeled as malformed data.");
            AssertPrivate(ProviderDiagnostics.Shared.Report());
        }
    }
    public static async Task NestedDeadline()
    {
        ProviderDiagnostics.Shared.Clear();
        await ExpectFailure(() => ProviderOperation.RunAsync<string>(ProviderOperationKind.ChatGptInference,
            token => ProviderOperation.RunAsync<string>(ProviderOperationKind.ChatGptModels,
                async ct => { await Task.Delay(10000, ct); return "late"; }, token), default, TimeSpan.FromMilliseconds(30)));
        if (ProviderDiagnostics.Shared.Snapshot() is not [{ Operation: ProviderOperationKind.ChatGptModels, Kind: ProviderFailureKind.Timeout }])
            throw new Exception("An ancestor deadline hid the active catalog operation.");
    }
    public static async Task Lifetimes()
    {
        ProviderDiagnostics.Shared.Clear();
        using var caller = new CancellationTokenSource(); caller.Cancel();
        try { await ProviderOperation.RunAsync<string>(ProviderOperationKind.GeminiInference, ct => Task.FromCanceled<string>(ct), caller.Token); throw new Exception("Cancellation accepted."); }
        catch (OperationCanceledException) { }
        if (ProviderDiagnostics.Shared.Snapshot().Length != 0) throw new Exception("Caller cancellation was recorded as a network failure.");
        await ExpectFailure(() => ProviderOperation.RunAsync<string>(ProviderOperationKind.GeminiInference, async ct => { await Task.Delay(10000, ct); return "late"; }, default, TimeSpan.FromMilliseconds(30)));
        if (ProviderDiagnostics.Shared.Snapshot() is not [{ Kind: ProviderFailureKind.Timeout }]) throw new Exception("Deadline was not recorded as timeout.");
    }
    public static async Task ChatGptIntegration()
    {
        ProviderDiagnostics.Shared.Clear();
        using var denied = new HttpClient(new StubHandler(_ => new(HttpStatusCode.Forbidden) { Content = new StringContent("fixture-secret private-host.corp") }));
        await ExpectFailure(() => new UnifiedRefiner(denied, new FixtureSessionProvider()).RefineAsync("private input", "", new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "auto", MinimumAiWords = 0 }, _ => { }, default, askAi: true));
        if (ProviderDiagnostics.Shared.Snapshot() is not [{ Operation: ProviderOperationKind.ChatGptModels, HttpStatus: 403 }]) throw new Exception("Catalog failure was not recorded once at its source.");
        foreach (var response in new[] { "data: {broken}\n\n", "data: {\"type\":\"response.output_text.delta\",\"delta\":\"fixture-secret\"}\n\n" })
        {
            ProviderDiagnostics.Shared.Clear();
            using var broken = new HttpClient(new StubHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "text/event-stream") }));
            await ExpectFailure(() => new UnifiedRefiner(broken, new FixtureSessionProvider()).RefineAsync("fixture-secret", "", new AppConfig { Backend = "chatgpt_oauth", RefinementModel = "gpt-fixture", MinimumAiWords = 0 }, _ => { }, default, askAi: true));
            if (ProviderDiagnostics.Shared.Snapshot() is not [{ Operation: ProviderOperationKind.ChatGptInference }]) throw new Exception("Malformed/interrupted inference was not diagnosed.");
            AssertPrivate(ProviderDiagnostics.Shared.Report());
        }
    }
    public static async Task GeminiIntegration()
    {
        foreach (var status in new[] { 400, 401, 403, 404, 407, 429, 500 })
        {
            ProviderDiagnostics.Shared.Clear();
            using var http = new HttpClient(new StubHandler(_ => {
                var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("fixture-secret private-host.corp") };
                response.Headers.Add("X-Private", "fixture-secret"); return response;
            }));
            await ExpectFailure(() => new GeminiApiClient(http, () => "fixture-google-api-key").RefineAsync("fixture-secret", "private rules", "gemini-custom-private", _ => { }, _ => { }, default));
            if (ProviderDiagnostics.Shared.Snapshot() is not [{ Operation: ProviderOperationKind.GeminiInference, Kind: ProviderFailureKind.Http } record] || record.HttpStatus != status) throw new Exception("Gemini HTTP failure lacks its status.");
            AssertPrivate(ProviderDiagnostics.Shared.Report());
        }
        foreach (var content in new HttpContent[] { new StringContent("<html>fixture-secret</html>"), new StreamContent(new BrokenStream()), new StreamContent(new WaitingStream()) })
        {
            ProviderDiagnostics.Shared.Clear();
            using var http = new HttpClient(new StubHandler(_ => new(HttpStatusCode.OK) { Content = content }));
            await ExpectFailure(() => ProviderOperation.RunAsync(ProviderOperationKind.GeminiInference, ct => new GeminiApiClient(http, () => "fixture-google-api-key").RefineAsync("fixture-secret", "rules", "auto", _ => { }, _ => { }, ct), default, TimeSpan.FromMilliseconds(50)));
            if (ProviderDiagnostics.Shared.Snapshot().Length != 1) throw new Exception("Body failure produced missing/duplicate records: " + content.GetType().Name + " count=" + ProviderDiagnostics.Shared.Snapshot().Length);
            AssertPrivate(ProviderDiagnostics.Shared.Report());
        }
    }
    private static async Task ExpectFailure(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ProviderStreamException) { AssertPrivate(ex.Message); return; }
        throw new Exception("Expected a sanitized provider failure.");
    }
    private static void AssertPrivate(string text)
    {
        foreach (var secret in new[] { "fixture-secret", "private-host", "Private", "private rules", "fixture-google-api-key", "gemini-custom-private" })
            if (text.Contains(secret, StringComparison.Ordinal)) throw new Exception("Diagnostic leaked private input: " + secret);
    }
    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => ValueTask.FromException<int>(new IOException("fixture-secret private-host.corp"));
    }
    private sealed class WaitingStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { await Task.Delay(10000, ct); return 0; }
    }
}
