using SharpVoice.Audio;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private nint setupWindow, setupNotice, setupBack, setupNext, setupMic, setupMeter, setupLevel, setupResult, setupPracticeButton;
    private readonly List<nint> setupControls = [], setupPageControls = [];
    private readonly Dictionary<nint, int> setupButtons = [];
    private List<AudioDeviceInfo> setupDevices = [];
    private int setupStep, setupSession;
    private bool setupPreview, setupPreviewPending, setupBusy, setupRecording, setupInSettings;
    public Action? SetupDismissed { get; set; }
    public Action? SetupPracticeRequested { get; set; }
    public Action? SetupCancelRequested { get; set; }
    public Func<bool>? CanConfigureSetup { get; set; }
    public bool SetupOpen => setupWindow != 0;
    public int SetupSession => setupSession;
    public bool SetupPracticeFocused => SetupOpen && setupStep == 3 && Win32.IsWindowVisible(setupWindow) != 0 && foregroundWindow() == setupWindow;
    internal nint SetupHandle => setupWindow;

    public void ShowSetupIfNeeded() { if (!config.SetupFinished) ShowSetup(); }
    public void ShowSetup()
    {
        if (SetupOpen) { Win32.ShowWindow(setupWindow, 5); Win32.SetForegroundWindow(setupWindow); return; }
        if (active || IsAuthenticationBusy?.Invoke() == true) throw new InvalidOperationException("Finish the current operation before opening setup.");
        CloseSettings();
        setupSession++; setupStep = 0; setupBusy = setupRecording = setupInSettings = false;
        Win32.SystemParametersInfoW(0x30, 0, out var area, 0);
        setupWindow = Create(0, "SharpVoice.Native", "SharpVoice · Setup", 0x00CA0000,
            Math.Max(area.Left, area.Left + (area.Right - area.Left - 700) / 2), Math.Max(area.Top, area.Top + (area.Bottom - area.Top - 640) / 2), 700, 640, 0, 0);
        var title = SetupControl("STATIC", "Welcome to SharpVoice", 0, 32, 22, 620, 36);
        Win32.SendMessageW(title, 0x30, (nuint)headingFont, 1);
        setupNotice = SetupControl("STATIC", "", 0, 32, 462, 620, 50);
        SetupButton("Skip Setup", 4801, 32, 530, 150);
        setupBack = SetupButton("Back", 4802, 362, 530, 120);
        setupNext = SetupButton("Next", 4803, 496, 530, 156);
        RenderSetupStep(); ApplyTheme(); ApplyMicrophoneIcon();
        Win32.SetTimer(setupWindow, 7, 100, 0);
        Win32.ShowWindow(setupWindow, 5); Win32.SetForegroundWindow(setupWindow);
    }

    private nint SetupControl(string type, string text, uint style, int x, int y, int width, int height, int id = 0, bool page = false)
    {
        var handle = Create(0, type, text, 0x50000000 | style, x, y, width, height, setupWindow, id);
        Win32.SendMessageW(handle, 0x30, (nuint)font, 1);
        if (type == "EDIT") Win32.SendMessageW(handle, 0xD3, 3, (8 << 16) | 8);
        InstallControlHover(handle, type);
        Win32.SetWindowTheme(handle, theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
        setupControls.Add(handle); if (page) setupPageControls.Add(handle);
        return handle;
    }
    private nint SetupButton(string text, int id, int x, int y, int width, bool page = false)
    {
        var button = SetupControl("BUTTON", text, 0x1000B, x, y, width, 36, id, page);
        setupButtons[button] = id; return button;
    }
    private void SetupText(string text, int y, int height = 60) => SetupControl("STATIC", text, 0, 32, y, 620, height, page: true);
    private unsafe void RenderSetupStep()
    {
        StopSetupPreview();
        foreach (var control in setupPageControls) { setupButtons.Remove(control); setupControls.Remove(control); Win32.DestroyWindow(control); }
        setupPageControls.Clear(); setupMic = setupMeter = setupLevel = setupResult = setupPracticeButton = 0;
        var titles = new[] { "Your Microphone", "Your Shortcuts", "Optional AI Cleanup", "Try Dictation" };
        SetupControl("STATIC", $"STEP {setupStep + 1} OF 4  ·  {titles[setupStep]}", 0, 32, 76, 620, 28, page: true);
        Win32.SetWindowTextW(setupNotice, "You can skip setup at any time. Use Settings for later changes.");
        Win32.EnableWindow(setupBack, setupStep > 0 ? 1 : 0);
        Win32.SetWindowTextW(setupNext, setupStep == 3 ? "Finish Setup" : setupStep == 2 && config.Backend == "none" ? "Skip AI Setup" : "Next");
        if (setupStep == 0)
        {
            SetupText("SharpVoice transcribes locally. Choose a microphone, then test its input level. The microphone test starts only when you click Test Microphone.", 126, 76);
            SetupText("Microphone", 216, 26);
            setupMic = SetupControl("COMBOBOX", "", 0x210213, 32, 248, 620, 220, 4808, true);
            if (Win32.SetWindowSubclass(setupMic, &ComboProcedure, 1, 0) == 0) throw new InvalidOperationException("Could not style the setup microphone selector.");
            try { setupDevices = AudioDeviceCatalog.Enumerate().ToList(); }
            catch { setupDevices = [new("", "Windows Default (System Microphone)")]; }
            if (!setupDevices.Any(d => d.Id == config.AudioDeviceId)) setupDevices.Add(new(config.AudioDeviceId, "Unavailable Microphone (Choose Another)"));
            foreach (var device in setupDevices) AddComboItem(setupMic, device.Id.Length == 0 ? "Windows Default (System Microphone)" : device.Name);
            Win32.SendMessageW(setupMic, 0x14E, (nuint)Math.Max(0, setupDevices.FindIndex(d => d.Id == config.AudioDeviceId)), 0);
            SetupButton("Test Microphone", 4804, 32, 300, 230, true);
            setupMeter = SetupControl("msctls_progress32", "Microphone Input Level", 0, 32, 354, 620, 14, page: true);
            setupLevel = SetupControl("STATIC", "Microphone test is off", 0, 32, 384, 620, 32, page: true);
        }
        else if (setupStep == 1)
        {
            SetupText(config.RecordingGesture == "toggle"
                ? "Focus a text field in any app. Press the dictation shortcut, wait for the start tone, speak, then press again to insert your words."
                : "Focus a text field in any app. Hold the dictation shortcut, wait for the start tone, speak, then release to insert your words.", 126, 76);
            SetupText($"Dictate:  {HotkeyDefinition.DisplayName(config.Hotkey)}\nAsk AI:  {HotkeyDefinition.DisplayName(config.AskHotkey)} (Requires an AI Provider)", 230, 90);
            SetupText(config.RecordingGesture == "toggle" ? "Your gesture is Toggle: press once to start and again to stop." : "Your gesture is Hold to Talk: hold to record and release to stop.", 334, 50);
            SetupButton("Customize Shortcuts", 4805, 32, 402, 250, true);
        }
        else if (setupStep == 2)
        {
            SetupText("AI is optional. Local transcription works without an account. AI cleanup sends text to your chosen provider unless you choose Local AI.", 126, 76);
            SetupText("Current Choice: " + ChoiceLabel("Backend", config.Backend), 232, 58);
            SetupText("Use the existing AI settings to select a provider, sign in or add a key, download a local model, and Test Connection. Apply Settings returns you here.", 306, 76);
            SetupButton("Set Up AI (Optional)", 4806, 32, 402, 250, true);
        }
        else
        {
            var gesture = config.RecordingGesture == "toggle" ? "Press your dictation shortcut to start and again to stop" : "Hold and release your dictation shortcut";
            SetupText("Try saying “SharpVoice helps me write with my voice.” " + gesture + ", or use Start/Stop Recording. Practice stays here: no AI, history, or paste.", 126, 76);
            setupResult = SetupControl("EDIT", "", 0x00A11844, 32, 222, 620, 152, 4810, true);
            setupPracticeButton = SetupButton("Start Recording", 4807, 32, 402, 230, true);
        }
        ApplyTheme(); Win32.InvalidateRect(setupWindow, 0, 1);
    }

    private void ApplySetupMicrophone()
    {
        var index = (int)Win32.SendMessageW(setupMic, 0x147, 0, 0);
        if (index < 0 || index >= setupDevices.Count) throw new InvalidOperationException("Choose a microphone first.");
        var id = setupDevices[index].Id;
        if (id == config.AudioDeviceId) return;
        if (CanConfigureSetup?.Invoke() == false) throw new InvalidOperationException("SharpVoice is getting ready. Please try again in a moment.");
        var next = config with { AudioDeviceId = id };
        ConfigChanged?.Invoke(next); config = next; PopulateDevices(id);
    }
    private void StopSetupPreview()
    {
        setupPreviewPending = false;
        if (setupPreview) { InputPreviewChanged?.Invoke(false); setupPreview = false; }
        if (setupWindow != 0) Win32.SetWindowTextW(Win32.GetDlgItem(setupWindow, 4804), "Test Microphone");
    }
    public void StopSetupMicrophone() => StopSetupPreview();
    private void ToggleSetupPreview()
    {
        if (setupPreview || setupPreviewPending) { StopSetupPreview(); return; }
        ApplySetupMicrophone();
        if (active || paused) throw new InvalidOperationException("Finish recording or resume the microphone, then test again.");
        if (CanConfigureSetup?.Invoke() == false)
        {
            setupPreviewPending = true;
            Win32.SetWindowTextW(setupNotice, "Preparing the selected microphone… The test will start when ready.");
            Win32.SetWindowTextW(Win32.GetDlgItem(setupWindow, 4804), "Cancel Microphone Test");
            return;
        }
        BeginSetupPreview();
    }
    private void BeginSetupPreview()
    {
        setupPreviewPending = false;
        InputPreviewChanged?.Invoke(true); setupPreview = true;
        Win32.SetWindowTextW(Win32.GetDlgItem(setupWindow, 4804), "Stop Microphone Test");
    }
    private void OpenSetupSettings(int page)
    {
        StopSetupPreview(); setupInSettings = true; Win32.ShowWindow(setupWindow, 0);
        ShowSettings(); Win32.SendMessageW(tabControl, 0x130C, (nuint)page, 0); ShowSettingsPage(page);
    }
    private void ResumeSetup()
    {
        if (!SetupOpen || !setupInSettings) return;
        setupInSettings = false; RenderSetupStep(); Win32.ShowWindow(setupWindow, 5); Win32.SetForegroundWindow(setupWindow);
    }
    public void SetSetupPracticeState(bool busy, bool recording = false)
    {
        setupBusy = busy; setupRecording = recording;
        if (!SetupOpen) return;
        Win32.EnableWindow(setupBack, !busy && setupStep > 0 ? 1 : 0); Win32.EnableWindow(setupNext, busy ? 0 : 1);
        if (setupPracticeButton != 0)
        {
            Win32.SetWindowTextW(setupPracticeButton, recording ? "Stop Recording" : busy ? "Working…" : "Start Recording");
            Win32.EnableWindow(setupPracticeButton, !busy || recording ? 1 : 0);
        }
    }
    public void SetSetupPracticeResult(string text, int session)
    {
        if (SetupOpen && setupStep == 3 && setupSession == session) Win32.SetWindowTextW(setupResult, text);
    }
    private void DismissSetup()
    {
        StopSetupPreview(); SetupCancelRequested?.Invoke(); SetupDismissed?.Invoke();
        config = config with { SetupFinished = true }; CloseSetupCore();
    }
    private void CloseSetupCore()
    {
        StopSetupPreview();
        var old = setupWindow; setupWindow = 0; setupSession++; setupInSettings = false;
        if (old != 0) { Win32.KillTimer(old, 7); Win32.DestroyWindow(old); }
        setupControls.Clear(); setupPageControls.Clear(); setupButtons.Clear(); setupNotice = setupResult = 0;
    }
    private bool HandleSetupInput(in Win32.Msg message)
    {
        if (!setupButtons.TryGetValue(message.Hwnd, out var id) || message.Message is not (0x100 or 0x101) || message.WParam is not (13 or 32)) return false;
        if (message.Message == 0x100 && ((long)message.LParam & (1L << 30)) == 0 && HoverIsWindowEnabled(message.Hwnd) != 0)
            Win32.SendMessageW(setupWindow, 0x111, (nuint)id, message.Hwnd);
        return true;
    }
    private bool HandleSetupMessage(nint hwnd, uint message, nuint wParam)
    {
        if (hwnd == 0 || hwnd != setupWindow) return false;
        try
        {
            if ((message == 0x6 && (wParam & 0xFFFF) == 0) || (message == 0x5 && wParam == 1))
            {
                StopSetupPreview();
                if (setupBusy) { setupSession++; SetupCancelRequested?.Invoke(); }
                return false;
            }
            if (message == 0x10) { DismissSetup(); return true; }
            if (message == 0x113 && wParam == 7)
            {
                if (setupStep == 0 && setupLevel != 0)
                {
                    if (setupPreviewPending && CanConfigureSetup?.Invoke() != false) BeginSetupPreview();
                    var level = ReadLevel?.Invoke() ?? float.NaN;
                    var valid = setupPreview && float.IsFinite(level);
                    Win32.SetWindowTextW(setupLevel, !setupPreview ? "Microphone test is off" : !valid ? "Waiting for microphone input…" : $"Input: {level:F0} dBFS · {(level >= config.NoiseGateDb ? "Above" : "Below")} Noise Gate");
                    Win32.SendMessageW(setupMeter, 0x402, (nuint)(valid ? (int)Math.Clamp(level + 100, 0, 100) : 0), 0);
                }
                return true;
            }
            if (message != 0x111) return false;
            var id = (int)(wParam & 0xFFFF); var notification = wParam >> 16;
            if (id == 4808 && notification == 1) { StopSetupPreview(); Win32.SetWindowTextW(Win32.GetDlgItem(setupWindow, 4804), "Test Microphone"); return true; }
            if (notification != 0) return false;
            switch (id)
            {
                case 2: case 4801: DismissSetup(); break;
                case 4802: if (!setupBusy && setupStep > 0) { setupStep--; RenderSetupStep(); } break;
                case 4803:
                    if (setupBusy) break;
                    if (setupStep == 3) DismissSetup();
                    else { if (setupStep == 0) { StopSetupPreview(); ApplySetupMicrophone(); } setupStep++; RenderSetupStep(); }
                    break;
                case 4804: ToggleSetupPreview(); break;
                case 4805: OpenSetupSettings(3); break;
                case 4806: OpenSetupSettings(1); break;
                case 4807: SetupPracticeRequested?.Invoke(); break;
            }
            return true;
        }
        catch (Exception ex) { Win32.SetWindowTextW(setupNotice, ex.Message); return true; }
    }
}
