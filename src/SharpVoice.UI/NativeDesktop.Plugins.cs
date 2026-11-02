using SharpVoice.Platform.Windows;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    public Func<string, bool, CancellationToken, Task<EditorPluginStatus>>? PluginRequested { get; set; }
    private CancellationTokenSource? pluginCancellation;
    private long pluginGeneration;
    private nint pluginInstallButton, pluginRefreshButton, pluginProgress;
    private EditorPluginStatus? selectedPlugin;

    private void OpenPlugins()
    {
        StartToolsWindow("plugins", "Plugins");
        Win32.SetWindowTextW(toolsNotice, "Add SharpVoice voice coding and reviewed edits to your editor. Choose an editor to check its plugin.");
        foreach (var target in EditorPlugins.Targets) AddToolListItem(target.Name);
        ToolEdit("PluginEditor", "Editor Location", 104, 64, multiline: true, readOnly: true);
        ToolEdit("PluginBundled", "Included Plugin Version", 210, readOnly: true);
        ToolEdit("PluginInstalled", "Installed Plugin Version", 286, readOnly: true);
        ToolEdit("PluginStatus", "Status", 362, 128, multiline: true, readOnly: true);
        pluginProgress = ToolControl("msctls_progress32", "Plugin Operation Progress", 8, 288, 540, 520, 12);
        pluginInstallButton = ToolButton("Install", 4401, 288, 596, 230);
        pluginRefreshButton = ToolButton("Refresh", 4402, 538, 596, 270);
        ToolButton("Close", 4205, 640, 692, 168);
        Win32.SendMessageW(toolsList, 0x186, 0, 0);
        FinishToolsWindow(); RequestPlugin(install: false);
    }
    private void CancelPluginOperation()
    {
        ++pluginGeneration;
        var owner = pluginCancellation; pluginCancellation = null;
        owner?.Cancel(); // Completion disposes its own source, even after this window closes.
        if (disposed) owner?.Dispose(); // Shutdown drops posted completions.
        selectedPlugin = null;
        pluginInstallButton = pluginRefreshButton = pluginProgress = 0;
    }
    private void RequestPlugin(bool install)
    {
        if (toolsKind != "plugins" || toolsWindow == 0 || pluginCancellation is not null) return;
        var index = (int)Win32.SendMessageW(toolsList, 0x188, 0, 0);
        if (index < 0 || index >= EditorPlugins.Targets.Length) return;
        if (install && selectedPlugin?.CanInstall != true) return;
        selectedPlugin = null;
        Win32.SetWindowTextW(toolFields["PluginEditor"], "Checking…");
        Win32.SetWindowTextW(toolFields["PluginBundled"], "—");
        Win32.SetWindowTextW(toolFields["PluginInstalled"], "—");
        Win32.SetWindowTextW(toolFields["PluginStatus"], install ? "Installing Plugin…" : "Checking Plugin…");
        Win32.EnableWindow(toolsList, 0); Win32.EnableWindow(pluginInstallButton, 0); Win32.EnableWindow(pluginRefreshButton, 0);
        Win32.ShowWindow(pluginProgress, 5); Win32.SendMessageW(pluginProgress, 0x40A, 1, 30);
        var owner = new CancellationTokenSource(install ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(20));
        pluginCancellation = owner;
        var request = ++pluginGeneration; var id = EditorPlugins.Targets[index].Id;
        var token = owner.Token;
        var handler = PluginRequested;
        var task = Task.Run(() => handler is null ? Task.FromException<EditorPluginStatus>(new InvalidOperationException("Plugin management is unavailable in this build.")) : handler(id, install, token));
        _ = CompletePluginAsync(task, owner, request, id);
    }
    private async Task CompletePluginAsync(Task<EditorPluginStatus> task, CancellationTokenSource owner, long request, string id)
    {
        EditorPluginStatus? result = null; string? failure = null;
        try { result = await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { failure = "Operation canceled or timed out. Refresh to check the editor's current status."; }
        catch (Exception ex) { failure = ex is InvalidOperationException or InvalidDataException ? ex.Message : "Could not manage the plugin. Check the editor installation and Refresh."; }
        var canceled = owner.IsCancellationRequested;
        Post(() =>
        {
            if (request != pluginGeneration || !ReferenceEquals(pluginCancellation, owner) || toolsKind != "plugins") { owner.Dispose(); return; }
            pluginCancellation = null; owner.Dispose();
            Win32.ShowWindow(pluginProgress, 0); Win32.SendMessageW(pluginProgress, 0x40A, 0, 0);
            Win32.EnableWindow(toolsList, 1); Win32.EnableWindow(pluginRefreshButton, 1);
            if (result is not null && !canceled)
            {
                selectedPlugin = result;
                Win32.SetWindowTextW(toolFields["PluginEditor"], result.Executable ?? "Not Found (Standard Install Locations and PATH)");
                Win32.SetWindowTextW(toolFields["PluginBundled"], result.BundledVersion);
                Win32.SetWindowTextW(toolFields["PluginInstalled"], result.InstalledVersion ?? "Not Installed");
                var context = id == "antigravity" ? "\r\n\r\nExperimental: requires a VS Code-compatible Antigravity IDE. Google account sign-in is separate." : "";
                Win32.SetWindowTextW(toolFields["PluginStatus"], result.Message + context);
                Win32.SetWindowTextW(pluginInstallButton, result.Action);
                Win32.EnableWindow(pluginInstallButton, result.CanInstall ? 1 : 0);
            }
            else
            {
                Win32.SetWindowTextW(toolFields["PluginEditor"], "Refresh to Retry");
                Win32.SetWindowTextW(toolFields["PluginStatus"], failure ?? "Operation timed out. Refresh to check whether installation completed.");
            }
        });
    }
}
