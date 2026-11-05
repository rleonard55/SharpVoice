using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.Core;

namespace SharpVoice.Platform.Windows;

public sealed unsafe class GlobalKeyboardHook : IDisposable
{
    private static GlobalKeyboardHook? current;
    private readonly Action<VoiceMode> pressed;
    private readonly Action released;
    private nint handle;
    private int key;
    private int releasingCaptureKey;
    private int askKey = HotkeyDefinition.ControlAltShift;
    private DualHotkeyStateMachine? dualState;
    private bool toggleRecording;
    private RecordingGestureRouter gesture = new(false);
    private bool externalRecording;
    public void SetExternalRecording(bool active) { externalRecording = active; gesture.SetExternalRecording(active); }
    public void SetRecordingActive(bool active) => gesture.SetRecordingActive(active);
    public void ResetRecordingGesture() => gesture.Reset();
    private HotkeyCaptureState? capture;
    private Action<int?>? captureCompleted;
    private HotkeyStateMachine state = new(HotkeyDefinition.ControlAlt);
    public int Key
    {
        get => key;
        set
        {
            if (dualState is not null) { Configure(value, askKey); return; }
            if (!HotkeyDefinition.IsSupported(value)) throw new ArgumentOutOfRangeException(nameof(value));
            key = value;
            state = new(value);
            foreach (var modifier in new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C })
                if (modifier != releasingCaptureKey && (Win32.GetAsyncKeyState(modifier) & 0x8000) != 0) state.Process(modifier, true);
        }
    }

    public int AskKey
    {
        get => askKey;
        set => Configure(key, value);
    }

    public void Configure(int dictationKey, int askKey, bool? toggle = null)
    {
        toggleRecording = toggle ?? toggleRecording;
        gesture = new(toggleRecording);
        gesture.SetExternalRecording(externalRecording);
        var next = new DualHotkeyStateMachine(dictationKey, askKey);
        var relevant = new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C,
            HotkeyDefinition.PrimaryKey(dictationKey), HotkeyDefinition.PrimaryKey(askKey) };
        next.SynchronizeHeldKeys(relevant.Distinct().Where(x => x != 0 && x != releasingCaptureKey && (Win32.GetAsyncKeyState(x) & 0x8000) != 0));
        key = dictationKey;
        this.askKey = askKey;
        dualState = next;
    }

    public void BeginCapture(Action<int?> completed)
    {
        capture = new HotkeyCaptureState();
        captureCompleted = completed;
    }

    public void CancelCapture()
    {
        capture = null;
        captureCompleted = null;
        Key = key;
    }

    public GlobalKeyboardHook(int key, Action pressed, Action released)
    {
        if (current is not null) throw new InvalidOperationException("Only one keyboard hook may be installed.");
        Key = key; this.pressed = _ => pressed(); this.released = released;
        Install();
    }

    public GlobalKeyboardHook(int dictationKey, int askKey, Action<VoiceMode> pressed, Action released, bool toggle = false)
    {
        if (current is not null) throw new InvalidOperationException("Only one keyboard hook may be installed.");
        this.pressed = pressed; this.released = released;
        Configure(dictationKey, askKey, toggle);
        Install();
    }

    private void Install()
    {
        current = this;
        handle = Win32.SetWindowsHookExW(13, &Callback, Win32.GetModuleHandleW(null), 0);
        if (handle == 0) { current = null; throw new InvalidOperationException("Could not install the global keyboard hook."); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint Callback(int code, nuint message, nint data)
    {
        var self = current;
        if (code >= 0 && self is not null)
        {
            var key = (Win32.KeyboardHookData*)data;
            if (message is 0x100 or 0x104 or 0x101 or 0x105)
            {
                // Callbacks must only enqueue work: Windows removes slow low-level hooks.
                try
                {
                    if (self.capture is not null && (key->Flags & 0x10) == 0)
                    {
                        var captured = self.capture.Process((int)key->Key, message is 0x100 or 0x104);
                        if (captured.Completed)
                        {
                            var completed = self.captureCompleted;
                            // Async key state still describes the final key as down inside this hook.
                            // Do not seed that just-released key into the new gesture's lockout.
                            self.releasingCaptureKey = message is 0x101 or 0x105 ? (int)key->Key : 0;
                            try
                            {
                                self.CancelCapture();
                                completed?.Invoke(captured.Key);
                            }
                            finally { self.releasingCaptureKey = 0; }
                        }
                        // Forward modifier pairs; reserve other keys while the recorder is active.
                        if (!HotkeyDefinition.IsModifier((int)key->Key)) return 1;
                        return Win32.CallNextHookEx(0, code, message, data);
                    }
                    if (self.dualState is not null)
                    {
                        var result = self.gesture.Process(self.dualState.Process((int)key->Key, message is 0x100 or 0x104, (key->Flags & 0x10) != 0));
                        if (result.Transition is DualHotkeyTransition.Pressed or DualHotkeyTransition.Promoted) self.pressed(result.Mode);
                        else if (result.Transition == DualHotkeyTransition.Released) self.released();
                        if (result.Suppress) return 1;
                    }
                    else
                    {
                        var result = self.state.Process((int)key->Key, message is 0x100 or 0x104, (key->Flags & 0x10) != 0);
                        if (result.Transition == HotkeyTransition.Pressed) self.pressed(VoiceMode.Dictation);
                        else if (result.Transition == HotkeyTransition.Released) self.released();
                        if (result.Suppress) return 1;
                    }
                }
                catch { /* Exceptions must never cross the unmanaged callback boundary. */ }
            }
        }
        return Win32.CallNextHookEx(0, code, message, data);
    }

    public void Dispose()
    {
        if (handle != 0) Win32.UnhookWindowsHookEx(handle);
        handle = 0; current = null;
    }
}
