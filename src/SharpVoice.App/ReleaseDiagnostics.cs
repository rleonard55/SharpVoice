using System.Text.Json;
using System.Security.Cryptography;
using SharpVoice.Speech;

namespace SharpVoice.App;

internal static class ReleaseDiagnostics
{
    // Packaging-only check: isolated empty cache, no network, microphone or accounts.
    internal static int VerifyBundledModel(string output)
    {
        var cache = Path.Combine(Path.GetTempPath(), "SharpVoiceModelCheck-" + Guid.NewGuid().ToString("N"));
        using var handler = new NoNetwork();
        using var http = new HttpClient(handler);
        try
        {
            var model = new ModelManager(http, cache).EnsureAsync("tiny.en-q5_1", _ => { }, CancellationToken.None).GetAwaiter().GetResult();
            var included = Path.Combine(AppContext.BaseDirectory, "models", "ggml-tiny.en-q5_1.bin");
            if (!string.Equals(model, included, StringComparison.OrdinalIgnoreCase) || handler.Requests != 0)
                throw new InvalidDataException("The packaged default model did not resolve offline.");
            using var modelStream = File.OpenRead(model);
            var hash = Convert.ToHexString(SHA256.HashData(modelStream)).ToLowerInvariant();
            using var stream = File.Create(output);
            using var json = new Utf8JsonWriter(stream);
            json.WriteStartObject();
            json.WriteString("model", "tiny.en-q5_1");
            json.WriteString("sha256", hash);
            json.WriteNumber("network_requests", handler.Requests);
            json.WriteEndObject();
            return 0;
        }
        finally { if (Directory.Exists(cache)) Directory.Delete(cache, true); }
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; throw new InvalidOperationException("Network access is disabled in the packaged-model check."); }
    }

    internal static int Write(string output)
    {
        using var updater = new GitHubReleaseUpdater();
        using var stream = File.Create(output);
        using var json = new Utf8JsonWriter(stream);
        json.WriteStartObject();
        json.WriteString("version", updater.CurrentVersion);
        json.WriteBoolean("updates_supported", updater.Supported);
        json.WriteString("ready_version", updater.ReadyVersion);
        json.WriteString("feed", GitHubReleaseUpdater.RepositoryUrl);
        json.WriteEndObject();
        return 0;
    }
}
