using System.Reflection;
using System.Text.Json;
using Velopack;

if (!OperatingSystem.IsWindows()) return 1;
VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
var version = typeof(FixtureMarker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
var operation = args.FirstOrDefault(x => x is "--report" or "--check-local" or "--download-local" or "--apply-local");
if (operation is null) return 0;
var index = Array.IndexOf(args, operation);
var report = operation == "--report" ? args[index + 1] : args[index + 2];
var manager = new UpdateManager(operation == "--report" ? AppContext.BaseDirectory : args[index + 1]);
try
{
    var update = operation == "--report" ? null : await manager.CheckForUpdatesAsync();
    if (update is not null && operation is "--download-local" or "--apply-local") await manager.DownloadUpdatesAsync(update);
    using (var stream = File.Create(report))
    using (var writer = new Utf8JsonWriter(stream))
    {
        writer.WriteStartObject(); writer.WriteString("version", version); writer.WriteBoolean("installed", manager.IsInstalled);
        writer.WriteString("candidate", update?.TargetFullRelease.Version.ToString()); writer.WriteEndObject();
    }
    if (update is not null && operation == "--apply-local") manager.ApplyUpdatesAndRestart(update, ["--report", report]);
    return 0;
}
catch (Exception error)
{
    using var stream = File.Create(report);
    using var writer = new Utf8JsonWriter(stream);
    writer.WriteStartObject(); writer.WriteString("version", version); writer.WriteString("error_type", error.GetType().Name); writer.WriteEndObject();
    return 2;
}
internal sealed class FixtureMarker;
