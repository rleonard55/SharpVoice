using System.Text.Json;

namespace SharpVoice.App;

internal static class ReleaseDiagnostics
{
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
