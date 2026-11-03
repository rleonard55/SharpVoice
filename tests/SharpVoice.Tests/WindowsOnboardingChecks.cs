using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsOnboardingChecks
{
    internal static void Run(string? output = null)
    {
        var isolated = CreateDesktopW("SharpVoiceSetupCheck" + Guid.NewGuid().ToString("N"), 0, 0, 0, 0x10000000, 0);
        if (isolated == 0) throw new Exception("Cannot create isolated setup desktop.");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { if (SetThreadDesktop(isolated) == 0) throw new Exception("Cannot enter isolated setup desktop."); CheckWindow(output); }
            catch (Exception ex) { failure = ex; }
        });
        thread.Start(); thread.Join(); CloseDesktop(isolated);
        if (failure is not null) throw failure;
        Console.WriteLine("PASS native setup navigation, skip/reopen, microphone cleanup, AI handoff, practice isolation and both OS themes.");
    }

    private static void CheckWindow(string? output)
    {
        var palette = ThemeColors.DarkTheme;
        var applied = new AppConfig { CleanupPrompt = DefaultPrompts.MasterPrompt };
        using var desktop = new NativeDesktop(applied, () => palette);
        desktop.Initialize(showSettings: false, integrateShell: false, audioFeedback: false);
        var root = Path.Combine(Path.GetTempPath(), "SharpVoiceSetupUi-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "config.json");
        var previews = new List<bool>(); var dismissals = 0; var practiceCalls = 0; var recording = false;
        desktop.InputPreviewChanged = previews.Add;
        desktop.ReadLevel = () => -44;
        var ready = true;
        desktop.CanConfigureSetup = () => ready;
        desktop.ConfigChanged = next => applied = next;
        desktop.SetupDismissed = () => { applied = applied with { SetupFinished = true }; ConfigManager.Save(applied, path); dismissals++; };
        desktop.SetupCancelRequested = () => { recording = false; desktop.SetSetupPracticeState(false); };
        desktop.SetupPracticeRequested = () => { practiceCalls++; recording = !recording; desktop.SetSetupPracticeState(recording, recording); };
        try
        {
            Check(Win32.GetDlgItem(desktop.SettingsHandle, 123) == 0, "General must not offer a setup wizard button.");
            Win32.SendMessageW(desktop.SettingsHandle, 0x111, 123, 0);
            Check(!desktop.SetupOpen, "A stale General setup command must not reopen the wizard.");
            var instructions = Win32.GetDlgItem(desktop.SettingsHandle, 1011);
            Check(Text(instructions) == DefaultPrompts.MasterPrompt, "Fresh general instructions must display the complete built-in template.");
            var edited = "Use British English.\r\n" + DefaultPrompts.MasterPrompt;
            Win32.SetWindowTextW(instructions, edited);
            desktop.ApplySettings();
            Check(applied.CleanupPrompt == edited && applied.CustomPrompt == "", "Edited cleanup template must save separately from additional preferences.");
            Win32.SetWindowTextW(instructions, "");
            desktop.ApplySettings();
            Check(applied.CleanupPrompt == "" && applied.CustomPrompt == "", "Clearing the template must keep the General Instructions box empty.");
            desktop.ShowSetupIfNeeded();
            Check(desktop.SetupOpen && desktop.UpdateRestartBlocked, "Fresh setup did not appear or block update restart.");
            Click(4804); Check(previews.SequenceEqual(new[] { true }), "Preview must start only on an explicit test action.");
            Win32.SendMessageW(desktop.SetupHandle, 0x6, 0, 0);
            Check(previews.SequenceEqual(new[] { true, false }), "Leaving setup must stop the microphone test.");
            Click(4804);
            Win32.SendMessageW(desktop.SetupHandle, 0x113, 7, 0);
            Click(4803); Check(previews.SequenceEqual(new[] { true, false, true, false }), "Leaving Microphone must release preview.");
            Click(4802); Click(4801);
            Check(!desktop.SetupOpen && dismissals == 1 && ConfigManager.Load(path).SetupFinished, "Skip did not persist its dismissal.");
            desktop.ShowSetupIfNeeded(); Check(!desktop.SetupOpen, "Skipped setup appeared automatically again.");
            desktop.ShowSetup(); ready = false;
            Click(4804); Check(previews.Count == 4, "Pending preview opened before the host was ready.");
            Win32.SendMessageW(desktop.SetupHandle, 0x6, 0, 0);
            ready = true; Win32.SendMessageW(desktop.SetupHandle, 0x113, 7, 0);
            Check(previews.Count == 4, "Focus loss must cancel a pending preview rather than resume it.");
            ready = false; Click(4804); ready = true;
            Win32.SendMessageW(desktop.SetupHandle, 0x113, 7, 0);
            Check(previews.Count == 5 && previews[^1], "An explicitly requested preview did not start after readiness.");
            Click(4801); Check(!previews[^1], "Skipping did not release the deferred microphone test.");
            foreach (var theme in new[] { ThemeColors.DarkTheme, ThemeColors.Light })
            {
                palette = theme; desktop.ShowSetup();
                Win32.SendMessageW(desktop.SetupHandle, 0x1A, 0, 0);
                Render(theme, "microphone");
                Check(WindowsHelpSmoke.RenderHelp(Item(4808), null, 16, 560) == theme.Field, "Microphone selector did not follow the form theme.");
                Click(4803); Click(4805);
                Check(Win32.IsWindowVisible(desktop.SettingsHandle) != 0 && Win32.SendMessageW(Win32.GetDlgItem(desktop.SettingsHandle, 1014), 0x130B, 0, 0) == 3,
                    "Shortcut customization did not use the existing Hotkeys page.");
                Win32.SendMessageW(desktop.SettingsHandle, 0x10, 0, 0);
                Check(Win32.IsWindowVisible(desktop.SetupHandle) != 0, "Closing settings did not resume setup.");
                Click(4803); Click(4806);
                Check(Win32.SendMessageW(Win32.GetDlgItem(desktop.SettingsHandle, 1014), 0x130B, 0, 0) == 1, "AI setup did not use AI & Models.");
                var backend = Win32.GetDlgItem(desktop.SettingsHandle, 1002);
                Win32.SendMessageW(backend, 0x14E, 1, 0);
                Win32.SendMessageW(desktop.SettingsHandle, 0x111, (1u << 16) | 1002, backend);
                desktop.ApplySettings();
                Check(applied.Backend == "chatgpt_oauth" && Win32.IsWindowVisible(desktop.SetupHandle) != 0, "Applied AI choice did not return to setup.");
                Click(4803);
                var session = desktop.SetupSession;
                Click(4807);
                Check(recording && HoverIsWindowEnabled(Item(4803)) == 0 && HoverIsWindowEnabled(Item(4801)) != 0, "Practice must block Next but keep Skip available.");
                desktop.SetSetupPracticeResult("Synthetic practice sentence.", session);
                desktop.SetSetupPracticeResult("Stale text", session - 1);
                Check(Text(Item(4810)) == "Synthetic practice sentence.", "Practice output or stale-result protection failed.");
                Click(4807); Check(!recording, "Practice stop did not route to the host.");
                Render(theme, "practice");
                Click(4803);
                Check(!desktop.SetupOpen && ConfigManager.Load(path).Backend == "chatgpt_oauth", "Finish reset AI preferences or left setup open.");
            }
            Check(practiceCalls == 4, "Practice actions did not route exactly once per click.");
            for (var step = 0; step < 4; step++)
            {
                desktop.ShowSetup();
                for (var n = 0; n < step; n++) Click(4803);
                if (step == 0) Click(4804);
                if (step == 3)
                {
                    Click(4807); var session = desktop.SetupSession;
                    Win32.SendMessageW(desktop.SetupHandle, 0x6, 0, 0);
                    desktop.SetSetupPracticeResult("Stale after focus loss", session);
                    Check(!recording && Text(Item(4810)).Length == 0, "Leaving practice accepted a stale response or kept recording.");
                }
                Win32.SendMessageW(desktop.SetupHandle, 0x10, 0, 0);
                Check(!desktop.SetupOpen && !recording, "Closing a step did not cancel its owned practice/preview.");
            }
            Check(!previews[^1], "Closing Microphone left input preview enabled.");
            Check(!desktop.UpdateRestartBlocked, "Closed setup left update restart blocked.");
            nint Item(int id) => Win32.GetDlgItem(desktop.SetupHandle, id);
            void Click(int id) { Check(Item(id) != 0, "Missing wizard action " + id); Win32.SendMessageW(desktop.SetupHandle, 0x111, (nuint)id, Item(id)); }
            void Render(ThemeColors theme, string step)
            {
                var file = output is null ? null : output + "-" + step + (theme.Dark ? "-dark.bmp" : "-light.bmp");
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    Pump();
                    if (WindowsHelpSmoke.RenderHelp(desktop.SetupHandle, file, 60) == theme.Background) return;
                    Thread.Sleep(20);
                }
                throw new Exception("Setup background did not follow the OS theme.");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static unsafe void Pump()
    {
        var message = new Win32.Msg();
        for (var i = 0; i < 32 && PeekMessageW(&message, 0, 0, 0, 1) != 0; i++) { Win32.TranslateMessage(message); Win32.DispatchMessageW(message); }
    }
    [LibraryImport("user32.dll")] private static unsafe partial int PeekMessageW(Win32.Msg* message, nint window, uint first, uint last, uint remove);
    private static unsafe string Text(nint window)
    {
        var chars = new char[Win32.GetWindowTextLengthW(window) + 1];
        fixed (char* p = chars) { Win32.GetWindowTextW(window, p, chars.Length); }
        return new string(chars, 0, chars.Length - 1);
    }
    [LibraryImport("user32.dll", EntryPoint = "IsWindowEnabled")] private static partial int HoverIsWindowEnabled(nint window);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint CreateDesktopW(string name, nint device, nint mode, uint flags, uint access, nint security);
    [LibraryImport("user32.dll")] private static partial int SetThreadDesktop(nint desktop);
    [LibraryImport("user32.dll")] private static partial int CloseDesktop(nint desktop);
}
