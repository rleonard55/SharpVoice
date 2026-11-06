using SharpVoice.Platform.Windows;

namespace SharpVoice.App;

/// <summary>Synchronizes existing editor plugins once per installed app/package version.</summary>
internal sealed class EditorPluginMaintenance(EditorPlugins plugins, string stampPath, string appVersion) : IDisposable
{
    private readonly CancellationTokenSource shutdown = new();
    internal Task Completion { get; private set; } = Task.CompletedTask;
    private bool started;

    internal void Start(Action<string> report)
    {
        if (started) return;
        started = true;
        Completion = Task.Run(() => RunAsync(report, shutdown.Token));
    }

    internal async Task RunAsync(Action<string> report, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var version = appVersion + "|" + EditorPlugins.BundledVersion;
            if (File.Exists(stampPath) && await File.ReadAllTextAsync(stampPath, ct).ConfigureAwait(false) == version) return;
            report("Checking installed IDE plugins after the SharpVoice update…");
            var results = await plugins.UpdateInstalledAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var failed = results.Where(x => x.Error is not null).Select(x => x.Name).ToArray();
            var updated = results.Where(x => x.Updated).Select(x => x.Name).ToArray();
            if (failed.Length != 0)
            {
                report("Plugin update needs attention: " + string.Join(", ", failed) + ". Open Plugins to retry."
                    + (updated.Length == 0 ? "" : " Other plugins updated; reload those editors."));
                return; // No stamp: retry on the next launch; completed editors won't reinstall.
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(stampPath))!);
            var temporary = stampPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, version, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, stampPath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            report(updated.Length == 0 ? "Installed IDE plugins are up to date. Missing plugins can be installed below."
                : "Updated: " + string.Join(", ", updated) + ". Reload those editors to activate the plugins.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            report("Automatic plugin update could not finish. Open Plugins to check or retry; SharpVoice remains available.");
        }
    }

    public void Dispose()
    {
        shutdown.Cancel();
        try { Completion.GetAwaiter().GetResult(); }
        finally { shutdown.Dispose(); }
    }
}
