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
        Console.WriteLine("PASS native draggable HUD opacity, placement persistence, topmost/focus behavior and fast paste dismissal.");
    }

    private static void CheckWindow()
    {
        Win32.SystemParametersInfoW(0x30, 0, out var work, 0);
        var stored = PillLayout.Clamp(new(work.Left + 20, work.Top + 20), work.Left, work.Top, work.Right, work.Bottom);
        using var desktop = new NativeDesktop(RoundTrip(new AppConfig { PillPosition = stored }));
        desktop.Initialize(showSettings: false, integrateShell: false, audioFeedback: false);
        var hud = FindWindowW("SharpVoice.Native", "SharpVoice HUD");
        Check(hud != 0, "HUD window was not created.");
        Check(GetLayeredWindowAttributes(hud, out _, out var opacity, out _) != 0 && opacity == 217, "The pill should use about 85% opacity.");
        Check(Win32.SendMessageW(hud, 0x84, 0, 0) == 2, "The pill must expose a caption drag target.");
        Check(Win32.SendMessageW(hud, 0x21, 0, 0) == 3, "Mouse dragging must not activate the pill.");
        Win32.GetWindowRect(hud, out var restored);
        Check(restored.Left == stored.X && restored.Top == stored.Y, "Launch did not restore the saved location.");
        var saves = 0;
        PillPosition? saved = null;
        desktop.PillPositionChanged = position => { saves++; saved = RoundTrip(new AppConfig { PillPosition = position }).PillPosition; };
        desktop.ConfigChanged = _ => throw new Exception("Dragging must not apply recording/provider settings.");
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
            foreach (var show in new Action[] { () => desktop.SetActive(true, target), () => desktop.SetStatus("Transcribing", showHud: true), () => desktop.SetActive(true, blocker) })
            {
                // Also simulate loss of the creation-time TOPMOST state.
                Check(SetWindowPos(hud, -2, 0, 0, 0, 0, 0x13) != 0, "Cannot lower test HUD.");
                Check(SetWindowPos(blocker, -1, 0, 0, 0, 0, 0x53) != 0, "Cannot raise competing window.");
                Check(GetFocus() == input && Win32.GetForegroundWindow() == foreground, "Overlay test setup changed editing focus before the HUD was shown.");
                show();
                Check(Win32.IsWindowVisible(hud) != 0, "Recording/status did not show the HUD.");
                Check((GetWindowLongPtrW(hud, -20) & 0x08000088) == 0x08000088 && (GetWindowLongPtrW(hud, -20) & 0x20) == 0, "The pill must stay TOPMOST/NOACTIVATE and accept dragging.");
                var first = GetTopWindow(0);
                while (first != 0 && first != hud && first != blocker) first = GetWindow(first, 2);
                Check(first == hud, "Another topmost window still covers the newly shown recording pill.");
                Check(GetFocus() == input && Win32.GetForegroundWindow() == foreground, $"Showing the pill stole editing focus (attempt {attempt}).");
                attempt++;
                Win32.GetWindowRect(hud, out var after);
                Check(before.Left == after.Left && before.Top == after.Top && before.Right == after.Right && before.Bottom == after.Bottom, "Showing the pill changed its size or position.");
            }
            Win32.SendMessageW(hud, 0x231, 0, 0);
            var moved = PillLayout.Clamp(new(stored.X + 100, stored.Y + 100), work.Left, work.Top, work.Right, work.Bottom);
            Check(SetWindowPos(hud, -1, moved.X, moved.Y, 0, 0, 0x11) != 0, "Cannot move the test pill.");
            Win32.SendMessageW(hud, 0x113, 2, 0);
            Check(saves == 0 && Win32.IsWindowVisible(hud) != 0, "Moving must defer saving and dismissal until release.");
            Win32.SendMessageW(hud, 0x232, 0, 0);
            Check(saves == 1 && saved == moved && GetFocus() == input, "Drag completion must save once and retain editing focus.");
            desktop.PlayInsertionSuccess(); desktop.SetActive(false);
            Check(!desktop.UpdateRestartBlocked, "Idle completion should not block update restart.");
            Win32.SendMessageW(hud, 0x231, 0, 0);
            Check(desktop.UpdateRestartBlocked, "Dragging must block update restart.");
            var held = System.Diagnostics.Stopwatch.StartNew();
            while (held.ElapsedMilliseconds < 1100) { Pump(); Thread.Sleep(10); }
            Check(Win32.IsWindowVisible(hud) != 0, "The pill dismissed while being dragged after paste.");
            Win32.SendMessageW(hud, 0x232, 0, 0);
            Check(!desktop.UpdateRestartBlocked && saves == 1, "An unmoved drag must release the restart block without saving again.");
            Win32.SendMessageW(hud, 0x113, 2, 0);
            Check(Win32.IsWindowVisible(hud) != 0, "A stale timer hid the new completion state too early.");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (Win32.IsWindowVisible(hud) != 0 && watch.ElapsedMilliseconds < 2200)
            {
                Pump(); Thread.Sleep(10);
            }
            Check(Win32.IsWindowVisible(hud) == 0 && watch.ElapsedMilliseconds >= 600 && watch.ElapsedMilliseconds < 2200, "Paste confirmation did not dismiss after its shorter interval.");
        }
        finally { Win32.DestroyWindow(blocker); Win32.DestroyWindow(target); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static AppConfig RoundTrip(AppConfig config)
    {
        var path = Path.Combine(Path.GetTempPath(), "SharpVoiceHudPosition-" + Guid.NewGuid().ToString("N") + ".json");
        try { ConfigManager.Save(config, path); return ConfigManager.Load(path); }
        finally { File.Delete(path); }
    }
    private static unsafe void Pump()
    {
        var message = new Win32.Msg();
        while (PeekMessageW(&message, 0, 0, 0, 1) != 0) { Win32.TranslateMessage(message); Win32.DispatchMessageW(message); }
    }
    [LibraryImport("user32.dll")] private static unsafe partial int PeekMessageW(Win32.Msg* message, nint window, uint first, uint last, uint remove);
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
    [LibraryImport("user32.dll")] private static partial int GetLayeredWindowAttributes(nint window, out uint color, out byte alpha, out uint flags);
}
