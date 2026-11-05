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
