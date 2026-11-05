using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    private sealed class DiagnosticActionsFixture
    {
        public string? Copied;
        public readonly List<string> Opened = [];
        public bool FailCopy, FailOpen;
        public void Copy(string report, nint owner) { if (FailCopy) throw new IOException("fixture-secret"); Copied = report; }
        public void Open(string url) { if (FailOpen) throw new IOException("fixture-secret"); Opened.Add(url); }
    }
    private static unsafe void CheckDiagnostics(NativeDesktop desktop, Action<ThemeColors> changeTheme, string? output, DiagnosticActionsFixture actions)
    {
        var window = desktop.SettingsHandle;
        Win32.ShowWindow(window, 4);
        var tabs = Win32.GetDlgItem(window, 1014);
        Win32.SendMessageW(tabs, 0x130C, 1, 0);
        var notice = new Win32.Notification { Window = tabs, Id = 1014, Code = -551 };
        Win32.SendMessageW(window, 0x4E, 1014, (nint)(&notice));
        var button = Win32.GetDlgItem(window, 124);
        if (button == 0 || Win32.IsWindowVisible(button) == 0) throw new Exception("AI & Models has no visible Diagnostics action.");
        foreach (var palette in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
        {
            changeTheme(palette); Win32.SendMessageW(window, 0x1A, 0, 0); Pump();
            Win32.GetWindowRect(window, out var settingsBounds);
            Win32.GetWindowRect(button, out var buttonBounds);
            var sampleX = buttonBounds.Right - settingsBounds.Left + 8;
            var sampleY = buttonBounds.Bottom - settingsBounds.Top + 4;
            if (RenderHelp(window, output is null ? null : output + (palette.Dark ? "-diagnostics-panel-dark.bmp" : "-diagnostics-panel-light.bmp"), sampleY, sampleX) != palette.Surface)
                throw new Exception("Diagnostics extends beyond the colored settings panel.");
        }
        ProviderDiagnostics.Shared.Clear();
        ProviderDiagnostics.Shared.Record(new(ProviderOperationKind.ChatGptInference, ProviderFailureKind.Tls, null, null, 125));
        Win32.SendMessageW(window, 0x111, 124, button);
        if (desktop.ToolsWindow == 0 || desktop.ToolField("DiagnosticReport") == 0) throw new Exception("Diagnostics has no report preview.");
        string Read(nint handle)
        {
            var chars = new char[20000];
            fixed (char* pointer = chars) return new string(chars, 0, Win32.GetWindowTextW(handle, pointer, chars.Length));
        }
        var preview = Read(desktop.ToolField("DiagnosticReport"));
        if (actions.Copied is not null || actions.Opened.Count != 0 || !preview.Contains("ChatGptInference")) throw new Exception("Opening diagnostics shared data or lost its report.");
        ProviderDiagnostics.Shared.Record(new(ProviderOperationKind.GeminiInference, ProviderFailureKind.Dns, null, null, 5));
        var copyButton = Win32.GetDlgItem(desktop.ToolsWindow, 4801);
        if (!desktop.HandleToolsInput(new Win32.Msg { Hwnd = copyButton, Message = 0x100, WParam = 13 })) throw new Exception("Copy Report has no keyboard action.");
        if (actions.Copied != preview || actions.Copied.Contains("GeminiInference")) throw new Exception("Copy did not use the exact displayed snapshot.");
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4802, 0);
        if (actions.Opened.Single() != ProviderDiagnostics.IssueUrl || actions.Opened[0].Contains("body=")) throw new Exception("Browser submission carried report data or used a wrong destination.");
        actions.FailCopy = true; actions.FailOpen = true;
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4801, 0);
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4802, 0);
        if (Read(desktop.ToolField("DiagnosticReport")) != preview || Read(Win32.GetDlgItem(desktop.ToolsWindow, 4100)).Contains("fixture-secret")) throw new Exception("Boundary failure lost preview or leaked raw exception text.");
        actions.FailCopy = actions.FailOpen = false;
        foreach (var palette in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
        {
            changeTheme(palette); Win32.SendMessageW(window, 0x1A, 0, 0); Pump();
            RenderHelp(desktop.ToolsWindow, output is null ? null : output + (palette.Dark ? "-diagnostics-dark.bmp" : "-diagnostics-light.bmp"), 60);
        }
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4803, 0);
        if (ProviderDiagnostics.Shared.Snapshot().Length != 0) throw new Exception("Diagnostics Clear retained records.");
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4801, 0);
        if (actions.Copied is null || actions.Copied.Contains("Operation:") || !actions.Copied.Contains("Failures: 0")) throw new Exception("Clear left a stale report in the copy action.");
        Win32.SendMessageW(desktop.ToolsWindow, 0x111, 4205, 0);
        Console.WriteLine("PASS native diagnostics preview, Clear and themes without provider credentials.");
    }
}
