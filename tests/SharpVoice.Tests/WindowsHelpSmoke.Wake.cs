using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    private static unsafe void CheckWake(NativeDesktop desktop, Action<ThemeColors> changeTheme, string? output)
    {
        static void Check(bool value, string why) { if (!value) throw new Exception(why); }
        var window = desktop.SettingsHandle; var savedCallback = desktop.ConfigChanged; AppConfig? saved = null;
        desktop.ConfigChanged = c => saved = c;
        var tabs = Win32.GetDlgItem(window, 1014); Win32.SendMessageW(tabs, 0x130C, 0, 0);
        var tabNotice = new Win32.Notification { Window=tabs, Id=1014, Code=-551 }; Win32.SendMessageW(window, 0x4E, 1014, (nint)(&tabNotice));
        var entry = Win32.GetDlgItem(window, 121);
        Check(entry != 0, "Audio settings has no Wake Phrases entry.");
        Win32.SendMessageW(window, 0x111, 121, entry);
        Check(desktop.ToolsWindow != 0 && desktop.ToolField("WakePhrasesEnabled") != 0, "Wake preferences did not open.");
        string Text(nint control) { var b = new char[4096]; fixed (char* p = b) return new string(b, 0, Win32.GetWindowTextW(control, p, b.Length)); }
        Check(Text(desktop.ToolField("AskWakePhrase")) == "Hey SharpVoice" && Text(desktop.ToolField("DictationWakePhrase")) == "Hey SharpVoice Dictate", "Wake defaults were not shown.");
        Check(Text(Win32.GetDlgItem(desktop.ToolsWindow, 4490)).Contains("selected Whisper model"), "Wake setup hides the selected speech model.");
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4484, 0);
        Check(Text(desktop.HelpBody).Contains("microphone listening locally"), "Wake help hides idle microphone use.");
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4484, 0);
        desktop.SetWakePreparation(new("downloading", 30, 100, "Downloading Local Wake Model"));
        Check(WakeWindowText(Win32.GetDlgItem(desktop.ToolsWindow, 4491)).Contains("30%") && Win32.IsWindowVisible(Win32.GetDlgItem(desktop.ToolsWindow, 4483)) != 0, "Download progress/cancellation missing.");
        desktop.SetWakePreparation(new("unavailable", 0, null, "Wake Detection Unavailable · Hotkeys Still Work"));
        Check(Win32.IsWindowVisible(Win32.GetDlgItem(desktop.ToolsWindow, 4482)) != 0 && Win32.IsWindowVisible(Win32.GetDlgItem(desktop.ToolsWindow, 4483)) == 0, "Failed preparation falsely looks ready or has no retry.");
        Win32.SetWindowTextW(desktop.ToolField("AskWakePhrase"), "Hello Computer");
        Win32.SetWindowTextW(desktop.ToolField("DictationWakePhrase"), "Hello Computer Write");
        Win32.SetWindowTextW(desktop.ToolField("WakeSilenceSeconds"), "3");
        Win32.SendMessageW(desktop.ToolField("WakePhrasesEnabled"), 0x14E, 1, 0);
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4481, 0);
        Check(saved is { WakePhrasesEnabled: true, AskWakePhrase: "Hello Computer", DictationWakePhrase: "Hello Computer Write", WakeSilenceSeconds: 3 } && desktop.ToolsWindow == 0, "Wake save lost choices or failed to close.");
        Win32.SendMessageW(window, 0x111, 121, entry);
        if (output is not null)
            foreach (var palette in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
            {
                changeTheme(palette); Win32.SendMessageW(window, 0x1A, 0, 0); Pump();
                Check(RenderHelp(desktop.ToolsWindow, output + "-wake" + (palette.Dark ? "-dark.bmp" : "-light.bmp"), 60) == palette.Background, "Wake form failed OS theme.");
            }
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4205, 0);
        desktop.ConfigChanged = savedCallback;
    }
}
