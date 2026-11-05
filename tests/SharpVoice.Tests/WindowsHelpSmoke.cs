using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    public static void Run(string? output = null, bool connectedOnly = false, bool geminiOnly = false, bool turnaroundOnly = false, bool diagnosticsOnly = false, bool updatesOnly = false)
    {
        var isolated = CreateDesktopW("SharpVoiceHelpCheck" + Guid.NewGuid().ToString("N"), 0, 0, 0, 0x10000000, 0);
        if (isolated == 0) throw new Exception("Cannot create isolated help-check desktop.");
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (SetThreadDesktop(isolated) == 0) throw new Exception("Cannot enter isolated help-check desktop.");
                var palette = ThemeColors.DarkTheme;
                var diagnosticActions = new DiagnosticActionsFixture();
                using var desktop = new NativeDesktop(new AppConfig { Backend = "chatgpt_oauth" }, () => palette, copyDiagnostics: diagnosticActions.Copy, openDiagnosticsUrl: diagnosticActions.Open);
                desktop.Initialize(showSettings: false, integrateShell: false);
                if (Win32.GetDlgItem(desktop.SettingsHandle, 113) == 0) throw new Exception("Local models have no Download Model action.");
                if(updatesOnly)CheckUpdates(desktop,next=>palette=next,output);else if(diagnosticsOnly)CheckDiagnostics(desktop,next=>palette=next,output,diagnosticActions);else if(turnaroundOnly)CheckTurnaround(desktop,next=>palette=next,output);else if(geminiOnly)CheckGeminiApi(desktop,next=>palette=next,output);else if(connectedOnly)CheckConnected(desktop, next=>palette=next, output);else CheckHelp(desktop, next => palette = next, output, diagnosticActions);
            }
            catch (Exception ex) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
        });
        thread.Start(); thread.Join(); CloseDesktop(isolated); failure?.Throw();
        Console.WriteLine(updatesOnly ? "PASS native one-click update controls on an isolated desktop." : "PASS native on-demand Settings help, hover/click/keyboard, dismissal, dynamic content and OS themes on an isolated desktop.");
    }

    private static unsafe void CheckHelp(NativeDesktop desktop, Action<ThemeColors> changeTheme, string? output, DiagnosticActionsFixture diagnosticActions)
    {
        CheckDiagnostics(desktop, changeTheme, output, diagnosticActions);
        CheckUpdates(desktop, changeTheme, output);
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var window = desktop.SettingsHandle;
        Check(Win32.GetDlgItem(window, 112) != 0, "Settings has no Test Connection action.");
        nint Item(int id) => Win32.GetDlgItem(window, id);
        string Text(nint hwnd)
        {
            var buffer = new char[4096];
            fixed (char* pointer = buffer) return new string(buffer, 0, Win32.GetWindowTextW(hwnd, pointer, buffer.Length));
        }
        void Page(int page)
        {
            var tabs = Item(1014);
            Win32.SendMessageW(tabs, 0x130C, (nuint)page, 0);
            var notification = new Win32.Notification { Window = tabs, Id = 1014, Code = -551 };
            Win32.SendMessageW(window, 0x4E, 1014, (nint)(&notification));
        }
        void Click(int id) => Win32.SendMessageW(window, 0x111, (nuint)id, Item(id));
        string HelpText(int id, bool pinned = false)
        {
            if (pinned || desktop.OpenHelpId == id) return Text(desktop.HelpBody);
            var previous = desktop.OpenHelpId;
            Click(id); var content = Text(desktop.HelpBody); Click(id);
            if (previous != 0) Click(previous);
            return content;
        }
        bool Input(nint target, uint message, nuint key) => desktop.HandleHelpInput(new Win32.Msg { Hwnd = target, Message = message, WParam = key });
        int[][] ids = [[3009, 3022, 3008], [3001, 3002, 3003, 3021, 2112], [3004, 3011, 3013], [3005, 3019], [3006, 3007, 3010], [3020]];
        var changes = 0; var previewChanges = 0; var authRequests = 0;
        desktop.ConfigChanged = _ => changes++;
        desktop.InputPreviewChanged = _ => previewChanges++;
        desktop.CanMonitorInput = () => false; // Isolate help actions from automatic Audio monitoring.
        desktop.SignInRequested = () => authRequests++;
        Win32.ShowWindow(window, 4); // Offscreen; never activate the user's desktop.
        for (var page = 0; page < ids.Length; page++)
        {
            Page(page);
            foreach (var id in ids[page])
            {
                Check(Item(id) != 0, $"Missing contextual help button {id}.");
                Check(Text(Item(id)).StartsWith("Help: "), "Help needs a descriptive accessible name.");
                Win32.SendMessageW(Item(id), 0x200, 0, 0);
                Win32.SendMessageW(Item(id), 0x113, desktop.PendingHelpTimerId, 0);
                Check(Win32.IsWindowVisible(desktop.HelpWindow) != 0 && desktop.OpenHelpId == 0, "Hover failed to show temporary help.");
                var hoverText = Text(desktop.HelpBody);
                Check(hoverText.Length > 40, "Missing setting-specific help.");
                Win32.SendMessageW(Item(id), 0x2A3, 0, 0);
                Check(Win32.IsWindowVisible(desktop.HelpWindow) == 0, "Pointer leave did not hide hover help.");
                var focus = GetFocus();
                Click(id);
                Pump();
                Check(desktop.OpenHelpId == id && Win32.IsWindowVisible(desktop.HelpWindow) != 0, $"Click {id} did not open help (open={desktop.OpenHelpId}, visible={Win32.IsWindowVisible(desktop.HelpWindow)}, text={HelpText(id, true)}).");
                Check(GetFocus() == focus, "Help changed focus.");
                Check(Text(desktop.HelpBody) == hoverText, "Click and hover explain different settings.");
                Click(id); Check(desktop.OpenHelpId == 0, "Click again did not close help.");
            }
        }
        Page(1);
        Win32.SendMessageW(Item(3021), 0x200, 0, 0);
        var staleTimer = desktop.PendingHelpTimerId;
        Win32.SendMessageW(window, 0x6, 0, 0);
        Win32.SendMessageW(Item(3021), 0x113, staleTimer, 0);
        Check(Win32.IsWindowVisible(desktop.HelpWindow) == 0, "A stale hover timer reopened help after deactivation.");
        Win32.SendMessageW(Item(3021), 0x200, 0, 0);
        Check(desktop.PendingHelpTimerId != 0 && desktop.PendingHelpTimerId != staleTimer, "Hover did not rearm after dismissal.");
        Win32.SendMessageW(Item(3021), 0x113, staleTimer, 0);
        Check(Win32.IsWindowVisible(desktop.HelpWindow) == 0, "Old timer triggered a new hover generation.");
        Check(Input(Item(3021), 0x100, 13) && desktop.OpenHelpId == 3021, "Enter did not open help.");
        Input(Item(3021), 0x101, 13);
        Check(changes == 0, "Help Enter applied settings.");
        Check(Input(Item(3021), 0x100, 27) && desktop.OpenHelpId == 0, "Escape did not close help.");
        Win32.SendMessageW(Item(3021), 0x100, 32, 0);
        Win32.SendMessageW(Item(3021), 0x101, 32, 0);
        Check(desktop.OpenHelpId == 3021, "Native button Space failed or double-toggled help.");
        Check((Win32.SendMessageW(Item(3021), 0x87, 13, 0) & 4) != 0, "Enter can escape to Apply.");
        Check(!Input(Item(3021), 0x100, 9) && desktop.OpenHelpId == 0, "Tab failed to dismiss or was consumed.");
        Click(3021); Check(!Input(Item(1021), 0x201, 0) && desktop.OpenHelpId == 0, "Outside click failed to dismiss or was consumed.");
        Check(HelpText(3021).Contains("8 words") && HelpText(3021).Contains("selected-text"), "Threshold exceptions missing from contextual help.");
        Click(3021); Win32.SendMessageW(window, 0x3, 0, 0);
        Check(desktop.OpenHelpId == 0, "Moving Settings left help at stale coordinates.");
        Click(3021); Page(2); Check(desktop.OpenHelpId == 0, "Navigation left stale help visible.");
        Click(3021); Check(desktop.OpenHelpId == 0, "Hidden tab can open stale help.");
        Page(1);
        desktop.SetResolvedModel("chatgpt_oauth", "test-exact-model-id");
        Check(Text(Item(1017)).Contains("test-exact-model-id") && Text(Item(1017)).Length < 100, "Actual model ID was hidden or mixed with guidance.");
        Win32.SendMessageW(Item(1002), 0x14E, 4, 0);
        Win32.SendMessageW(window, 0x111, (1u << 16) | 1002, Item(1002));
        Check(Win32.IsWindowVisible(Item(1023)) != 0 && Text(Item(1023)).Contains("retain history"), "Gemini retention notice was hidden.");
        Check(HelpText(3003).Contains("Antigravity"), "Model help did not follow provider.");
        for (var i = 0; i < 100; i++)
        {
            desktop.SetGoogleStatus("Current account status " + i);
            Check(HelpText(3003).Contains("Current account status " + i), "Help retained stale provider text.");
        }
        Click(3003);
        desktop.SetStatus("Inserted · Ready · Ctrl + Alt: dictate · Ctrl + Alt + Shift: ask AI");
        Check(Text(Item(1024)) == "Inserted", "Routine shortcut reminders still clutter status.");
        Check(desktop.OpenHelpId == 3003 && HelpText(3003, true).Contains("Current account status 99"), "Unrelated status refresh changed open model help.");
        Check(HelpText(3900).Contains("Ctrl + Alt + Shift"), "Full status or shortcut guidance missing from help.");
        desktop.SetStatus("Test error: Ready · context", isError: true);
        Check(Text(Item(1024)) == "Test error: Ready · context", "Error details were shortened.");
        Win32.SendMessageW(window, 0x6, 0, 0); Check(desktop.OpenHelpId == 0, "Deactivation left help visible.");
        foreach (var palette in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
        {
            changeTheme(palette); Win32.SendMessageW(window, 0x1A, 0, 0);
            Click(3021); Pump();
            var file = output is null ? null : output + (palette.Dark ? "-dark.bmp" : "-light.bmp");
            Check(RenderHelp(desktop.HelpWindow, file) == palette.Surface, "Rendered help background did not follow OS theme.");

        }
        CheckControlHover(desktop, changeTheme, output);
        CheckConnection(desktop, changeTheme, output);
        CheckLocalDownloads(desktop, changeTheme, output);
        CheckTurnaround(desktop, changeTheme, output);
        CheckProviderIcons(desktop, changeTheme, output);
        CheckGeminiApi(desktop, changeTheme, output);
        CheckWritingTools(desktop, changeTheme, output);
        CheckPlugins(desktop, changeTheme, output);
        CheckAnswer(desktop, changeTheme, output);
            CheckConnected(desktop, changeTheme, output);
        CheckWake(desktop, changeTheme, output);
        Click(3900); Win32.SendMessageW(window, 0x10, 0, 0);
        Check(desktop.OpenHelpId == 0 && Win32.IsWindowVisible(desktop.HelpWindow) == 0, "Closing Settings left help visible.");
        Check(changes == 0 && previewChanges == 0 && authRequests == 0, "Help triggered a settings, microphone or account action.");
    }
    private static unsafe void Pump()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var msg = new Win32.Msg();
            while (PeekMessageW(&msg, 0, 0, 0, 1) != 0) { Win32.TranslateMessage(msg); Win32.DispatchMessageW(msg); }
            Thread.Sleep(10);
        }
    }
    [LibraryImport("user32.dll")] private static unsafe partial int PeekMessageW(Win32.Msg* message, nint window, uint first, uint last, uint remove);
    [LibraryImport("user32.dll")] private static partial nint GetFocus();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint CreateDesktopW(string name, nint device, nint mode, uint flags, uint access, nint security);
    [LibraryImport("user32.dll")] private static partial int SetThreadDesktop(nint desktop);
    [LibraryImport("user32.dll")] private static partial int CloseDesktop(nint desktop);
}
