using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    [LibraryImport("user32.dll", EntryPoint = "IsWindowEnabled")] private static partial int UpdateControlEnabled(nint window);
    private static unsafe void CheckUpdates(NativeDesktop desktop, Action<ThemeColors> changeTheme, string? output)
    {
        var window = desktop.SettingsHandle;
        Win32.ShowWindow(window, 4);
        var tabs = Win32.GetDlgItem(window, 1014);
        Win32.SendMessageW(tabs, 0x130C, 5, 0);
        var notification = new Win32.Notification { Window = tabs, Id = 1014, Code = -551 };
        Win32.SendMessageW(window, 0x4E, 1014, (nint)(&notification));
        Win32.SendMessageW(window, 0x111, 122, 0);
        var tool = desktop.ToolsWindow;
        if (tool == 0) throw new Exception("General has no Updates interface.");
        var enabled = desktop.ToolField("AutomaticUpdates"); var action = desktop.ToolField("UpdateAction");
        if (Win32.SendMessageW(enabled, 0x146, 0, 0) != 2 || Win32.SendMessageW(action, 0x146, 0, 0) != 3)
            throw new Exception("Enablement and update action must be separate controls.");
        if (Win32.IsWindowVisible(desktop.ToolField("UpdateDetails")) != 0) throw new Exception("Update guidance must start collapsed.");
        Win32.SendMessageW(tool, 0x111, 4705, 0);
        if (Win32.IsWindowVisible(desktop.ToolField("UpdateDetails")) == 0) throw new Exception("Update details are not accessible on demand.");
        Win32.SendMessageW(tool, 0x111, 4705, 0);
        var installButton = Win32.GetDlgItem(tool, 4704);
        foreach (var state in new[] {
            new AppUpdateView("0.1.0", "Ready to check."),
            new AppUpdateView("0.1.0", "Update available.", "0.2.0"),
            new AppUpdateView("0.1.0", "Update ready.", "0.2.0", "0.2.0") })
        {
            desktop.SetUpdateView(state);
            if (UpdateControlEnabled(installButton) == 0) throw new Exception("Install and Restart must work before checking or downloading an update.");
        }
        desktop.SetUpdateView(new("0.1.0", "Downloading…", "0.2.0", Busy: true));
        if (UpdateControlEnabled(installButton) != 0) throw new Exception("Busy updates must block repeated install clicks.");
        desktop.SetUpdateView(new("Development", "Unsupported.", Supported: false));
        if (UpdateControlEnabled(installButton) != 0) throw new Exception("Development builds must not offer release installation.");
        var check = 0; var download = 0; var install = 0;
        desktop.UpdateCheckRequested = () => check++;
        desktop.UpdateDownloadRequested = () => download++;
        desktop.UpdateInstallRequested = () => install++;
        desktop.SetUpdateView(new("0.1.0", "Version 0.2.0 is available.", "0.2.0"));
        Win32.SendMessageW(tool, 0x111, 4702, 0); Win32.SendMessageW(tool, 0x111, 4703, 0);
        desktop.SetUpdateView(new("0.1.0", "Update downloaded and verified.", "0.2.0", "0.2.0"));
        Win32.SendMessageW(tool, 0x111, 4704, 0);
        if (check != 1 || download != 1 || install != 1) throw new Exception("Manual update controls did not route independently.");
        var previous = desktop.ConfigChanged; AppConfig? saved = null; desktop.ConfigChanged = value => saved = value;
        Win32.SendMessageW(enabled, 0x14E, 1, 0); Win32.SendMessageW(action, 0x14E, 0, 0);
        Win32.SendMessageW(tool, 0x111, 4701, 0); desktop.ConfigChanged = previous;
        if (saved is null || saved.AutomaticUpdates || saved.UpdateAction != "notify") throw new Exception("Off must preserve the independent Notify preference.");
        Win32.SendMessageW(window, 0x111, 122, 0);
        if (Win32.SendMessageW(desktop.ToolField("AutomaticUpdates"), 0x147, 0, 0) != 1 || Win32.SendMessageW(desktop.ToolField("UpdateAction"), 0x147, 0, 0) != 0)
            throw new Exception("Saved update choices were not recalled.");
        foreach (var palette in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
        {
            changeTheme(palette); Win32.SendMessageW(window, 0x1A, 0, 0); Pump();
            RenderHelp(desktop.ToolsWindow, output is null ? null : output + (palette.Dark ? "-updates-dark.bmp" : "-updates-light.bmp"), 60);
        }
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4205, 0);
        desktop.UpdateCheckRequested = desktop.UpdateDownloadRequested = desktop.UpdateInstallRequested = null;
        Console.WriteLine("PASS independent update controls, retained Off/action settings, manual actions and both themes.");
    }
}
