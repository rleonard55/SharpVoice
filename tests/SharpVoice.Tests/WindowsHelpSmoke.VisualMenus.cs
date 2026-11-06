using System.Reflection;
using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    // Capture real menus on a private desktop; never choose an action or touch the user's tray.
    internal static void CaptureMenus(string output)
    {
        var isolated = CreateDesktopW("SharpVoiceVisual" + Guid.NewGuid().ToString("N"), 0, 0, 0, 0x10000000, 0);
        if (isolated == 0) throw new Exception("Cannot create visual capture desktop.");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (SetThreadDesktop(isolated) == 0) throw new Exception("Cannot enter capture desktop.");
                var palette = ThemeColors.Light;
                using var desktop = new NativeDesktop(new AppConfig { Backend = "chatgpt_oauth" }, () => palette);
                desktop.IsChatGptSignedIn = () => true;
                desktop.Initialize(showSettings: false, integrateShell: false, audioFeedback: false);
                foreach (var theme in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
                {
                    palette = theme;
                    Win32.SendMessageW(desktop.SettingsHandle, 0x1A, 0, 0);
                    foreach (var name in new[] { "TrayMenu", "AccountMenu" })
                    {
                        Exception? captureFailure = null;
                        var owner = VisualThreadId();
                        var capture = new Thread(() =>
                        {
                            try
                            {
                                if (SetThreadDesktop(isolated) == 0) throw new Exception("Cannot enter menu capture desktop.");
                                nint menu = 0;
                                for (var i = 0; i < 100 && menu == 0; i++) { Thread.Sleep(30); menu = VisualFindWindow("#32768", null); }
                                if (menu == 0) throw new Exception("Menu did not open: " + name);
                                Thread.Sleep(200);
                                RenderHelp(menu, output + "-" + name + (theme.Dark ? "-dark.bmp" : "-light.bmp"));
                            }
                            catch (Exception ex) { captureFailure = ex; }
                            finally { VisualPostThreadMessage(owner, 0x100, 27, 0); }
                        });
                        capture.Start();
                        typeof(NativeDesktop).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(desktop, null);
                        capture.Join();
                        if (captureFailure is not null) throw captureFailure;
                    }
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.Start(); thread.Join(); CloseDesktop(isolated);
        if (failure is not null) throw failure;
        Console.WriteLine("PASS isolated tray and account menu visual captures.");
    }
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)] private static partial nint VisualFindWindow(string cls, string? title);
    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")] private static partial uint VisualThreadId();
    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")] private static partial int VisualPostThreadMessage(uint thread, uint message, nuint wparam, nint lparam);
}
