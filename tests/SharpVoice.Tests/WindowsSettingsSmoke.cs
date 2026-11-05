using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsSettingsSmoke
{
    public static unsafe void Run(bool custom)
    {
        var config = custom
            ? new AppConfig { Hotkey = HotkeyDefinition.FromDisplayName("Ctrl + Shift + Space"), SpeechModel = "base.en", Backend = "none", Mode = "email", Injection = "typing", NoiseGateDb = -70, AudioDeviceId = "unavailable-test-endpoint", EmailPrompt = "Keep paragraphs short.", StartOnLogin = true, MinimumAiWords = 0, MicrophoneActivation = "keep_ready" }
            : new AppConfig();
        using var desktop = new NativeDesktop(config);
        var signedIn = false;
        var authBusy = false;
        var signInRequests = 0;
        var signOutRequests = 0;
        desktop.IsChatGptSignedIn = () => signedIn;
        desktop.IsAuthenticationBusy = () => authBusy;
        desktop.SignInRequested = () => signInRequests++;
        desktop.SignOutRequested = () => signOutRequests++;
        desktop.Initialize(showSettings: false);
        var tabs = GetDlgItem(desktop.SettingsHandle, 1014);
        if (tabs == 0 || Win32.SendMessageW(tabs, 0x1304, 0, 0) != 6) throw new Exception("Expected six settings tabs.");
        var expected = new[] { (1005, HotkeyDefinition.DisplayName(config.Hotkey)), (1019, HotkeyDefinition.DisplayName(config.AskHotkey)), (1001, custom ? "Base (English, Balanced)" : "Tiny Q5 (English, Smaller Download)"), (1002, "None (Transcription Only)"), (1004, custom ? "Email (Professional Messages)" : "Auto (Match the App)"), (1006, custom ? "Typing (Compatibility Mode)" : "Clipboard (Recommended)"), (1020, config.StartOnLogin ? "On (Start in Tray)" : "Off (Launch Manually)"), (1022, custom ? "Keep Ready (Faster Start)" : "On Hotkey (Default)"), (1021, config.MinimumAiWords.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
        foreach (var (id, value) in expected)
        {
            var control = GetDlgItem(desktop.SettingsHandle, id);
            if (control == 0) throw new Exception("Missing settings dropdown.");
            var buffer = new char[128];
            int count;
            fixed (char* pointer = buffer) count = Win32.GetWindowTextW(control, pointer, buffer.Length);
            var displayed = new string(buffer, 0, count);
            if (displayed != value) throw new Exception($"Settings displayed '{displayed}' instead of saved '{value}'.");
        }
        string InputLabel()
        {
            var buffer = new char[256];
            fixed (char* text = buffer)
            {
                var count = Win32.GetWindowTextW(GetDlgItem(desktop.SettingsHandle, 1018), text, buffer.Length);
                return new string(buffer, 0, count);
            }
        }
        desktop.ReadLevel = () => -52;
        var gate = GetDlgItem(desktop.SettingsHandle, 1008);
        Win32.SetWindowTextW(gate, "-71");
        Win32.SendMessageW(desktop.SettingsHandle, 0x113, 3, 0);
        if (!InputLabel().Contains("19 dB above gate")) throw new Exception("Input meter did not show sound above the draft gate.");
        desktop.ReadLevel = () => -80;
        Win32.SendMessageW(desktop.SettingsHandle, 0x113, 3, 0);
        if (!InputLabel().Contains("9 dB below gate")) throw new Exception("Input meter did not show below-gate sound.");
        var slider = GetDlgItem(desktop.SettingsHandle, 1015);
        Win32.SendMessageW(slider, 0x405, 1, -85);
        Win32.SendMessageW(desktop.SettingsHandle, 0x114, 0, slider);
        if (!InputLabel().Contains("5 dB above gate")) throw new Exception("Gate slider did not refresh the live threshold comparison.");
        desktop.ReadLevel = () => -85;
        Win32.SendMessageW(desktop.SettingsHandle, 0x113, 3, 0);
        if (!InputLabel().Contains("at gate")) throw new Exception("Threshold equality was not represented correctly.");
        desktop.ReadLevel = () => float.NaN;
        Win32.SendMessageW(desktop.SettingsHandle, 0x113, 3, 0);
        if (!InputLabel().Contains("Waiting")) throw new Exception("Invalid input level showed a passing comparison.");
        desktop.ReadLevel = () => -80;
        Win32.SetWindowTextW(gate, "invalid");
        if (!InputLabel().Contains("valid gate")) throw new Exception("Invalid gate draft displayed a misleading passing state.");
        Win32.SetWindowTextW(gate, config.NoiseGateDb.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppConfig? applied = null;
        desktop.ConfigChanged = next => applied = next;
        var previewChanges = new List<bool>();
        desktop.InputPreviewChanged = enabled => previewChanges.Add(enabled);
        SetWindowPos(desktop.SettingsHandle, 0, -32000, -32000, 0, 0, 0x15);
        Win32.ShowWindow(desktop.SettingsHandle, 4); // Offscreen and without activation.
        Win32.SendMessageW(desktop.SettingsHandle, 0x113, 3, 0);
        Win32.SendMessageW(tabs, 0x130C, 1, 0);
        var previewPage = new Win32.Notification { Window = tabs, Id = 1014, Code = -551 };
        Win32.SendMessageW(desktop.SettingsHandle, 0x4E, 1014, (nint)(&previewPage));
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 111, 0); // Removed action cannot restart monitoring.
        if (!previewChanges.SequenceEqual(new[] { true, false })) throw new Exception("Automatic monitoring did not stop on leaving Audio.");
        Win32.SendMessageW(tabs, 0x130C, 0, 0);
        Win32.SendMessageW(desktop.SettingsHandle, 0x4E, 1014, (nint)(&previewPage));
        desktop.SetActive(true); desktop.SetActive(false);
        if (!previewChanges.SequenceEqual(new[] { true, false, true, false, true })) throw new Exception("Dictation did not suspend and resume automatic monitoring.");
        Win32.SendMessageW(desktop.SettingsHandle, 0x10, 0, 0);
        if (previewChanges.Last() || previewChanges.Count != 6) throw new Exception("Closing Settings retained microphone monitoring.");
        Win32.ShowWindow(desktop.SettingsHandle, 4);
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 101, 0);
        if ((GetWindowLongPtrW(desktop.SettingsHandle, -16) & 0x10000000) != 0)
            throw new Exception("Successful Apply did not close Settings to the tray.");
        if (applied != config) throw new Exception("Applying untouched settings changed the configuration.");
        Win32.ShowWindow(desktop.SettingsHandle, 4);
        Win32.SetWindowTextW(gate, "-120");
        var rejected = false;
        try { desktop.ApplySettings(); } catch (ArgumentException) { rejected = true; }
        if (!rejected || (GetWindowLongPtrW(desktop.SettingsHandle, -16) & 0x10000000) == 0)
            throw new Exception("Validation failure must leave Settings open.");
        Win32.SetWindowTextW(gate, config.NoiseGateDb.ToString(System.Globalization.CultureInfo.InvariantCulture));
        desktop.ConfigChanged = _ => throw new IOException("Simulated save failure");
        rejected = false;
        try { desktop.ApplySettings(); } catch (IOException) { rejected = true; }
        if (!rejected || (GetWindowLongPtrW(desktop.SettingsHandle, -16) & 0x10000000) == 0)
            throw new Exception("Save failure must leave Settings open.");
        desktop.ConfigChanged = next => applied = next;
        var minimumWords = GetDlgItem(desktop.SettingsHandle, 1021);
        foreach (var invalid in new[] { "-1", "1001", "eight" })
        {
            Win32.SetWindowTextW(minimumWords, invalid);
            rejected = false;
            try { desktop.ApplySettings(); } catch (Exception ex) when (ex is ArgumentException or FormatException) { rejected = true; }
            if (!rejected || (GetWindowLongPtrW(desktop.SettingsHandle, -16) & 0x10000000) == 0) throw new Exception("Invalid cleanup threshold must leave Settings open.");
        }
        foreach (var value in new[] { 0, 12, config.MinimumAiWords })
        {
            Win32.SetWindowTextW(minimumWords, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            desktop.ApplySettings();
            if (applied?.MinimumAiWords != value) throw new Exception("Cleanup threshold was not applied.");
        }
        var speechChoices = GetDlgItem(desktop.SettingsHandle, 1001);
        for (var i = 0; i < SpeechModels.All.Length; i++)
        {
            Win32.SendMessageW(speechChoices, 0x14E, (nuint)i, 0);
            desktop.ApplySettings();
            if (applied?.SpeechModel != SpeechModels.All[i]) throw new Exception("Friendly speech choice saved a label instead of its ID.");
        }
        Win32.SendMessageW(speechChoices, 0x14E, (nuint)Array.IndexOf(SpeechModels.All, config.SpeechModel), 0);
        desktop.ApplySettings();
        var activation = GetDlgItem(desktop.SettingsHandle, 1022);
        foreach (var mode in new[] { 0, 1, config.MicrophoneActivation == "keep_ready" ? 1 : 0 })
        {
            Win32.SendMessageW(activation, 0x14E, (nuint)mode, 0);
            desktop.ApplySettings();
            if (applied?.MicrophoneActivation != (mode == 0 ? "on_hotkey" : "keep_ready")) throw new Exception("Activation choice saved a label instead of its ID.");
        }
        var askShortcut = GetDlgItem(desktop.SettingsHandle, 1019);
        fixed (char* f9 = "F9")
        {
            var index = Win32.SendMessageW(askShortcut, 0x158, unchecked((nuint)(-1)), (nint)f9);
            if (index < 0) throw new Exception("Ask AI shortcut presets are missing.");
            Win32.SendMessageW(askShortcut, 0x14E, (nuint)index, 0);
        }
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 101, 0);
        if (applied?.AskHotkey != 0x78 || applied.Hotkey != config.Hotkey)
            throw new Exception("Changing the Ask AI shortcut lost the separate dictation shortcut.");
        Win32.SetWindowTextW(GetDlgItem(desktop.SettingsHandle, 1008), "-72");
        var promptMode = GetDlgItem(desktop.SettingsHandle, 1013);
        Win32.SendMessageW(promptMode, 0x14E, 1, 0);
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, (1u << 16) | 1013, promptMode);
        Win32.SetWindowTextW(GetDlgItem(desktop.SettingsHandle, 1012), "Preserve ticket IDs.");
        Win32.SendMessageW(promptMode, 0x14E, 2, 0);
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, (1u << 16) | 1013, promptMode);
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 101, 0);
        if (applied?.EmailPrompt != "Preserve ticket IDs." || applied.NoiseGateDb != -72 || applied.AudioDeviceId != config.AudioDeviceId)
            throw new Exception("Prompt navigation, gate edits or unavailable microphone selection were lost.");
        Win32.SendMessageW(tabs, 0x130C, 1, 0);
        var changed = new Win32.Notification { Window = tabs, Id = 1014, Code = -551 };
        Win32.SendMessageW(desktop.SettingsHandle, 0x4E, 1014, (nint)(&changed));
        var signIn = GetDlgItem(desktop.SettingsHandle, 102);
        var signOut = GetDlgItem(desktop.SettingsHandle, 103);
        bool Visible(nint control) => (GetWindowLongPtrW(control, -16) & 0x10000000) != 0;
        if (!Visible(minimumWords)) throw new Exception("Minimum words belongs on AI & models.");
        desktop.SetCleanupSkipped(true);
        if (!desktop.ModelSummary.Contains("AI cleanup skipped")) throw new Exception("HUD did not disclose cleanup bypass.");
        desktop.SetVoiceMode(VoiceMode.AskAi);
        if (desktop.ModelSummary.Contains("AI cleanup skipped")) throw new Exception("New voice mode retained a stale cleanup bypass.");
        desktop.SetVoiceMode(VoiceMode.Dictation);
        var cancel = GetDlgItem(desktop.SettingsHandle, 108);
        var cancelRequests = 0;
        desktop.CancelRequested = () => cancelRequests++;
        if (cancel == 0 || Visible(cancel)) throw new Exception("Cancel dictation should be hidden while idle.");
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 108, 0);
        if (cancelRequests != 0) throw new Exception("Idle cancellation must be ignored.");
        desktop.SetActive(true);
        if (!Visible(cancel)) throw new Exception("Active dictation needs a Settings cancel action.");
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 108, 0);
        if (cancelRequests != 1) throw new Exception("Settings did not request cancellation.");
        desktop.SetActive(false);
        if (Visible(cancel)) throw new Exception("Cancel dictation remained visible after completion.");
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 108, 0);
        if (cancelRequests != 1) throw new Exception("A stale cancel action was accepted after completion.");
        if (Visible(signIn) != (config.Backend == "chatgpt_oauth") || Visible(signOut))
            throw new Exception("Signed-out auth controls do not match the selected backend.");
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 102, 0);
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 103, 0);
        if (signInRequests != (config.Backend == "chatgpt_oauth" ? 1 : 0) || signOutRequests != 0)
            throw new Exception("Hidden or stale signed-out authentication action was accepted.");
        desktop.RequestAuthentication(ChatGptAccountAction.Reconnect, "chatgpt_oauth");
        signedIn = true;
        desktop.RefreshAuthentication();
        if (Visible(signIn) || Visible(signOut) != (config.Backend == "chatgpt_oauth"))
            throw new Exception("Signed-in auth controls show the wrong action.");
        authBusy = true;
        desktop.RefreshAuthentication();
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 103, 0);
        desktop.RequestAuthentication(ChatGptAccountAction.Reconnect, "chatgpt_oauth");
        if (signInRequests != (config.Backend == "chatgpt_oauth" ? 1 : 0)) throw new Exception("Invalid reconnect was accepted.");
        if (IsWindowEnabled(signOut) != 0 || signOutRequests != 0) throw new Exception("Busy authentication must be disabled.");
        authBusy = false;
        desktop.RefreshAuthentication();
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 102, 0);
        desktop.RequestAuthentication(ChatGptAccountAction.Reconnect, "chatgpt_oauth");
        desktop.RequestAuthentication(ChatGptAccountAction.SignOut, "chatgpt_oauth");
        if (signInRequests != (config.Backend == "chatgpt_oauth" ? 2 : 0) || signOutRequests != (config.Backend == "chatgpt_oauth" ? 1 : 0))
            throw new Exception("Signed-in authentication offered the wrong action.");
        var backend = GetDlgItem(desktop.SettingsHandle, 1002);
        Win32.SendMessageW(backend, 0x14E, 2, 0); // Embedded
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, (1u << 16) | 1002, backend);
        if (Visible(signIn) || Visible(signOut)) throw new Exception("Other backends must not show ChatGPT authentication.");
        var localModelChoice = GetDlgItem(desktop.SettingsHandle, 1003);
        Win32.SendMessageW(localModelChoice, 0x14E, 2, 0);
        desktop.ApplySettings();
        if (applied?.Backend != "embedded" || applied.RefinementModel != LocalAiModels.All[1].Id) throw new Exception("Local friendly model choice did not persist the selected larger model ID.");
        var requests = signInRequests + signOutRequests;
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 102, 0);
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 103, 0);
        desktop.RequestAuthentication(ChatGptAccountAction.Reconnect, "chatgpt_oauth");
        if (requests != signInRequests + signOutRequests) throw new Exception("Another backend invoked ChatGPT authentication.");
        Win32.SendMessageW(backend, 0x14E, 1, 0); // ChatGPT
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, (1u << 16) | 1002, backend);
        desktop.SetAvailableModels([("gpt-6-luna", "GPT-6 Luna (gpt-6-luna) · Recommended", "Efficient transformations.")]);
        var model = GetDlgItem(desktop.SettingsHandle, 1003);
        Win32.SendMessageW(model, 0x14E, 1, 0);
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 101, 0);
        if (applied?.RefinementModel != "gpt-6-luna") throw new Exception("Model picker saved a display label instead of the model ID.");
        desktop.SetResolvedModel("chatgpt_oauth", "gpt-6-luna");
        Win32.SendMessageW(model, 0x14E, 0, 0);
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 101, 0);
        if (applied?.RefinementModel != "auto") throw new Exception("Recommended auto label did not preserve the auto policy.");
        Win32.SetWindowTextW(model, "my-explicit-model");
        desktop.SetAvailableModels([("gpt-6-luna", "GPT-6 Luna (gpt-6-luna) · Recommended", "Efficient transformations.")]);
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 101, 0);
        if (applied?.RefinementModel != "my-explicit-model") throw new Exception("Refreshing models lost a typed explicit model.");
        var claudeSignedIn = false;
        var claudeIn = 0; var claudeOut = 0;
        desktop.IsClaudeSignedIn = () => claudeSignedIn;
        desktop.ClaudeSignInRequested = () => claudeIn++;
        desktop.ClaudeSignOutRequested = () => claudeOut++;
        Win32.SendMessageW(backend, 0x14E, 3, 0); // Claude Code
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, (1u << 16) | 1002, backend);
        if (!Visible(signIn) || Visible(signOut)) throw new Exception("Signed-out Claude must offer only sign in.");
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 102, 0);
        if (claudeIn != 1 || requests != signInRequests + signOutRequests) throw new Exception("Claude sign-in routed to the wrong provider.");
        claudeSignedIn = true;
        desktop.RefreshAuthentication();
        if (Visible(signIn) || !Visible(signOut)) throw new Exception("Signed-in Claude must offer its account menu.");
        authBusy = true;
        desktop.RefreshAuthentication();
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 103, 0);
        if (claudeOut != 0 || IsWindowEnabled(signOut) != 0) throw new Exception("Busy Claude account action must be disabled.");
        authBusy = false;
        desktop.RefreshAuthentication();
        desktop.RequestAuthentication(ChatGptAccountAction.Reconnect, "claude_code");
        desktop.RequestAuthentication(ChatGptAccountAction.SignOut, "claude_code");
        if (claudeIn != 2 || claudeOut != 1) throw new Exception("Claude sign-out was not routed.");
        Win32.SendMessageW(model, 0x14E, 1, 0); // Recommended Haiku label
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 101, 0);
        if (applied?.Backend != "claude_code" || applied.RefinementModel != "haiku") throw new Exception("Claude model label was saved instead of its ID.");
        Win32.SetWindowTextW(model, "claude-explicit-version");
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 101, 0);
        if (applied?.RefinementModel != "claude-explicit-version") throw new Exception("Explicit Claude model was lost.");
        var googleOpen = 0; var googleOut = 0;
        desktop.GoogleAccountRequested = signOut => { if (signOut) googleOut++; else googleOpen++; };
        Win32.SendMessageW(backend, 0x14E, 4, 0);
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, (1u << 16) | 1002, backend);
        if (Visible(signIn) || !Visible(signOut)) throw new Exception("Google must show one account control without claiming sign-in status.");
        authBusy = true;
        desktop.RequestAuthentication(ChatGptAccountAction.Reconnect, "gemini_antigravity");
        if (googleOpen != 0) throw new Exception("Busy Google account action was accepted.");
        authBusy = false;
        desktop.RequestAuthentication(ChatGptAccountAction.Reconnect, "gemini_antigravity");
        desktop.RequestAuthentication(ChatGptAccountAction.SignOut, "gemini_antigravity");
        desktop.RequestAuthentication(ChatGptAccountAction.Reconnect, "claude_code");
        if (googleOpen != 1 || googleOut != 1 || claudeIn != 2) throw new Exception("Google account actions crossed providers.");
        Win32.SendMessageW(model, 0x14E, 1, 0);
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 101, 0);
        if (applied?.RefinementModel != "gemini-3.8-flash-medium") throw new Exception("Google model saved its display label.");
        Win32.SetWindowTextW(model, "gemini-explicit-fixture");
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 101, 0);
        if (applied?.Backend != "gemini_antigravity" || applied.RefinementModel != "gemini-explicit-fixture") throw new Exception("Explicit Google model lost.");
        var startup = GetDlgItem(desktop.SettingsHandle, 1020);
        for (var page = 0; page < 6; page++)
        {
            Win32.SendMessageW(tabs, 0x130C, (nuint)page, 0);
            Win32.SendMessageW(desktop.SettingsHandle, 0x4E, 1014, (nint)(&changed));
            if (Visible(activation) != (page == 0) || Visible(GetDlgItem(desktop.SettingsHandle, 1042)) != (page == 0)) throw new Exception("Microphone activation/monitoring belongs only on Audio.");
            if (Visible(minimumWords) != (page == 1)) throw new Exception("Minimum words appeared on the wrong tab.");
            if (Visible(startup) != (page == 5)) throw new Exception("Start on sign-in belongs only on General.");
            if (Visible(GetDlgItem(desktop.SettingsHandle, 1006)) != (page == 4)) throw new Exception("Insertion controls appeared on the wrong tab.");
            var rect = new Win32.Rect();
            Win32.SendMessageW(tabs, 0x130A, (nuint)page, (nint)(&rect));
            Win32.GetClientRect(tabs, out var bounds);
            if (rect.Left < 0 || rect.Right > bounds.Right) throw new Exception("Settings navigation requires horizontal scrolling.");
        }
        Win32.SendMessageW(startup, 0x14E, config.StartOnLogin ? 0u : 1u, 0);
        Win32.SendMessageW(desktop.SettingsHandle, 0x111, 101, 0);
        if (applied?.StartOnLogin != !config.StartOnLogin) throw new Exception("Startup choice was not applied.");
        desktop.Post(() => Win32.PostQuitMessage(0));
        desktop.Run();
        Console.WriteLine("PASS native settings display and unchanged Apply round-trip: " + (custom ? "custom preferences." : "default preferences."));
    }

    [LibraryImport("user32.dll")]
    private static partial int SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowExW(nint parent, nint after, string className, string? title);
    [LibraryImport("user32.dll")]
    private static partial nint GetDlgItem(nint parent, int id);
    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint window, int index);
    [LibraryImport("user32.dll")]
    private static partial int IsWindowEnabled(nint window);
}
