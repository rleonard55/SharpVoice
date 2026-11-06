using System.Diagnostics;
using SharpVoice.Platform.Windows;

internal static class EditorPluginChecks
{
    public static async Task RealInstall(string executable, string root)
    {
        Directory.CreateDirectory(root);
        var plugins = new EditorPlugins(_ => executable, (start, ct) =>
        {
            start.ArgumentList.Add("--user-data-dir"); start.ArgumentList.Add(Path.Combine(root, "profile"));
            start.ArgumentList.Add("--extensions-dir"); start.ArgumentList.Add(Path.Combine(root, "extensions"));
            return EditorPlugins.RunAsync(start, ct);
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var after = await plugins.InstallAsync("vscode", timeout.Token);
        if (after.InstalledVersion != after.BundledVersion) throw new Exception("Real plugin install did not verify.");
        // Repeat proves the local VSIX installer also accepts an explicit reinstall.
        after = await plugins.InstallAsync("vscode", timeout.Token);
        Console.WriteLine("PASS production bundled plugin installer and reinstall in isolated VS Code profile: " + after.InstalledVersion);
    }
    public static async Task Install()
    {
        using var fixture = new Fixture();
        string? installedVersion = null; var calls = new List<ProcessStartInfo>();
        var plugins = new EditorPlugins(_ => fixture.Executable, (start, ct) =>
        {
            ct.ThrowIfCancellationRequested(); calls.Add(start);
            if (start.UseShellExecute || !start.CreateNoWindow || start.Environment["ELECTRON_RUN_AS_NODE"] != "1" || start.Environment.ContainsKey("VSCODE_IPC_HOOK_CLI")) throw new Exception("Unsafe editor CLI launch.");
            if (start.ArgumentList[0] != fixture.Cli) throw new Exception("Versioned editor CLI was not resolved.");
            if (start.ArgumentList.Contains("--install-extension"))
            {
                var package = start.ArgumentList[start.ArgumentList.IndexOf("--install-extension") + 1];
                using var zip = System.IO.Compression.ZipFile.OpenRead(package);
                using var json = System.Text.Json.JsonDocument.Parse(zip.GetEntry("extension/package.json")!.Open());
                if (json.RootElement.GetProperty("name").GetString() != "sharpvoice-vscode" || json.RootElement.GetProperty("publisher").GetString() != "sharpvoice") throw new Exception("Wrong bundled plugin.");
                if (start.ArgumentList.Contains("--force")) throw new Exception("Forced installation can downgrade the plugin.");
                installedVersion = json.RootElement.GetProperty("version").GetString();
                return Task.FromResult(new EditorPluginCommandResult(0, "Installed"));
            }
            if (!start.ArgumentList.Contains("--list-extensions") || !start.ArgumentList.Contains("--show-versions")) throw new Exception("Status did not query the editor.");
            return Task.FromResult(new EditorPluginCommandResult(0, installedVersion is not null ? $"unrelated.extension@9.0\nsharpvoice.sharpvoice-vscode@{installedVersion}\n" : "unrelated.extension@9.0\n"));
        });
        var before = await plugins.GetStatusAsync("vscode", default);
        if (before.Executable != fixture.Executable || before.InstalledVersion is not null || !before.CanInstall || before.Action != "Install") throw new Exception("Detected editor did not offer installation.");
        var after = await plugins.InstallAsync("vscode", default);
        if (after.InstalledVersion != after.BundledVersion || after.CanInstall != true || after.Action != "Reinstall" || !after.Message.Contains("Reload")) throw new Exception("Verified install did not report reload guidance.");
        if (calls.Count != 4) throw new Exception("Install did not recheck status before and after its mutation.");
    }

    public static async Task Failures()
    {
        using var fixture = new Fixture();
        var missing = new EditorPlugins(_ => null, (_, _) => throw new Exception("Launched missing editor."));
        if ((await missing.GetStatusAsync("vscode", default)).CanInstall) throw new Exception("Missing editor offered install.");
        try { await missing.InstallAsync("vscode", default); throw new Exception("Installed into missing editor."); } catch (InvalidOperationException) { }
        try { await missing.GetStatusAsync("untrusted", default); throw new Exception("Accepted unknown plugin target."); } catch (ArgumentException) { }
        var newer = new EditorPlugins(_ => fixture.Executable, (_, _) => Task.FromResult(new EditorPluginCommandResult(0, "sharpvoice.sharpvoice-vscode@99.0.0")));
        if ((await newer.GetStatusAsync("vscode", default)).CanInstall) throw new Exception("Newer plugin allowed downgrade.");
        try { await newer.InstallAsync("vscode", default); throw new Exception("Downgraded newer plugin."); } catch (InvalidOperationException) { }
        var failed = new EditorPlugins(_ => fixture.Executable, (_, _) => Task.FromResult(new EditorPluginCommandResult(1, "")));
        try { await failed.GetStatusAsync("vscode", default); throw new Exception("Status query failure hidden."); } catch (InvalidOperationException) { }
        var installFailed = new EditorPlugins(_ => fixture.Executable, (start, _) => Task.FromResult(new EditorPluginCommandResult(start.ArgumentList.Contains("--install-extension") ? 1 : 0, "")));
        try { await installFailed.InstallAsync("vscode", default); throw new Exception("Failed install reported success."); } catch (InvalidOperationException) { }
        var unverified = new EditorPlugins(_ => fixture.Executable, (_, _) => Task.FromResult(new EditorPluginCommandResult(0, "")));
        try { await unverified.InstallAsync("vscode", default); throw new Exception("Unverified install reported success."); } catch (InvalidOperationException) { }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await newer.InstallAsync("vscode", cancellation.Token); throw new Exception("Ignored cancellation."); } catch (OperationCanceledException) { }
        if (!EditorPlugins.IsSupportedEditor("Code") || !EditorPlugins.IsSupportedEditor("Antigravity") || EditorPlugins.IsSupportedEditor("notepad")) throw new Exception("Editor process recognition failed.");
    }

    public static async Task AutomaticUpdates()
    {
        using var vscode = new Fixture(); using var insiders = new Fixture(); using var antigravity = new Fixture();
        var paths = new Dictionary<string, string> { ["vscode"] = vscode.Executable, ["insiders"] = insiders.Executable, ["antigravity"] = antigravity.Executable };
        var versions = new Dictionary<string, string?> { ["vscode"] = "0.0.1", ["insiders"] = EditorPlugins.BundledVersion, ["antigravity"] = "99.0.0" };
        var installs = new List<string>(); string? failure = null;
        var plugins = new EditorPlugins(id => paths.GetValueOrDefault(id), (start, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var id = paths.Single(x => x.Value == start.FileName).Key;
            if (id == failure) throw new InvalidOperationException("Synthetic editor is locked.");
            if (start.ArgumentList.Contains("--install-extension"))
            {
                if (start.ArgumentList.Contains("--force")) throw new Exception("Automatic update forced a downgrade.");
                installs.Add(id); versions[id] = EditorPlugins.BundledVersion;
                return Task.FromResult(new EditorPluginCommandResult(0, "Installed"));
            }
            return Task.FromResult(new EditorPluginCommandResult(0, versions[id] is { } version ? "sharpvoice.sharpvoice-vscode@" + version : ""));
        });
        var result = await plugins.UpdateInstalledAsync(default);
        if (!installs.SequenceEqual(new[] { "vscode" }) || result.Any(x => x.Error is not null)) throw new Exception("Automatic update failed version eligibility.");
        installs.Clear(); versions["insiders"] = null; paths.Remove("antigravity");
        result = await plugins.UpdateInstalledAsync(default);
        if (installs.Count != 0 || result.Any(x => x.Error is not null)) throw new Exception("Missing plugin/editor was installed or equal version reinstalled.");
        paths["antigravity"] = antigravity.Executable; versions["antigravity"] = "0.0.1"; failure = "vscode";
        result = await plugins.UpdateInstalledAsync(default);
        if (result.Count(x => x.Error is not null) != 1 || !installs.SequenceEqual(new[] { "antigravity" })) throw new Exception("One failed editor blocked another update.");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await plugins.UpdateInstalledAsync(canceled.Token); throw new Exception("Automatic update ignored cancellation."); } catch (OperationCanceledException) { }
    }

