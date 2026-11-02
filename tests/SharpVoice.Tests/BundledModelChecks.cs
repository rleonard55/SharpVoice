using System.Net;
using System.Security.Cryptography;
using SharpVoice.Speech;

internal static class BundledModelChecks
{
    private const string Tiny = "tiny.en-q5_1";
    private static readonly byte[] Good = "synthetic verified model"u8.ToArray();
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Good));

    public static async Task UsesBundle()
    {
        using var fixture = new Fixture();
        var bundled = fixture.Bundle(Tiny, Good);
        var result = await fixture.Manager.EnsureAsync(Tiny, _ => { }, default);
        Check(result == bundled && fixture.Handler.Requests == 0, "The included default model must work without an HTTP request.");
        Check(!File.Exists(fixture.Cached(Tiny)), "Do not duplicate the included model into the user cache.");
        Check(await fixture.Manager.EnsureAsync(Tiny, _ => { }, default) == bundled, "Repeated use lost the bundled model.");
    }

    public static async Task CachePriority()
    {
        using var fixture = new Fixture();
        fixture.Bundle(Tiny, Good);
        await File.WriteAllBytesAsync(fixture.Cached(Tiny), Good);
        Check(await fixture.Manager.EnsureAsync(Tiny, _ => { }, default) == fixture.Cached(Tiny), "A verified user cache must remain preferred.");
        using var damaged = new Fixture(); damaged.Bundle(Tiny, Good);
        await File.WriteAllTextAsync(damaged.Cached(Tiny), "corrupt cache");
        Check(await damaged.Manager.EnsureAsync(Tiny, _ => { }, default) == damaged.Bundled(Tiny), "A damaged cache must fall back to a verified included model.");
        Check(fixture.Handler.Requests == 0 && damaged.Handler.Requests == 0, "Valid bundled/cache models must not use the network.");
    }

    public static async Task Recovery()
    {
        foreach (var bundledBytes in new byte[]?[] { null, "corrupt bundle"u8.ToArray() })
        {
            using var fixture = new Fixture();
            if (bundledBytes is not null) fixture.Bundle(Tiny, bundledBytes);
            Check(await fixture.Manager.EnsureAsync(Tiny, _ => { }, default) == fixture.Cached(Tiny), "Missing/corrupt bundled models must retain verified download recovery.");
            Check(fixture.Handler.Requests == 1 && await ModelManager.VerifyAsync(fixture.Cached(Tiny), Hash, default), "Recovery installed unverified bytes.");
        }
        using var other = new Fixture();
        other.Bundle("base.en", Good);
        Check(await other.Manager.EnsureAsync("base.en", _ => { }, default) == other.Cached("base.en") && other.Handler.Requests == 1, "Other selections must retain their normal download path.");
        using var bad = new Fixture();
        bad.Bundle(Tiny, "invalid bundle"u8.ToArray()); bad.Handler.Payload = "invalid download"u8.ToArray();
        try { await bad.Manager.EnsureAsync(Tiny, _ => { }, default); throw new Exception("Invalid model was accepted."); }
        catch (InvalidDataException) { }
        Check(!File.Exists(bad.Cached(Tiny)) && !File.Exists(bad.Cached(Tiny) + ".download"), "Invalid downloads must not remain installed or staged.");
    }

    public static async Task Cancellation()
    {
        using var fixture = new Fixture(); fixture.Bundle(Tiny, Good);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await fixture.Manager.EnsureAsync(Tiny, _ => { }, cancel.Token); throw new Exception("Canceled resolution succeeded."); }
        catch (OperationCanceledException) { }
        Check(fixture.Handler.Requests == 0, "Canceled resolution started a download.");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Handler : HttpMessageHandler
    {
        internal int Requests;
        internal byte[] Payload = Good;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) }); }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "SharpVoiceBundledModel-" + Guid.NewGuid().ToString("N"));
        internal readonly Handler Handler = new();
        private readonly HttpClient http;
        internal readonly ModelManager Manager;
        internal Fixture()
        {
            Directory.CreateDirectory(Path.Combine(root, "cache")); Directory.CreateDirectory(Path.Combine(root, "bundle"));
            http = new(Handler); Manager = new(http, Path.Combine(root, "cache"), Path.Combine(root, "bundle"), _ => Hash);
        }
        internal string Cached(string model) => Path.Combine(root, "cache", $"ggml-{model}.bin");
        internal string Bundled(string model) => Path.Combine(root, "bundle", $"ggml-{model}.bin");
        internal string Bundle(string model, byte[] bytes) { File.WriteAllBytes(Bundled(model), bytes); return Bundled(model); }
        public void Dispose() { http.Dispose(); Directory.Delete(root, true); }
    }
}
