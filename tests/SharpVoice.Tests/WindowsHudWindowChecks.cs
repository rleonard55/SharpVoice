using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHudWindowChecks
{
    internal static void Run()
    {
        var isolated = CreateDesktopW("SharpVoiceHudCheck" + Guid.NewGuid().ToString("N"), 0, 0, 0, 0x10000000, 0);
        if (isolated == 0) throw new Exception("Cannot create isolated HUD desktop.");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (SetThreadDesktop(isolated) == 0) throw new Exception("Cannot enter isolated HUD desktop.");
                CheckWindow();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.Start(); thread.Join(); CloseDesktop(isolated);
        if (failure is not null) throw failure;
        Console.WriteLine("PASS native HUD raises above topmost windows on start/status/repeat without changing focus, size, position or click-through behavior.");
    }

    private static void CheckWindow()
    {
        using var desktop = new NativeDesktop(new AppConfig());
        desktop.Initialize(showSettings: false, integrateShell: false, audioFeedback: false);
        var hud = FindWindowW("SharpVoice.Native", "SharpVoice HUD");
        Check(hud != 0, "HUD window was not created.");
        var target = Win32.CreateWindowExW(0, "STATIC", "Synthetic typing app", 0x10CF0000, 10, 10, 520, 400, 0, 0, Win32.GetModuleHandleW(null), 0);
        Check(target != 0, "Cannot create isolated typing window.");
        SetActiveWindow(target);
        var input = Win32.CreateWindowExW(0, "EDIT", "Synthetic typing target", 0x50010000, 40, 100, 200, 30, target, 0, Win32.GetModuleHandleW(null), 0);
        SetFocus(input);
        Check(GetFocus() == input, "Cannot establish isolated editing focus.");
        var blocker = Win32.CreateWindowExW(0x08000088, "STATIC", "HUD test overlay", 0x90000000, 0, 0, 520, 100, 0, 0, Win32.GetModuleHandleW(null), 0);
        Check(blocker != 0, "Cannot create competing topmost window.");
        try
        {
            // Creating a visible competing window can focus it on this desktop.
            // Establish the user's editing target after all fixture windows exist.
            SetActiveWindow(target);
            SetFocus(input);
            Check(GetFocus() == input, "Cannot restore isolated editing focus after overlay setup.");
            Win32.GetWindowRect(hud, out var before);
            var foreground = Win32.GetForegroundWindow();
            var attempt = 0;
            foreach (var show in new Action[] { () => desktop.SetActive(true), () => desktop.SetStatus("Transcribing", showHud: true), () => desktop.SetActive(true) })
            {
                // Also simulate loss of the creation-time TOPMOST state.
                Check(SetWindowPos(hud, -2, 0, 0, 0, 0, 0x13) != 0, "Cannot lower test HUD.");
                Check(SetWindowPos(blocker, -1, 0, 0, 0, 0, 0x53) != 0, "Cannot raise competing window.");
                Check(GetFocus() == input && Win32.GetForegroundWindow() == foreground, "Overlay test setup changed editing focus before the HUD was shown.");
                show();
                Check(Win32.IsWindowVisible(hud) != 0, "Recording/status did not show the HUD.");
                Check((GetWindowLongPtrW(hud, -20) & 0x080000A8) == 0x080000A8, "Showing the pill failed to restore TOPMOST, NOACTIVATE, TOOLWINDOW and click-through styles.");
                var first = GetTopWindow(0);
                while (first != 0 && first != hud && first != blocker) first = GetWindow(first, 2);
                Check(first == hud, "Another topmost window still covers the newly shown recording pill.");
                Check(GetFocus() == input && Win32.GetForegroundWindow() == foreground, $"Showing the pill stole editing focus (attempt {attempt}).");
                attempt++;
                Win32.GetWindowRect(hud, out var after);
                Check(before.Left == after.Left && before.Top == after.Top && before.Right == after.Right && before.Bottom == after.Bottom, "Showing the pill changed its size or position.");
            }
            desktop.SetActive(false);
            Win32.SendMessageW(hud, 0x113, 2, 0);
            Check(Win32.IsWindowVisible(hud) == 0, "The finished pill no longer dismisses.");
        }
        finally { Win32.DestroyWindow(blocker); Win32.DestroyWindow(target); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint CreateDesktopW(string name, nint device, nint mode, uint flags, uint access, nint security);
    [LibraryImport("user32.dll")] private static partial int SetThreadDesktop(nint desktop);
    [LibraryImport("user32.dll")] private static partial int CloseDesktop(nint desktop);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint FindWindowW(string name, string title);
    [LibraryImport("user32.dll")] private static partial nint SetActiveWindow(nint window);
    [LibraryImport("user32.dll")] private static partial nint SetFocus(nint window);
    [LibraryImport("user32.dll")] private static partial nint GetFocus();
    [LibraryImport("user32.dll")] private static partial nint GetTopWindow(nint window);
    [LibraryImport("user32.dll")] private static partial nint GetWindow(nint window, uint command);
    [LibraryImport("user32.dll")] private static partial nint GetWindowLongPtrW(nint window, int index);
    [LibraryImport("user32.dll")] private static partial int SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
