using System.Globalization;
using SharpVoice.Audio;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private static readonly string[] TabNames = ["Audio", "AI & models", "Prompts", "Hotkeys", "Insertion", "General"];
    private readonly HashSet<nint> editableCombos = [];
    private readonly List<nint>[] pages = [[], [], [], [], [], []];
    private readonly Dictionary<string, string> promptDrafts = [];
    private List<AudioDeviceInfo> audioDevices = [];
    private int buildingPage = -1;
    private nint tabControl, gateSlider, inputMeter, inputLabel, captureLabel;
    private nint signInButton, signOutButton, cancelButton, monitoringIndicator;
    private bool inputPreview, inputMonitoringFailed, settingsMinimized;
    private string modelBackend = "";
    public Action<bool>? InputPreviewChanged { get; set; }
    // The app owns recorder readiness and spoken-answer playback. Query it before requesting capture.
    public Func<bool>? CanMonitorInput { get; set; }
    public void StopInputPreview() => SetInputPreview(false);
    private void RefreshInputMonitoring()
    {
        if (monitoringIndicator == 0) return;
        var eligible = !disposed && !active && !paused && !setupPreview && !settingsMinimized
            && Win32.IsWindowVisible(settingsWindow) != 0
            && Win32.SendMessageW(tabControl, 0x130B, 0, 0) == 0
            && CanMonitorInput?.Invoke() != false;
        if (!eligible) inputMonitoringFailed = false;
        if (!eligible) SetInputPreview(false);
        else if (!inputMonitoringFailed)
        {
            try { SetInputPreview(true); }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                // A disconnected/failed device must not trigger an error on every timer tick.
                inputMonitoringFailed = true;
            }
        }
        Win32.SetWindowTextW(monitoringIndicator, inputPreview ? "● Monitoring applied mic"
            : inputMonitoringFailed ? "Monitoring unavailable" : "Monitoring suspended");
    }
    private void SetInputPreview(bool enabled)
    {
        if (inputPreview == enabled) return;
        if (enabled && (active || paused || Win32.IsWindowVisible(settingsWindow) == 0 || Win32.SendMessageW(tabControl, 0x130B, 0, 0) != 0)) return;
        InputPreviewChanged?.Invoke(enabled);
        inputPreview = enabled;
    }
    private string editingPromptMode = "prose";
    private bool capturingShortcut;

    private unsafe void BuildSettings()
    {
        modelBackend = config.Backend;
        var controls = new Win32.CommonControls { Size = (uint)sizeof(Win32.CommonControls), Classes = 0x2C };
        if (Win32.InitCommonControlsEx(in controls) == 0) throw new InvalidOperationException("Could not initialize Windows settings controls.");
        var title = Control("STATIC", "SharpVoice", 0, 32, 20, 650, 36);
        Win32.SendMessageW(title, 0x30, (nuint)headingFont, 1);
        Control("STATIC", "Your voice. Your words. Wherever you work.", 0, 32, 62, 650, 24);
        statusControl = Control("STATIC", SettingsStatus(status, false), 0, 32, 99, 605, 38, 1024);
        AddHelp("Status", "Status and Shortcuts", 1900, 99);
        tabControl = Control("SysTabControl32", "", 0x2900 | 0x10000, 24, 145, 680, 36, 1014);
        // Size each tab to its label, reserving room for its icon without scrolling.
        Win32.SendMessageW(tabControl, 0x132B, 0, (4 << 16) | 20);
        Win32.SendMessageW(tabControl, 0x1329, 0, 32 << 16);
        if (Win32.SetWindowSubclass(tabControl, &TabProcedure, 1, 0) == 0) throw new InvalidOperationException("Could not style Settings navigation.");
        for (var i = 0; i < TabNames.Length; i++)
            fixed (char* text = TabNames[i].Replace("&", "&&", StringComparison.Ordinal))
            {
                var item = new Win32.TabItem { Mask = 1, Text = text };
                if (Win32.SendMessageW(tabControl, 0x133E, (nuint)i, (nint)(&item)) == -1) throw new InvalidOperationException("Could not create Settings tab.");
            }

        buildingPage = 0;
        AddCombo("Microphone", "AudioDevice", 196, ["Windows Default (System Microphone)"], "Windows Default (System Microphone)", 1009);
        Button("Wake Phrases", 121, 40, 234, 176);
        AddHelp("WakePhrases", "Wake Phrases", 121, 237, 218);
        Button("Refresh Microphones", 105, 250, 234, 210);
        monitoringIndicator = Control("STATIC", "Monitoring suspended", 0, 475, 242, 200, 25, 1042);
        AddChoice("Mic Activation", "MicrophoneActivation", 294, config.MicrophoneActivation, 1022);
        inputLabel = Control("STATIC", "INPUT LEVEL · Waiting", 0, 40, 350, 635, 25, 1018);
        inputMeter = Control("msctls_progress32", "Input level with noise gate marker", 0, 40, 380, 635, 22, 1016);
        if (Win32.SetWindowSubclass(inputMeter, &MeterProcedure, 1, 0) == 0) throw new InvalidOperationException("Could not style the noise gate meter.");
        AddEdit("Noise Gate · dB", "NoiseGateDb", 425, config.NoiseGateDb.ToString(CultureInfo.InvariantCulture), 1008);
        gateSlider = Control("msctls_trackbar32", "", 0x10000 | 0x10, 244, 467, 391, 28, 1015);
        Win32.SendMessageW(gateSlider, 0x407, 0, -90);
        Win32.SendMessageW(gateSlider, 0x408, 1, 0);
        Win32.SendMessageW(gateSlider, 0x405, 1, (nint)(int)config.NoiseGateDb);

        AddChoice("Live Transcription", "LiveTranscription", 510, config.LiveTranscription ? "on" : "off", 1040);

        buildingPage = 1;
        AddChoice("Whisper Model", "SpeechModel", 196, config.SpeechModel, 1001);
        AddChoice("Speech Processing", "SpeechProcessing", 234, config.FastTranscription ? "fast" : "standard", 1041);
        AddChoice("AI Refinement", "Backend", 272, config.Backend, 1002);
        AddCombo("AI Model", "RefinementModel", 310, ModelSuggestions(config.Backend), config.RefinementModel, 1003, editable: true);
        AddEdit("Minimum Words for AI", "MinimumAiWords", 348, config.MinimumAiWords.ToString(CultureInfo.InvariantCulture), 1021);
        modelHint = Control("STATIC", "", 0, 40, 386, 635, 23, 1017);
        retentionNotice = Control("STATIC", "Antigravity may retain history.", 0, 40, 435, 635, 20, 1023);
        connectionStatus = Control("STATIC", "", 0, 40, 411, 635, 22, 1025);
        connectionTestButton = Button("Test Connection", 112, 250, 456, 175);
        if (Win32.SetWindowSubclass(connectionTestButton, &ConnectionProcedure, 1, 0) == 0) throw new InvalidOperationException("Could not initialize the connection-test action.");
        AddHelp("ConnectionTest", "Test Connection", 112, 459, 435);
        localDownloadButton = Button("Download Model", 113, 40, 456, 175);
        if (Win32.SetWindowSubclass(localDownloadButton, &LocalDownloadProcedure, 1, 0) == 0) throw new InvalidOperationException("Could not initialize model download action.");
        AddHelp("LocalDownload", "Download Local Model", 113, 459, 220);
        localDownloadBar = Control("msctls_progress32", "Local Model Download Progress", 0, 40, 435, 635, 12, 1026);
        refreshModelsButton = Button("Refresh Models", 109, 465, 456, 210);
        signInButton = Button("ChatGPT Sign In", 102, 40, 456, 200);
        signOutButton = Button("ChatGPT Sign Out", 103, 40, 456, 200);

        var diagnosticsButton = Button("Diagnostics", 124, 40, 500, 200);
        if (Win32.SetWindowSubclass(diagnosticsButton, &ConnectionProcedure, 1, 0) == 0) throw new InvalidOperationException("Could not initialize diagnostics action.");

        buildingPage = 2;
        AddChoice("Writing Mode", "Mode", 196, config.Mode, 1004);
        Control("STATIC", "General Instructions (Optional)", 0, 40, 244, 590, 24);
        AddHelp("CustomPrompt", "General Instructions", 1011, 241);
        fields["CustomPrompt"] = Control("EDIT", config.CleanupPrompt.Length == 0 ? config.CustomPrompt : config.CleanupPrompt, 0x00A11044, 40, 280, 635, 78, 1011);
        AddChoice("Instructions For", "PromptMode", 384, "prose", 1013);
        promptDrafts["prose"] = config.ProsePrompt; promptDrafts["email"] = config.EmailPrompt;
        promptDrafts["code"] = config.CodePrompt; promptDrafts["casual"] = config.CasualPrompt;
        fields["ModePrompt"] = Control("EDIT", config.ProsePrompt, 0x00A11044, 40, 427, 635, 78, 1012);

        buildingPage = 3;
        string[] presets = ["Ctrl + Alt", "Ctrl + Alt + Shift", .. Enumerable.Range(1, 24).Select(x => $"F{x}"), HotkeyDefinition.DisplayName(config.Hotkey), HotkeyDefinition.DisplayName(config.AskHotkey)];
        var shortcuts = presets.Distinct().ToArray();
        AddCombo("Hold to Dictate", "Hotkey", 196, shortcuts, HotkeyDefinition.DisplayName(config.Hotkey), 1005);
        Button("Record Shortcut", 107, 250, 237, 195);
        AddCombo("Hold to Ask AI", "AskHotkey", 300, shortcuts, HotkeyDefinition.DisplayName(config.AskHotkey), 1019);
        Button("Record AI Shortcut", 110, 250, 341, 215);
        captureLabel = Control("STATIC", "", 0, 40, 392, 635, 40);

        AddChoice("Recording Gesture", "RecordingGesture", 440, config.RecordingGesture, 1027);
        AddEdit("Silence Auto-Stop · sec", "SilenceStopSeconds", 486, config.SilenceStopSeconds.ToString(CultureInfo.InvariantCulture), 1028);

        buildingPage = 4;
        AddChoice("Insert Text Using", "Injection", 196, config.Injection, 1006);
        AddEdit("Insertion Delay · ms", "PasteDelayMs", 252, config.PasteDelayMs.ToString(CultureInfo.InvariantCulture), 1007);
        AddEdit("Recording Limit · sec", "MaxRecordingSeconds", 308, config.MaxRecordingSeconds.ToString(CultureInfo.InvariantCulture), 1010);

        buildingPage = 5;
        AddChoice("Start on Sign-In", "StartOnLogin", 196, config.StartOnLogin ? "On" : "Off", 1020);

        AddChoice("History Storage", "PersistHistory", 236, config.PersistHistory ? "saved" : "memory", 1029);
        Button("Updates", 122, 40, 282, 285);
        Button("Personal Dictionary", 114, 40, 328, 285);
        Button("Voice Snippets", 115, 350, 328, 285);
        Button("App Profiles", 116, 40, 374, 285);
        Button("Recent Dictations", 117, 350, 374, 285);
        Button("Plugins", 118, 40, 420, 285);
        Button("Smart Ask AI", 119, 350, 420, 285);
        Button("Connected Ask AI", 120, 40, 466, 285);

        buildingPage = -1;
        Control("STATIC", "LAST RESULT", 0, 32, 550, 605, 22);
        AddHelp("Turnaround", "Last Dictation Timing", 104, 550);
        previewControl = Control("EDIT", "", 0x00A11844, 32, 578, 664, 92);
        Button("Apply Settings", 101, 32, 694, 176);
        cancelButton = Button("Cancel dictation", 108, 276, 694, 200);
        Win32.ShowWindow(cancelButton, active ? 5 : 0);
        Button("Copy Last Text", 104, 520, 694, 176);
        if (config.Backend == "chatgpt_oauth") SetAvailableModels([]);
        else if (config.Backend == "claude_code") SetClaudeModels();
        else if (config.Backend == "gemini_api") SetGoogleApiModels();
        else if (config.Backend == "gemini_antigravity") SetGoogleModels();
        else if (config.Backend == "embedded") SetEmbeddedModels();
        else SetTranscriptionOnlyModel();
        PopulateDevices(config.AudioDeviceId);
        ShowSettingsPage(0);
        RefreshInputMeter();
    }

    private nint Button(string text, int id, int x, int y, int width) => Control("BUTTON", text, 0x1000B, x, y, width, 36, id);
    private void AddEdit(string label, string key, int y, string value, int id)
    {
        Control("STATIC", label, 0, 40, y + 5, 200, 25);
        fields[key] = Control("EDIT", value, 0x10080, 250, y, 385, 31, id);
        AddHelp(key, label, id, y);
    }
    private unsafe void AddCombo(string label, string key, int y, string[] options, string value, int id, bool editable = false)
    {
        Control("STATIC", label, 0, 40, y + 5, 200, 25);
        var hwnd = Control("COMBOBOX", "", (editable ? 0x210042u : 0x210003u) | 0x210u, 250, y, 385, 220, id);
        AddHelp(key, label, id, y);
        if (editable) { editableCombos.Add(hwnd); InstallComboEditHover(hwnd); }
        if (Win32.SetWindowSubclass(hwnd, &ComboProcedure, 1, 0) == 0) throw new InvalidOperationException("Could not style Settings dropdown.");
        fields[key] = hwnd;
        foreach (var option in options) AddComboItem(hwnd, option);
        if (editable) Win32.SetWindowTextW(hwnd, value);
        else Win32.SendMessageW(hwnd, 0x14E, (nuint)Math.Max(0, Array.IndexOf(options, value)), 0);
    }
    private static unsafe void AddComboItem(nint hwnd, string value)
    {
        fixed (char* text = value) Win32.SendMessageW(hwnd, 0x143, 0, (nint)text);
    }
    private static unsafe string Text(nint hwnd)
    {
        var length = Win32.GetWindowTextLengthW(hwnd);
        var chars = new char[length + 1];
        fixed (char* pointer = chars) Win32.GetWindowTextW(hwnd, pointer, chars.Length);
        return new string(chars, 0, length);
    }
    private void ShowSettingsPage(int page)
    {
        if (page < 0 || page >= pages.Length) return;
        HideHelp();
        ClearControlHover();
        for (var i = 0; i < pages.Length; i++) foreach (var hwnd in pages[i]) Win32.ShowWindow(hwnd, i == page ? 5 : 0);
        RefreshInputMonitoring();
        if (page != 1) CancelConnectionTest();
        if (page != 3) CancelShortcutCapture();
        RefreshAuthentication();
        RefreshModelHint();
        Win32.InvalidateRect(settingsWindow, 0, 1);
    }
    public void RefreshAuthentication()
    {
        RefreshConnectedAccount();
        if (signInButton == 0 || signOutButton == 0 || !fields.TryGetValue("Backend", out var backend)) return;
        var provider = SelectedChoice("Backend");
        var action = RefinementAuthentication.ActionFor(provider, AccountSignedIn(provider));
        Win32.SetWindowTextW(signInButton, provider == "gemini_api" ? "Manage API Key" : provider == "claude_code" ? "Claude Sign In" : "ChatGPT Sign In");
        Win32.SetWindowTextW(signOutButton, provider == "gemini_antigravity" ? "Google Account ▾" : provider == "claude_code" ? "Claude Account ▾" : "ChatGPT Account ▾");
        var onModelsTab = Win32.SendMessageW(tabControl, 0x130B, 0, 0) == 1;
        Win32.ShowWindow(signInButton, onModelsTab && (provider == "gemini_api" || action == ChatGptAccountAction.SignIn) ? 5 : 0);
        Win32.ShowWindow(signOutButton, onModelsTab && action == ChatGptAccountAction.ManageAccount ? 5 : 0);
        var enabled = IsAuthenticationBusy?.Invoke() != true ? 1 : 0;
        Win32.EnableWindow(signInButton, enabled);
        Win32.EnableWindow(signOutButton, enabled);
        RefreshConnectionTestControls();
    }
    private bool AccountSignedIn(string backend) => backend == "claude_code" ? IsClaudeSignedIn?.Invoke() ?? false : IsChatGptSignedIn?.Invoke() ?? false;
    internal void RequestAuthentication(ChatGptAccountAction requested, string backend)
    {
        if (backend != SelectedChoice("Backend") || !RefinementAuthentication.CanRequest(backend, AccountSignedIn(backend), IsAuthenticationBusy?.Invoke() == true, requested)) return;
        if (backend == "gemini_antigravity")
        {
            GoogleAccountRequested?.Invoke(requested == ChatGptAccountAction.SignOut);
        }
        else if (backend == "claude_code")
        {
            if (requested is ChatGptAccountAction.SignIn or ChatGptAccountAction.Reconnect) ClaudeSignInRequested?.Invoke();
            else if (requested == ChatGptAccountAction.SignOut) ClaudeSignOutRequested?.Invoke();
        }
        else if (requested is ChatGptAccountAction.SignIn or ChatGptAccountAction.Reconnect) SignInRequested?.Invoke();
        else if (requested == ChatGptAccountAction.SignOut) SignOutRequested?.Invoke();
    }
    private string SelectedDeviceId()
    {
        var index = (int)Win32.SendMessageW(fields["AudioDevice"], 0x147, 0, 0);
        return index >= 0 && index < audioDevices.Count ? audioDevices[index].Id : config.AudioDeviceId;
    }
    private void PopulateDevices(string preserve)
    {
        try { audioDevices = AudioDeviceCatalog.Enumerate().ToList(); }
        catch (Exception) { audioDevices = [new("", "Windows default microphone")]; }
        if (!audioDevices.Any(x => x.Id == preserve)) audioDevices.Add(new(preserve, "Unavailable Microphone (Reconnect or Choose Another)"));
        var combo = fields["AudioDevice"];
        Win32.SendMessageW(combo, 0x14B, 0, 0);
        foreach (var device in audioDevices) AddComboItem(combo, device.Id.Length == 0 ? "Windows Default (System Microphone)" : device.Name);
        Win32.SendMessageW(combo, 0x14E, (nuint)audioDevices.FindIndex(x => x.Id == preserve), 0);
    }
    private static string[] ModelSuggestions(string backend) => backend switch { "gemini_api" => ["auto", "gemini-3.1-flash-lite", "gemini-3.5-flash-lite", "gemini-3.8-flash"], "gemini_antigravity" => ["auto", "gemini-3.8-flash-medium", "gemini-4-argon-medium"], "claude_code" => ["auto", "haiku", "sonnet", "opus"], "embedded" => ["auto", .. LocalAiModels.All.Select(x => x.Id)], _ => ["auto"] };
    private void BackendChanged()
    {
        var backend = SelectedChoice("Backend");
        var model = SelectedModelId();
        if ((backend == "gemini_api") != (modelBackend == "gemini_api")) model = "auto";
        modelBackend = backend;
        if (backend is ("gemini_antigravity" or "gemini_api") && !model.StartsWith("gemini-", StringComparison.Ordinal)) model = "auto";
        if (backend == "chatgpt_oauth" && (model.StartsWith("gemini-", StringComparison.Ordinal) || config.Backend == "claude_code" || model.StartsWith("qwen2.5-", StringComparison.Ordinal) || model is "gpt-5.1-codex" or "haiku" or "sonnet" or "opus" || model.StartsWith("claude-", StringComparison.Ordinal))) model = "auto";
        if (backend == "claude_code" && !(model is "auto" or "haiku" or "sonnet" or "opus" || model.StartsWith("claude-", StringComparison.Ordinal))) model = "auto";
        if (backend == "embedded" && LocalAiModels.Find(model) is null) model = "auto";
        var combo = fields["RefinementModel"];
        Win32.SendMessageW(combo, 0x14B, 0, 0);
        modelLabels.Clear(); modelDescriptions.Clear();
        foreach (var option in ModelSuggestions(backend)) AddComboItem(combo, option);
        Win32.SetWindowTextW(combo, model);
        if (backend == "chatgpt_oauth") SetAvailableModels([]);
        else if (backend == "claude_code") SetClaudeModels();
        else if (backend == "gemini_api") SetGoogleApiModels();
        else if (backend == "gemini_antigravity") SetGoogleModels();
        else if (backend == "embedded") SetEmbeddedModels();
        else SetTranscriptionOnlyModel();
        RefreshAuthentication(); RefreshModelHint(); RequestModelsRefresh();
    }
    private void SavePromptDraft() => promptDrafts[editingPromptMode] = Text(fields["ModePrompt"]);
    internal void ApplySettings()
    {
        if (active || capturingShortcut) throw new InvalidOperationException("Finish or cancel dictation or shortcut recording before applying settings.");
        SavePromptDraft();
        var next = config with
        {
            Hotkey = HotkeyDefinition.FromDisplayName(Text(fields["Hotkey"])),
            AskHotkey = HotkeyDefinition.FromDisplayName(Text(fields["AskHotkey"])),
            LiveTranscription = SelectedChoice("LiveTranscription") == "on",
            FastTranscription = SelectedChoice("SpeechProcessing") == "fast",
            AudioDeviceId = SelectedDeviceId(), MicrophoneActivation = SelectedChoice("MicrophoneActivation"),
            RecordingGesture = SelectedChoice("RecordingGesture"),
            SilenceStopSeconds = int.Parse(Text(fields["SilenceStopSeconds"]), CultureInfo.InvariantCulture),
            PersistHistory = SelectedChoice("PersistHistory") == "saved",
            SpeechModel = SelectedChoice("SpeechModel"), Backend = SelectedChoice("Backend"),
            RefinementModel = SelectedModelId(), Mode = SelectedChoice("Mode"),
            MinimumAiWords = int.Parse(Text(fields["MinimumAiWords"]), CultureInfo.InvariantCulture),
            NoiseGateDb = float.Parse(Text(fields["NoiseGateDb"]), CultureInfo.InvariantCulture),
            StartOnLogin = SelectedChoice("StartOnLogin") == "On",
            Injection = SelectedChoice("Injection"), PasteDelayMs = int.Parse(Text(fields["PasteDelayMs"]), CultureInfo.InvariantCulture),
            MaxRecordingSeconds = int.Parse(Text(fields["MaxRecordingSeconds"]), CultureInfo.InvariantCulture),
            CleanupPrompt = config.CleanupPrompt.Length == 0 ? "" : Text(fields["CustomPrompt"]),
            CustomPrompt = config.CleanupPrompt.Length == 0 ? Text(fields["CustomPrompt"]) : config.CustomPrompt,
            ProsePrompt = promptDrafts["prose"], EmailPrompt = promptDrafts["email"], CodePrompt = promptDrafts["code"], CasualPrompt = promptDrafts["casual"]
        };
        next.Validate();
        ConfigChanged?.Invoke(next);
        if (config.Backend != next.Backend || config.RefinementModel != next.RefinementModel) { resolvedBackend = ""; resolvedModel = ""; modelWasUsed = false; }
        config = next;
        RefreshModelHint();
        hook?.Configure(config.Hotkey, config.AskHotkey, config.RecordingGesture == "toggle");
        CloseSettings();
    }
    private void StartShortcutCapture(string field = "Hotkey")
    {
        if (active) throw new InvalidOperationException("Finish dictation before recording a shortcut.");
        capturingShortcut = true;
        Win32.SetWindowTextW(captureLabel, field == "AskHotkey" ? "Record Ask AI shortcut: press keys, then release all. Escape cancels." : "Record dictation shortcut: press keys, then release all. Escape cancels.");
        hook!.BeginCapture(key => Post(() =>
        {
            capturingShortcut = false;
            if (key is null) { Win32.SetWindowTextW(captureLabel, "Shortcut recording cancelled or unsupported. Your previous shortcut is unchanged."); return; }
            var name = HotkeyDefinition.DisplayName(key.Value);
            var combo = fields[field];
            AddComboItem(combo, name);
            var count = Win32.SendMessageW(combo, 0x146, 0, 0);
            Win32.SendMessageW(combo, 0x14E, (nuint)(count - 1), 0);
            Win32.SetWindowTextW(captureLabel, "Recorded " + (field == "AskHotkey" ? "Ask AI: " : "dictation: ") + name + ". Apply Settings to use it.");
        }));
    }
    private void CancelShortcutCapture()
    {
        if (!capturingShortcut) return;
        hook?.CancelCapture();
        capturingShortcut = false;
        Win32.SetWindowTextW(captureLabel, "Shortcut recording cancelled. Your previous shortcut is unchanged.");
    }
    private void CloseSettings()
    {
        CloseTools();
        ClearControlHover();
        HideHelp();
        CancelConnectionTest();
        StopInputPreview();
        CancelShortcutCapture();
        Win32.KillTimer(settingsWindow, 3);
        Win32.ShowWindow(settingsWindow, 0);
        ResumeSetup();
    }
    private unsafe bool HandleSettingsMessage(uint message, nuint wParam, nint lParam)
    {
        if (message == 0x5) { settingsMinimized = wParam == 1; RefreshInputMonitoring(); }
        if (message == 0x18 && wParam == 0) { StopInputPreview(); inputMonitoringFailed = false; }
        if ((message == 0x6 && (wParam & 0xFFFF) == 0) || message is 0x3 or 0x5 or 0x216) { HideHelp(); ClearControlHover(); }
        if (message == 0x111 && (wParam >> 16) == 0 && helpEntries.ContainsKey((int)(wParam & 0xFFFF)))
        { ToggleHelp((int)(wParam & 0xFFFF)); return true; }
        if (message == 0x4E && lParam != 0)
        {
            var header = (Win32.Notification*)lParam;
            if (header->Window == tabControl && header->Code == -551) { ShowSettingsPage((int)Win32.SendMessageW(tabControl, 0x130B, 0, 0)); return true; }
        }
        if (message == 0x114 && lParam == gateSlider)
        {
            Win32.SetWindowTextW(fields["NoiseGateDb"], ((int)Win32.SendMessageW(gateSlider, 0x400, 0, 0)).ToString(CultureInfo.InvariantCulture));
            return true;
        }
        if (message == 0x113 && wParam == 3)
        {
            RefreshInputMonitoring();
            RefreshInputMeter();
            return true;
        }
        if (message != 0x111) return false;
        var id = (int)(wParam & 0xFFFF);
        var notification = wParam >> 16;
        if (notification == 1 && id == 1013)
        {
            SavePromptDraft(); editingPromptMode = SelectedChoice("PromptMode");
            Win32.SetWindowTextW(fields["ModePrompt"], promptDrafts[editingPromptMode]); return true;
        }
        if ((notification == 1 || notification == 5) && id == 1003) { RefreshModelHint(); return true; }
        if (notification == 0 && id == 113) { RequestLocalDownload(); return true; }
        if (notification == 0 && id == 124) { OpenProviderDiagnostics(); return true; }
        if (notification == 0 && id == 112) { RequestConnectionTest(); return true; }
        if (notification == 0 && id == 109) { RequestModelsRefresh(); return true; }
        if (notification == 1 && id == 1002) { BackendChanged(); return true; }
        if (notification == 0x300 && id == 1008 && gateSlider != 0 && fields.TryGetValue("NoiseGateDb", out var edit))
        {
            if (float.TryParse(Text(edit), CultureInfo.InvariantCulture, out var value) && value is >= -90 and <= 0)
                Win32.SendMessageW(gateSlider, 0x405, 1, (nint)(int)value);
            RefreshInputMeter();
            return true;
        }
        if (notification == 0 && id == 105) { PopulateDevices(SelectedDeviceId()); return true; }
        if (notification == 0 && id == 108) { if (active) CancelRequested?.Invoke(); return true; }
        if (notification == 0 && id == 110) { StartShortcutCapture("AskHotkey"); return true; }
        if (notification == 0 && id == 107) { StartShortcutCapture(); return true; }
        return false;
    }
}
