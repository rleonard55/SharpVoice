using System.Collections.Concurrent;
using SharpVoice.Platform.Windows;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    private static unsafe void CheckPlugins(NativeDesktop desktop, Action<ThemeColors> changeTheme, string? output)
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        string Text(nint control)
        {
            var buffer = new char[4096]; fixed (char* value = buffer) return new string(buffer, 0, Win32.GetWindowTextW(control, value, buffer.Length));
        }
        var requests = new ConcurrentQueue<(string Id, bool Install, CancellationToken Token, TaskCompletionSource<EditorPluginStatus> Completion)>();
        desktop.PluginRequested = (id, install, ct) =>
        {
            var completion = new TaskCompletionSource<EditorPluginStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Enqueue((id, install, ct, completion)); return completion.Task;
        };
        (string Id, bool Install, CancellationToken Token, TaskCompletionSource<EditorPluginStatus> Completion) Next()
        {
            for (var retry = 0; retry < 40; retry++) { Pump(); if (requests.TryDequeue(out var result)) return result; }
            throw new Exception("Plugin operation did not start.");
        }
        nint Item(int id) => Win32.GetDlgItem(desktop.ToolsWindow, id);
        void Click(int id) => Win32.SendMessageW(desktop.ToolsWindow, 0x111, (nuint)id, Item(id));
        void Open() => Win32.SendMessageW(desktop.SettingsHandle, 0x111, 118, Win32.GetDlgItem(desktop.SettingsHandle, 118));
        Open(); var old = Next();
        Check(!old.Install && old.Id == "vscode", "Opening Plugins installed something or selected the wrong editor.");
        Check(Win32.SendMessageW(Item(4101), 0x18B, 0, 0) == 3, "Plugin manager is missing an editor.");
        Click(4205); Check(old.Token.IsCancellationRequested, "Closing Plugins did not cancel pending work.");
        Open(); var current = Next();
        old.Completion.SetResult(new("Old", null, null, "0.1.1", "Stale Completion", false, "Install")); Pump();
        Check(Text(desktop.ToolField("PluginStatus")) == "Checking Plugin…", "Closed plugin operation overwrote the reopened manager.");
        current.Completion.SetResult(new("VS Code", "C:\\Editors\\Code.exe", null, "0.1.1", "Ready to Install", true, "Install")); Pump();
        Check(Text(desktop.ToolField("PluginStatus")) == "Ready to Install" && Text(Item(4401)) == "Install", "Plugin status did not reach its controls.");
        Click(4401); Click(4401); var install = Next();
        Check(install.Install && requests.IsEmpty, "Duplicate install requests were accepted.");
        install.Completion.SetResult(new("VS Code", "C:\\Editors\\Code.exe", "0.1.1", "0.1.1", "Installed · Reload the editor to activate SharpVoice.", true, "Reinstall")); Pump();
        Check(Text(desktop.ToolField("PluginStatus")).Contains("Reload") && Text(Item(4401)) == "Reinstall", "Verified installation did not show completion guidance.");
        foreach (var palette in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
        {
            changeTheme(palette); Win32.SendMessageW(desktop.SettingsHandle, 0x1A, 0, 0); Pump();
            Check(RenderHelp(desktop.ToolsWindow, output is null ? null : output + (palette.Dark ? "-plugins-dark.bmp" : "-plugins-light.bmp"), 60) == palette.Background, "Plugin manager did not follow the OS theme.");
        }
        Win32.SendMessageW(Item(4101), 0x186, 2, 0); Win32.SendMessageW(desktop.ToolsWindow, 0x111, (1u << 16) | 4101, Item(4101));
        var antigravity = Next(); Check(antigravity.Id == "antigravity" && !antigravity.Install, "Antigravity selection did not check its own editor.");
        antigravity.Completion.SetResult(new("Antigravity", null, null, "0.1.1", "Editor Not Found", false, "Install")); Pump();
        Check(Text(desktop.ToolField("PluginStatus")).Contains("Experimental"), "Unverified Antigravity support was not labeled.");
        Click(4401); Pump(); Check(requests.IsEmpty, "Missing editor accepted install.");
        Click(4402); var failed = Next(); failed.Completion.SetException(new InvalidOperationException("Fixture installer failed.")); Pump();
        Check(Text(desktop.ToolField("PluginStatus")) == "Fixture installer failed.", "Installer failure was hidden.");
        Click(4205); desktop.PluginRequested = null;
        Console.WriteLine("PASS native Plugins status, explicit installation, duplicate guard, close cancellation, stale results, failures and OS themes.");
    }
}
