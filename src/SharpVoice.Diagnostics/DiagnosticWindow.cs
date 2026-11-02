using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using SharpVoice.Audio;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.Diagnostics;

internal sealed class DiagnosticWindow : IDisposable
{
    private static DiagnosticWindow? current;
    private readonly IDiagnosticController session;
    private readonly ConcurrentQueue<Action> pending = new();
    private readonly Dictionary<int, nint> controls = [];
    private IReadOnlyList<AudioDeviceInfo> devices = [];
    private readonly nint font = Win32.CreateFontW(-16, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
    private nint window;
    private int tasks;
    private bool closing, disposed, exporting;
    private long closingAt;
    private string notice = "", oldEvents = "";
    private string speechModel = "tiny.en-q5_1";
    internal nint Handle => window;
    internal DiagnosticWindow(IDiagnosticController session) => this.session = session;
    internal unsafe void Initialize(bool show = true)
    {
        current = this;
        var common = new Win32.CommonControls { Size = (uint)sizeof(Win32.CommonControls), Classes = 0xFF };
        Win32.InitCommonControlsEx(in common);
        fixed (char* name = "SharpVoice.WakeDiagnostics")
        {
            var wc = new Win32.WindowClass { Size = (uint)sizeof(Win32.WindowClass), Instance = Win32.GetModuleHandleW(null),
                ClassName = name, Proc = &Procedure, Cursor = Win32.LoadCursorW(0, 32512), Background = 16,
                Icon = Win32.LoadIconW(0, 32516), SmallIcon = Win32.LoadIconW(0, 32516) };
            if (Win32.RegisterClassExW(in wc) == 0) throw new InvalidOperationException("Cannot register diagnostic window.");
        }
        Win32.SystemParametersInfoW(0x30, 0, out var area, 0);
        window = Win32.CreateWindowExW(0, "SharpVoice.WakeDiagnostics", "SharpVoice · Wake Diagnostics", 0x00CA0000,
            Math.Max(area.Left, (area.Right - 1000) / 2), Math.Max(area.Top, (area.Bottom - 720) / 2), 1000, 720, 0, 0, Win32.GetModuleHandleW(null), 0);
        if (window == 0) throw new InvalidOperationException("Cannot create diagnostic window.");
        Add(123, "STATIC", "SharpVoice Wake Diagnostics", 0, 16, 12, 950, 24);
        Label("1. Exit SharpVoice.  2. Start listening.  3. Run both phrase tests several times.  4. Export the ZIP report.\r\nReports contain technical events; accounts, prompts and transcripts are never collected.", 16, 40, 950, 42);
        Label("Microphone", 16, 89, 130, 24);
        Add(101, "COMBOBOX", "", 0x210203, 160, 85, 626, 250);
        Button(102, "Refresh Microphones", 800, 85, 166);
        Label("Ask AI Phrase", 16, 130, 140, 24); Add(103, "EDIT", "Hey SharpVoice", 0x810080, 160, 126, 806, 30);
        Label("Dictation Phrase", 16, 170, 140, 24); Add(104, "EDIT", "Hey SharpVoice Dictate", 0x810080, 160, 166, 806, 30);
        Label("Gate · dB", 16, 211, 85, 24); Add(105, "EDIT", "-65", 0x810080, 105, 207, 70, 30);
        Label("Silence · Seconds", 200, 211, 150, 24); Add(106, "EDIT", "2", 0x810080, 355, 207, 60, 30);
        Add(107, "BUTTON", "Keep Microphone Ready", 0x10003, 455, 207, 235, 30);
        Add(108, "BUTTON", "Save Test Audio (Optional)", 0x10003, 705, 207, 261, 30);
        Button(110, "Start Listening", 16, 252, 155); Button(111, "Stop", 180, 252, 115);
        Button(112, "Test Ask AI", 310, 252, 145); Button(113, "Test Dictation", 465, 252, 150);
        Button(114, "Room Noise Test", 625, 252, 165); Button(115, "Finish Attempt", 800, 252, 166);
        Add(120, "STATIC", "", 0, 16, 297, 770, 46); Button(116, "Mark Missed", 800, 297, 166);
        Add(121, "EDIT", "", 0xA00844, 16, 351, 950, 103);
        Add(122, "EDIT", "", 0xA00844, 16, 464, 950, 153);
        Win32.SendMessageW(controls[122], 0xC5, 65535, 0);
        Button(117, "Export Report ZIP", 16, 630, 180); Button(118, "Open Log Folder", 210, 630, 170); Button(119, "Close", 840, 630, 126);
        Label("Audio is off by default. If enabled, guided tests save microphone clips (up to 20 seconds each).", 395, 632, 430, 40);
        LoadPreferences(); Win32.SetWindowTextW(controls[123], "SharpVoice Wake Diagnostics · Whisper " + speechModel); RefreshDevices(); Render();
        Win32.SetTimer(window, 1, 200, 0);
        if (show) Win32.ShowWindow(window, 5);
    }
    internal int Run()
    {
        Initialize();
        while (Win32.GetMessageW(out var msg, 0, 0, 0) > 0)
            if (Win32.IsDialogMessageW(window, msg) == 0) { Win32.TranslateMessage(msg); Win32.DispatchMessageW(msg); }
        return 0;
    }
    private nint Add(int id, string type, string text, uint style, int x, int y, int width, int height)
    {
        var control = Win32.CreateWindowExW(0, type, text, 0x50000000 | style, x, y, width, height, window, id, Win32.GetModuleHandleW(null), 0);
        if (control == 0) throw new InvalidOperationException("Cannot create diagnostic control.");
        Win32.SendMessageW(control, 0x30, (nuint)font, 1); if (id != 0) controls[id] = control; return control;
    }
    private void Label(string text, int x, int y, int width, int height) => Add(0, "STATIC", text, 0, x, y, width, height);
    private void Button(int id, string text, int x, int y, int width) => Add(id, "BUTTON", text, 0x10000, x, y, width, 32);
    private unsafe string Text(int id)
    {
        var buffer = new char[512]; fixed (char* data = buffer) return new(buffer, 0, Win32.GetWindowTextW(controls[id], data, buffer.Length));
    }
    private bool Checked(int id) => Win32.SendMessageW(controls[id], 0xF0, 0, 0) != 0;
    private void LoadPreferences()
    {
        try
        {
            if (!File.Exists(AppPaths.Config)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(AppPaths.Config));
            var root = document.RootElement;
            void Set(string key, int id) { if (root.TryGetProperty(key, out var value)) Win32.SetWindowTextW(controls[id], value.ToString()); }
            Set("ask_wake_phrase", 103); Set("dictation_wake_phrase", 104); Set("noise_gate_db", 105); Set("wake_silence_seconds", 106);
            if (root.TryGetProperty("speech_model", out var model) && model.GetString() is { } selected) speechModel = selected;
            if (root.TryGetProperty("microphone_activation", out var mode) && mode.GetString() == "keep_ready") Win32.SendMessageW(controls[107], 0xF1, 1, 0);
        }
        catch (Exception) { notice = "Saved wake preferences could not be read. Diagnostic defaults are shown; no settings were changed."; }
    }
    private unsafe void RefreshDevices()
    {
        try
        {
            devices = AudioDeviceCatalog.Enumerate(); Win32.SendMessageW(controls[101], 0x14B, 0, 0);
            foreach (var device in devices) fixed (char* name = device.Name) Win32.SendMessageW(controls[101], 0x143, 0, (nint)name);
            var selected = 0;
            if (File.Exists(AppPaths.Config))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.Config));
                if (doc.RootElement.TryGetProperty("audio_device_id", out var id))
                    for (var i = 0; i < devices.Count; i++) if (devices[i].Id == id.GetString()) selected = i;
            }
            Win32.SendMessageW(controls[101], 0x14E, (nuint)selected, 0);
        }
        catch (Exception) { notice = "Could not enumerate microphones. Check Windows microphone access, then click Refresh."; }
    }
    private void Render()
    {
        var state = session.View; var busy = tasks > 0;
        foreach (var id in new[] { 101, 102, 103, 104, 105, 106, 107, 108, 110 }) Win32.EnableWindow(controls[id], !closing && !state.Running && !busy ? 1 : 0);
        foreach (var id in new[] { 112, 113, 114 }) Win32.EnableWindow(controls[id], !closing && state.Running && !state.AttemptActive && !busy ? 1 : 0);
        foreach (var id in new[] { 115, 116 }) Win32.EnableWindow(controls[id], !closing && state.AttemptActive && !busy ? 1 : 0);
        Win32.EnableWindow(controls[111], !closing && (state.Running || busy) ? 1 : 0);
        Win32.EnableWindow(controls[117], state.CanExport && !exporting ? 1 : 0);
        Win32.EnableWindow(controls[118], state.CanExport ? 1 : 0);
        Win32.SetWindowTextW(controls[120], notice.Length > 0 ? notice : closing ? Environment.TickCount64 - closingAt < 5000
            ? "Closing microphone and detector… Reports remain available."
            : "Stopping is taking longer than expected. Export/Open Logs still work; Force Close exits this diagnostic process." : state.Status);
        Win32.SetWindowTextW(controls[119], closing && Environment.TickCount64 - closingAt >= 5000 ? "Force Close" : "Close");
        Win32.SetWindowTextW(controls[121], state.Health);
        if (oldEvents != state.Events) { oldEvents = state.Events; Win32.SetWindowTextW(controls[122], state.Events); Win32.SendMessageW(controls[122], 0x115, 7, 0); }
    }
    private void Post(Action action) { if (disposed) return; pending.Enqueue(action); Win32.PostMessageW(window, Win32.WmApp, 0, 0); }
    private void Work(Func<Task> action)
    {
        tasks++; Render();
        _ = Task.Run(async () =>
        {
            try { await action(); }
            catch (Exception ex) { Post(() => notice = "Action failed: " + ex.GetType().Name + ". Stop, then export the report."); }
            finally { Post(() => { tasks--; if (closing && tasks == 0) Win32.DestroyWindow(window); else Render(); }); }
        });
    }
    private void Command(int id)
    {
        if (id == 119) { Close(); return; }
        if ((closing || tasks > 0) && id is not (111 or 117 or 118)) return;
        notice = "";
        switch (id)
        {
            case 102: RefreshDevices(); break;
            case 110:
                if (!float.TryParse(Text(105), NumberStyles.Float, CultureInfo.InvariantCulture, out var gate) || !float.IsFinite(gate) || gate is < -90 or > 0 || !int.TryParse(Text(106), out var silence) || silence is < 1 or > 10) { notice = "Enter a gate from −90 to 0 dB and silence from 1 to 10 seconds."; break; }
                var index = (int)Win32.SendMessageW(controls[101], 0x147, 0, 0);
                if (index < 0 || index >= devices.Count) { notice = "Select a microphone first."; break; }
                var settings = new DiagnosticSettings(Text(103).Trim(), Text(104).Trim(), gate, silence, index == 0 ? "Windows Default" : "Selected Microphone " + index, Checked(108), Checked(107), speechModel);
                try { (new AppConfig { AskWakePhrase = settings.AskPhrase, DictationWakePhrase = settings.DictationPhrase }).Validate(); }
                catch (ArgumentException ex) { notice = ex.Message; break; }
                var device = devices[index].Id;
                // Register start/cancellation in command order before dispatching work.
                var start = session.StartAsync(settings, device); Work(() => start); break;
            case 111: var stop = session.StopAsync(); Work(() => stop); break;
            case 112: Work(() => session.BeginAttemptAsync(VoiceMode.AskAi)); break;
            case 113: Work(() => session.BeginAttemptAsync(VoiceMode.Dictation)); break;
            case 114: Work(() => session.BeginAttemptAsync(null)); break;
            case 115: Work(() => session.FinishAttemptAsync(false)); break;
            case 116: Work(() => session.FinishAttemptAsync(true)); break;
            case 117:
                if (exporting) break; exporting = true;
                Work(() =>
                {
                    try { var file = session.Export(); Post(() => notice = "Report ready: " + file + "\r\nReview the ZIP, then send it back for diagnosis."); OpenFolder(Path.GetDirectoryName(file)!); return Task.CompletedTask; }
                    finally { Post(() => exporting = false); }
                }); break;
            case 118: if (session.LogDirectory is string directory) OpenFolder(directory); break;
        }
        Render();
    }
    private static void OpenFolder(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private void Close()
    {
        if (closing) { if (Environment.TickCount64 - closingAt >= 5000) Environment.Exit(2); return; }
        closing = true; closingAt = Environment.TickCount64; notice = ""; var stop = session.StopAsync(); Work(() => stop);
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint Procedure(nint hwnd, uint message, nuint wp, nint lp)
    {
        var self = current;
        try
        {
            if (self is not null && hwnd == self.window)
            {
                switch (message)
                {
                    case 0x111: if ((wp >> 16) == 0) self.Command((int)(wp & 0xFFFF)); return 0;
                    case 0x113: self.Render(); return 0;
                    case Win32.WmApp: for (var i = 0; i < 32 && self.pending.TryDequeue(out var action); i++) action(); return 0;
                    case 0x10: self.Close(); return 0;
                    case 2: self.window = 0; Win32.PostQuitMessage(0); return 0;
                }
            }
        }
        catch (Exception ex) { if (self is not null) { self.notice = "Diagnostic UI error: " + ex.GetType().Name + ". Use Stop, then export the report."; } }
        return Win32.DefWindowProcW(hwnd, message, wp, lp);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (session is DiagnosticSession owned) owned.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (window != 0) Win32.DestroyWindow(window);
        Win32.DeleteObject(font); current = null;
    }
}
