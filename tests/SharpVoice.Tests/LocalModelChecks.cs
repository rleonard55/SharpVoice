using System.Net;
using System.Security.Cryptography;
using SharpVoice.Core;
using SharpVoice.Refiner;
using SharpVoice.Refiner.Auth;
using System.Text.Json;

internal static class LocalModelChecks
{
    public static Task DefaultsAndPersistence()
    {
        Check(new AppConfig().Backend == "none", "Fresh install unexpectedly enables AI cleanup.");
        var path = Path.Combine(Path.GetTempPath(), "sharpvoice-default-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Check(ConfigManager.Load(path).Backend == "none", "Missing config enables AI.");
            foreach (var backend in new[] { "none", "chatgpt_oauth", "claude_code", "gemini_antigravity", "embedded" })
            {
                var id = backend == "embedded" ? LocalAiModels.All[1].Id : "auto";
                ConfigManager.Save(new AppConfig { Backend = backend, RefinementModel = id }, path);
                var saved = ConfigManager.Load(path);
                Check(saved.Backend == backend && saved.RefinementModel == id, "Saved preference was reset to installation default.");
            }
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject(); foreach (var prop in document.RootElement.EnumerateObject()) if (prop.Name != "backend") prop.WriteTo(writer); writer.WriteEndObject();
            }
            File.WriteAllBytes(path, output.ToArray());
            Check(ConfigManager.Load(path).Backend == "none", "Missing optional backend did not choose no cleanup.");
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }
    public static async Task TransferAndCache()
    {
        var data = Enumerable.Range(0, 240000).Select(x => (byte)(x % 251)).ToArray();
        var fixture = Model(data); var path = TempPath(); var requests = 0;
        using var http = new HttpClient(new Handler(_ => { requests++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }; }));
        try
        {
            var store = new LocalModelStore(http); var events = new List<LocalModelProgress>();
            var result = await store.EnsureAtPathAsync(fixture, path, events.Add, default);
            Check(result == path && File.ReadAllBytes(path).SequenceEqual(data), "Downloaded model was not installed correctly.");
            Check(events.Any(x => x.Stage == LocalModelStage.Downloading && x.Bytes > 0) && events.Any(x => x.Stage == LocalModelStage.Verifying) && events.Last().Stage == LocalModelStage.Ready, "Download or verification progress missing.");
            Check(events.Where(x => x.Stage == LocalModelStage.Downloading).Select(x => x.Bytes).SequenceEqual(events.Where(x => x.Stage == LocalModelStage.Downloading).Select(x => x.Bytes).Order()), "Transfer progress went backwards.");
            await store.EnsureAtPathAsync(fixture, path, _ => { }, default);
            Check(requests == 1 && !Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".download-*").Any(), "A valid cached model was re-downloaded or a partial file leaked.");
        }
        finally { Cleanup(path); }
    }
    public static async Task IntegrityAndCancellation()
    {
        var bytes = new byte[240000]; var fixture = Model(bytes); var path = TempPath();
        var old = new byte[] { 3, 4, 5 }; File.WriteAllBytes(path, old);
        try
        {
            using var bad = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[8]) }));
            await Reject<InvalidDataException>(() => new LocalModelStore(bad).EnsureAtPathAsync(fixture, path, _ => { }, default));
            Check(File.ReadAllBytes(path).SequenceEqual(old), "Failed transfer replaced the old model.");
            using var wrongHash = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Enumerable.Repeat((byte)1, bytes.Length).ToArray()) }));
            await Reject<InvalidDataException>(() => new LocalModelStore(wrongHash).EnsureAtPathAsync(fixture, path, _ => { }, default));
            using var extra = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new UnknownLength(new byte[bytes.Length + 1]) }));
            await Reject<InvalidDataException>(() => new LocalModelStore(extra).EnsureAtPathAsync(fixture, path, _ => { }, default));
            using var valid = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
            using var cancel = new CancellationTokenSource();
            await Reject<OperationCanceledException>(() => new LocalModelStore(valid).EnsureAtPathAsync(fixture, path, p => { if (p.Stage == LocalModelStage.Downloading && p.Bytes > 0) cancel.Cancel(); }, cancel.Token));
            Check(File.ReadAllBytes(path).SequenceEqual(old) && !Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".download-*").Any(), "Cancel replaced the model or leaked a partial file.");
            await new LocalModelStore(valid).EnsureAtPathAsync(fixture, path, _ => { }, default);
            Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Retry after cancellation failed.");
        }
        finally { Cleanup(path); }
    }
    public static async Task ConcurrentPreparation()
    {
        var bytes = new byte[240000]; var fixture = Model(bytes); var path = TempPath(); var requests = 0;
        using var http = new HttpClient(new Handler(_ => { Interlocked.Increment(ref requests); return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; }));
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => new LocalModelStore(http).EnsureAtPathAsync(fixture, path, _ => { }, default)));
            Check(requests == 1, "Concurrent preparation fetched the same model more than once.");
        }
        finally { Cleanup(path); }
    }
    public static async Task SelectedInference()
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("Canceled inference attempted a download.")));
        using var stop = new CancellationTokenSource();
        var refiner = new UnifiedRefiner(http, new NoSession()); var actual = "";
        refiner.ModelResolved += id => { actual = id; stop.Cancel(); };
        await Reject<OperationCanceledException>(() => refiner.RefineAsync("A fixed test request", "", new AppConfig { Backend = "embedded", RefinementModel = LocalAiModels.All[1].Id }, _ => { }, stop.Token, askAi: true, allowModelDownload: false));
        Check(actual == LocalAiModels.All[1].Id, "Inference ignored the selected local model.");
    }
    private sealed class NoSession : IChatGptSessionProvider
    { public Task<ChatGptSession> GetSessionAsync(CancellationToken ct) => throw new Exception("Local inference accessed an account."); }
    private sealed class UnknownLength(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
    private static LocalAiModel Model(byte[] data) => new("fixture-model", "Fixture", data.Length, Convert.ToHexString(SHA256.HashData(data)), "https://fixture.invalid/model.gguf");
    private static string TempPath() => Path.Combine(Path.GetTempPath(), "sharpvoice-model-" + Guid.NewGuid().ToString("N") + ".gguf");
    private static void Cleanup(string path) { File.Delete(path); foreach (var temp in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".download-*")) File.Delete(temp); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Reject<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); } }
}
