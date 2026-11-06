using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SharpVoice.Platform.Windows;

public sealed record EditorPluginTarget(string Id, string Name);
public sealed record EditorPluginStatus(string Name, string? Executable, string? InstalledVersion, string BundledVersion, string Message, bool CanInstall, string Action);
public sealed record EditorPluginUpdateResult(string Id, string Name, bool Updated, string? Error);
public sealed record EditorPluginCommandResult(int ExitCode, string Output);

public sealed class EditorPlugins(Func<string, string?>? locate = null, Func<ProcessStartInfo, CancellationToken, Task<EditorPluginCommandResult>>? run = null)
{
    private readonly SemaphoreSlim installationGate = new(1, 1);
    private const string ExtensionId = "sharpvoice.sharpvoice-vscode";
    private readonly Func<string, string?> find = locate ?? FindEditor;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<EditorPluginCommandResult>> execute = run ?? RunAsync;
    public static readonly EditorPluginTarget[] Targets = [new("vscode", "Visual Studio Code"), new("insiders", "VS Code Insiders"), new("antigravity", "Antigravity IDE (Experimental)")];
    public async Task<EditorPluginStatus> GetStatusAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var target = Targets.FirstOrDefault(t => t.Id == id) ?? throw new ArgumentException("Unknown editor plugin.", nameof(id));
        var version = BundledVersion;
        var path = find(id);
        if (path is null || !File.Exists(path)) return new(target.Name, null, null, version, "Editor Not Found · Install the desktop editor, then Refresh.", false, "Install");
        var result = await execute(Start(path, "--list-extensions", "--show-versions"), ct).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new InvalidOperationException("Could not check plugins. Update the editor or close its installer, then Refresh.");
        var installed = result.Output.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith(ExtensionId + "@", StringComparison.OrdinalIgnoreCase))?[(ExtensionId.Length + 1)..];
        if (installed is not null && !Version.TryParse(installed, out _)) throw new InvalidOperationException("The installed plugin version could not be read. Manage it in the editor's Extensions view.");
        var newer = installed is not null && Version.Parse(installed) > Version.Parse(version);
        var action = installed is null ? "Install" : installed == version ? "Reinstall" : "Update";
        return new(target.Name, path, installed, version, newer ? "Newer Version Installed · Keep your current plugin." : installed is null ? "Ready to Install" : installed == version ? "Up to Date" : "Update Available", !newer, newer ? "Newer Installed" : action);
    }
    public async Task<EditorPluginStatus> InstallAsync(string id, CancellationToken ct)
    {
        await installationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var before = await GetStatusAsync(id, ct).ConfigureAwait(false);
            if (!before.CanInstall || before.Executable is null) throw new InvalidOperationException(before.Message);
            return await InstallVerifiedAsync(id, before, ct).ConfigureAwait(false);
        }
        finally { installationGate.Release(); }
    }
    public async Task<IReadOnlyList<EditorPluginUpdateResult>> UpdateInstalledAsync(CancellationToken ct)
    {
        var results = new List<EditorPluginUpdateResult>();
        foreach (var target in Targets)
        {
            ct.ThrowIfCancellationRequested();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            try
            {
                await installationGate.WaitAsync(deadline.Token).ConfigureAwait(false);
                try
                {
                    // Read inside the same gate as manual installation. Never install a missing
                    // plugin, reinstall an equal version, or downgrade a newer editor version.
                    var before = await GetStatusAsync(target.Id, deadline.Token).ConfigureAwait(false);
                    var outdated = before.InstalledVersion is not null && before.Executable is not null
                        && Version.Parse(before.InstalledVersion) < Version.Parse(before.BundledVersion);
                    if (outdated) await InstallVerifiedAsync(target.Id, before, deadline.Token).ConfigureAwait(false);
                    results.Add(new(target.Id, target.Name, outdated, null));
                }
                finally { installationGate.Release(); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ct.ThrowIfCancellationRequested();
                // One unavailable/locked editor must not stop other installed editors updating.
                results.Add(new(target.Id, target.Name, false, "Automatic update failed. Open Plugins and Refresh to retry."));
            }
        }
        return results;
    }
    private async Task<EditorPluginStatus> InstallVerifiedAsync(string id, EditorPluginStatus before, CancellationToken ct)
    {
        // Unique, app-owned temporary package; no user-picked downloads and no extension-directory editing.
        var directory = Path.Combine(Path.GetTempPath(), "SharpVoice-plugin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var package = Path.Combine(directory, "sharpvoice-vscode.vsix");
            using (var source = OpenPackage())
            await using (var output = File.Create(package)) await source.CopyToAsync(output, ct).ConfigureAwait(false);
            var result = await execute(Start(before.Executable!, "--install-extension", package), ct).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new InvalidOperationException("Plugin installation failed. Close the editor's installer, check its Extensions view and retry.");
            var after = await GetStatusAsync(id, ct).ConfigureAwait(false);
            if (after.InstalledVersion != after.BundledVersion) throw new InvalidOperationException("Installation could not be verified. Check the editor's Extensions view, then Refresh.");
            return after with { Message = "Installed · Reload the editor to activate SharpVoice." };
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    private static Stream OpenPackage() => typeof(EditorPlugins).Assembly.GetManifestResourceStream("SharpVoice.EditorPlugin.vsix") ?? throw new InvalidOperationException("This build is missing its bundled editor plugin.");
    public static string BundledVersion
    {
        get
        {
            using var source = OpenPackage(); using var zip = new System.IO.Compression.ZipArchive(source);
            using var document = JsonDocument.Parse(zip.GetEntry("extension/package.json")!.Open());
            var root = document.RootElement;
            if (root.GetProperty("publisher").GetString() != "sharpvoice" || root.GetProperty("name").GetString() != "sharpvoice-vscode") throw new InvalidDataException("Invalid bundled editor plugin.");
            return root.GetProperty("version").GetString()!;
        }
    }
    internal static string? ResolveCli(string executable)
    {
        var root = Path.GetDirectoryName(executable);
        if (root is null || !Directory.Exists(root)) return null;
        var direct = Path.Combine(root, "resources", "app", "out", "cli.js");
        if (File.Exists(direct)) return direct;
        // New VS Code releases keep resources in an immediate version/hash directory.
        return Directory.EnumerateDirectories(root).Select(d => Path.Combine(d, "resources", "app", "out", "cli.js"))
            .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }
    private static string? FindEditor(string id)
    {
        var folders = id switch { "vscode" => new[] { "Microsoft VS Code" }, "insiders" => ["Microsoft VS Code Insiders"], "antigravity" => ["Antigravity", "Antigravity IDE", "antigravity-ide"], _ => [] };
        var names = id switch { "vscode" => new[] { "Code.exe" }, "insiders" => ["Code - Insiders.exe"], "antigravity" => ["Antigravity.exe", "Antigravity IDE.exe", "antigravity-ide.exe"], _ => [] };
        var roots = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
        foreach (var root in roots.Where(x => !string.IsNullOrWhiteSpace(x)))
        foreach (var folder in folders)
        foreach (var name in names)
        {
            var path = Path.Combine(root, folder, name);
            if (File.Exists(path) && ResolveCli(path) is not null) return path;
        }
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var directory = entry.Trim(' ', '"'); if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)) continue;
            foreach (var root in new[] { directory, Path.GetDirectoryName(directory) ?? directory })
            foreach (var name in names) { var path = Path.Combine(root, name); if (File.Exists(path) && ResolveCli(path) is not null) return path; }
        }
        return null;
    }
    internal static ProcessStartInfo Start(string executable, params string[] arguments)
    {
        var cli = ResolveCli(executable) ?? throw new InvalidOperationException("The editor's extension installer is unavailable. Update or reinstall the desktop editor.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable)!, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var key in start.Environment.Keys.Where(x => x.StartsWith("VSCODE_", StringComparison.OrdinalIgnoreCase) || x.StartsWith("ELECTRON_", StringComparison.OrdinalIgnoreCase) || x is "NODE_OPTIONS" or "NODE_PATH").ToArray()) start.Environment.Remove(key);
        start.Environment["ELECTRON_RUN_AS_NODE"] = "1";
        start.ArgumentList.Add(cli);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
    internal static async Task<EditorPluginCommandResult> RunAsync(ProcessStartInfo start, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var child = new Process { StartInfo = start };
        try { if (!child.Start()) throw new InvalidOperationException("Could not start the editor's plugin installer."); }
        catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("Could not start the editor's plugin installer. Check its installation."); }
        async Task<string> Read(StreamReader reader)
        {
            try
            {
                var text = new StringBuilder(); var buffer = new char[4096]; int count;
                while ((count = await reader.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) != 0)
                {
                    if (text.Length + count > 131072) throw new InvalidDataException("The editor's plugin installer returned excessive output.");
                    text.Append(buffer, 0, count);
                }
                return text.ToString();
            }
            catch { deadline.Cancel(); throw; }
        }
        var output = Read(child.StandardOutput); var error = Read(child.StandardError);
        try
        {
            await Task.WhenAll(output, error, child.WaitForExitAsync(deadline.Token)).WaitAsync(deadline.Token).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return new(child.ExitCode, await output.ConfigureAwait(false));
        }
        catch
        {
            deadline.Cancel();
            try { if (!child.HasExited) child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch (Exception) { }
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The editor's plugin installer stopped or timed out. Refresh and retry.");
        }
    }
    public static bool IsSupportedEditor(string process) => process.ToLowerInvariant() is "code" or "code - insiders" or "antigravity" or "antigravity ide" or "antigravity-ide";
}
