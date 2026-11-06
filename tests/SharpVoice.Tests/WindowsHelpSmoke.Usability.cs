using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    public static void RunUsability(string scenario)
    {
        var isolated = CreateDesktopW("SharpVoiceUsability" + Guid.NewGuid().ToString("N"), 0, 0, 0, 0x10000000, 0);
        if (isolated == 0) throw new Exception("Cannot create isolated usability desktop.");
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (SetThreadDesktop(isolated) == 0) throw new Exception("Cannot enter isolated usability desktop.");
                CheckUsability(scenario);
            }
            catch (Exception ex) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
        });
        thread.Start(); thread.Join(); CloseDesktop(isolated); failure?.Throw();
    }

    private static unsafe void CheckUsability(string scenario)
    {
        using var desktop = new NativeDesktop(new AppConfig());
        desktop.CanMonitorInput = () => false;
        desktop.Initialize(showSettings: false, integrateShell: false, audioFeedback: false);
        var window = desktop.SettingsHandle;
        Win32.ShowWindow(window, 4);
        if (scenario == "keyboard")
        {
            var applied = 0;
            desktop.ConfigChanged = _ => applied++;
            var button = Win32.GetDlgItem(window, 101);
            Win32.SendMessageW(button, 0x100, 13, 0);
            Win32.SendMessageW(button, 0x101, 13, 0);
            if (applied != 1) throw new Exception("Enter on Apply Settings did not activate exactly once.");
            Win32.SendMessageW(button, 0x100, 13, 0);
            if (applied != 1) throw new Exception("Hidden Settings accepted a keyboard action.");
            Win32.ShowWindow(window, 4);
            Win32.EnableWindow(button, 0);
            Win32.SendMessageW(button, 0x100, 32, 0);
            if (applied != 1) throw new Exception("Disabled Settings action accepted Space.");
            Win32.EnableWindow(button, 1);
            Win32.SendMessageW(button, 0x100, 13, (nint)(1L << 30));
            if (applied != 1) throw new Exception("Held Enter repeated Apply.");
            Win32.SendMessageW(button, 0x100, 32, 0);
            Win32.SendMessageW(button, 0x101, 32, 0);
            if (applied != 2) throw new Exception("Space on Apply Settings did not activate exactly once.");
        }
        else if (scenario == "notification")
        {
            Win32.SendMessageW(window, Win32.WmTray, 0, 0x405);
            if (Win32.SendMessageW(Win32.GetDlgItem(window, 1014), 0x130B, 0, 0) != 5)
                throw new Exception("Update notification displayed General without selecting its tab.");
            Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4205, 0);
        }
        else if (scenario == "discovery")
        {
            var result = new TaskCompletionSource<ConnectedTool[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken token = default;
            desktop.ConnectedDiscoveryRequested = (_, ct) => { token = ct; started.SetResult(); return result.Task; };
            Win32.SendMessageW(window, 0x111, 120, 0);
            var path = desktop.ToolField("ConnectedPath");
            Win32.SetWindowTextW(path, @"C:\fixture\codex-a.exe");
            Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4471, 0);
            if (!started.Task.Wait(5000)) throw new Exception("Discovery did not start.");
            Win32.SetWindowTextW(path, @"C:\fixture\codex-b.exe");
            if (!token.IsCancellationRequested) throw new Exception("Changing executable did not cancel its in-flight discovery.");
            result.SetResult([ConnectedChecks.Send()]);
            for (var i = 0; i < 10; i++) Pump();
            if (Win32.SendMessageW(Win32.GetDlgItem(desktop.ToolsWindow, 4101), 0x18B, 0, 0) != 0)
                throw new Exception("Stale discovery populated a different executable's catalog.");
            desktop.ConnectedDiscoveryRequested = (_, _) => Task.FromResult(new[] { ConnectedChecks.Send() });
            Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4471, 0);
            for (var i = 0; i < 40 && Win32.SendMessageW(Win32.GetDlgItem(desktop.ToolsWindow, 4101), 0x18B, 0, 0) == 0; i++) Pump();
            if (Win32.SendMessageW(Win32.GetDlgItem(desktop.ToolsWindow, 4101), 0x18B, 0, 0) != 1) throw new Exception("Discovery could not retry for the new executable.");
            Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4476, 0);
            Win32.SetWindowTextW(path, @"C:\fixture\codex-c.exe");
            if (Win32.SendMessageW(Win32.GetDlgItem(desktop.ToolsWindow, 4101), 0x18B, 0, 0) != 0) throw new Exception("Changing executable retained the old catalog.");
            AppConfig? saved = null; desktop.ConfigChanged = next => saved = next;
            Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4474, 0);
            if (saved is null || saved.ConnectedTools.Length != 0) throw new Exception("Permissions from a different executable were saved.");
        }
        else throw new ArgumentException("Unknown usability scenario.");
        Console.WriteLine("PASS native usability: " + scenario);
    }
}