    public static async Task AutomaticUpdateSerialization()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var installed = "0.0.1"; var installs = 0;
        var plugins = new EditorPlugins(id => id == "vscode" ? fixture.Executable : null, async (start, ct) =>
        {
            if (start.ArgumentList.Contains("--install-extension"))
            {
                installs++; entered.TrySetResult(); await release.Task.WaitAsync(ct);
                installed = EditorPlugins.BundledVersion;
                return new EditorPluginCommandResult(0, "Installed");
            }
            return new EditorPluginCommandResult(0, "sharpvoice.sharpvoice-vscode@" + installed);
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var manual = plugins.InstallAsync("vscode", timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        var automatic = plugins.UpdateInstalledAsync(timeout.Token);
        release.SetResult();
        await manual; var results = await automatic;
        if (installs != 1 || results.Any(x => x.Updated || x.Error is not null)) throw new Exception("Manual and automatic installers raced or reinstalled a current package.");
    }

    public static async Task AutomaticUpdateCancellation()
    {
        using var fixture = new Fixture();
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0; var installed = "0.0.1";
        var plugins = new EditorPlugins(id => id == "vscode" ? fixture.Executable : null, async (start, ct) =>
        {
            if (start.ArgumentList.Contains("--install-extension"))
            {
                if (++attempts == 1) { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); }
                installed = EditorPlugins.BundledVersion;
            }
            return new EditorPluginCommandResult(0, "sharpvoice.sharpvoice-vscode@" + installed);
        });
        var pending = plugins.UpdateInstalledAsync(cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancel.Cancel();
        try { await pending; throw new Exception("Canceled active plugin installation completed successfully."); } catch (OperationCanceledException) { }
        using var retryLimit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var retry = await plugins.UpdateInstalledAsync(retryLimit.Token);
        if (attempts != 2 || retry.Count(x => x.Updated) != 1 || retry.Any(x => x.Error is not null)) throw new Exception("Canceled installation held its gate or prevented retry.");
    }

    public static async Task AutomaticUpdateStamp()
    {
        using var fixture = new Fixture();
        var root = Path.Combine(Path.GetTempPath(), "SharpVoice-plugin-maintenance-" + Guid.NewGuid().ToString("N"));
        var stamp = Path.Combine(root, "plugin.version"); var installed = "0.0.1"; var calls = 0; var fail = false;
        var messages = new List<string>();
        var plugins = new EditorPlugins(id => id == "vscode" ? fixture.Executable : null, (start, ct) =>
        {
            calls++; ct.ThrowIfCancellationRequested();
            if (fail) throw new InvalidOperationException("Synthetic failure.");
            if (start.ArgumentList.Contains("--install-extension")) installed = EditorPlugins.BundledVersion;
            return Task.FromResult(new EditorPluginCommandResult(0, "sharpvoice.sharpvoice-vscode@" + installed));
        });
        try
        {
            using var first = new SharpVoice.App.EditorPluginMaintenance(plugins, stamp, "1.0.0");
            await first.RunAsync(messages.Add, default);
            if (!File.Exists(stamp) || installed != EditorPlugins.BundledVersion || !messages.Last().Contains("Reload")) throw new Exception("First app-version maintenance did not verify and stamp its update.");
            var before = calls; await first.RunAsync(messages.Add, default);
            if (calls != before) throw new Exception("Unchanged app/package version queried editors again.");
            using var next = new SharpVoice.App.EditorPluginMaintenance(plugins, stamp, "1.0.1");
            fail = true; await next.RunAsync(messages.Add, default);
            if (!File.ReadAllText(stamp).StartsWith("1.0.0|") || !messages.Last().Contains("attention")) throw new Exception("Failed maintenance advanced its stamp or hid recovery guidance.");
            fail = false; await next.RunAsync(messages.Add, default);
            if (!File.ReadAllText(stamp).StartsWith("1.0.1|")) throw new Exception("Failed maintenance could not retry.");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            using var later = new SharpVoice.App.EditorPluginMaintenance(plugins, stamp, "1.0.2");
            before = calls; await later.RunAsync(messages.Add, canceled.Token);
            if (calls != before || !File.ReadAllText(stamp).StartsWith("1.0.1|")) throw new Exception("Canceled maintenance changed state.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "SharpVoice Plugins " + Guid.NewGuid().ToString("N"));
        public string Executable { get; }
        public string Cli { get; }
        public Fixture()
        {
            Executable = Path.Combine(root, "Code.exe"); Cli = Path.Combine(root, "abc123", "resources", "app", "out", "cli.js");
            Directory.CreateDirectory(Path.GetDirectoryName(Cli)!); File.WriteAllText(Executable, "fixture"); File.WriteAllText(Cli, "fixture");
            if (EditorPlugins.ResolveCli(Executable) != Cli) throw new Exception("Versioned editor installation was not detected.");
        }
        public void Dispose() => Directory.Delete(root, true);
    }
}
