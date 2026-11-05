using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsSettingsSmoke
{
    public static unsafe void CompactAudio()
    {
        using var desktop = new NativeDesktop(new AppConfig()); desktop.Initialize(false, false, audioFeedback: false);
        var window = desktop.SettingsHandle;
        nint Item(int id) => Win32.GetDlgItem(window, id);
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string Read(nint control) { var chars = new char[512]; fixed (char* p = chars) return new string(chars, 0, Win32.GetWindowTextW(control, p, chars.Length)); }
        Check(Item(1016) == 0, "Separate input meter still occupies a row; combined meter is missing.");
        var slider = Item(1015); var gate = Item(1008); var toggle = Item(1040);
        Check((GetWindowLongPtrW(toggle, -16) & 15) == 3, "Live Transcript must expose native checkbox semantics.");
        Check(Win32.SendMessageW(toggle, 0xF0, 0, 0) == 1, "Live Transcript did not load the saved On value.");
        desktop.ReadLevel = () => -52;
        Win32.SetWindowTextW(gate, "-71.5"); Win32.SendMessageW(window, 0x113, 3, 0);
        Check(Read(Item(1018)).Contains("20 dB above gate") || Read(Item(1018)).Contains("19.5 dB above gate"), "Combined meter lost the draft threshold comparison.");
        Win32.SendMessageW(window, 0x114, 5, slider); Win32.SendMessageW(window, 0x114, 8, slider);
        Check(Read(gate) == "-71.5", "A no-op thumb interaction rounded the exact typed gate.");
        SetWindowPos(window, 0, -32000, -32000, 0, 0, 0x15); Win32.ShowWindow(window, 4);
        var channel = new Win32.Rect(); var nativeThumb = new Win32.Rect();
        Win32.SendMessageW(slider, 0x41A, 0, (nint)(&channel)); Win32.SendMessageW(slider, 0x419, 0, (nint)(&nativeThumb));
        var exactX = channel.Left + (int)MathF.Round(18.5f / 90 * (channel.Right - channel.Left));
        var edgeX = exactX + (exactX >= (nativeThumb.Left + nativeThumb.Right) / 2 ? 5 : -5);
        foreach (var edgeY in new[] { 3, 15, 27 })
        {
            Win32.SendMessageW(slider, 0x201, 1, (edgeY << 16) | edgeX); Win32.SendMessageW(slider, 0x202, 0, (edgeY << 16) | edgeX);
            Check(Read(gate) == "-71.5", "Clicking the fractional marker edge changed the draft without dragging.");
        }
        AppConfig? applied = null; desktop.ConfigChanged = value => applied = value;
        Win32.SendMessageW(toggle, 0xF5, 0, 0);
        Check(Win32.SendMessageW(toggle, 0xF0, 0, 0) == 0 && applied is null, "Toggle changed applied configuration before Apply.");
        Win32.SendMessageW(window, 0x111, 101, 0);
        Check(applied is { LiveTranscription: false, NoiseGateDb: -71.5f }, "Apply lost the exact fractional gate or toggle state.");
        Win32.SetWindowTextW(gate, "invalid"); Check(Read(Item(1018)).Contains("valid gate"), "Invalid gate displayed a passing state.");
        Win32.SetWindowTextW(gate, "-65");
        foreach (var value in new[] { -90, -45, 0 })
        {
            Win32.SendMessageW(slider, 0x405, 1, value); Win32.SendMessageW(window, 0x114, 0, slider);
            Check(Read(gate) == value.ToString(), "Combined threshold did not synchronize numeric input.");
        }
        // The real native trackbar handles keyboard and mouse thumb movement.
        Win32.SendMessageW(slider, 0x405, 1, -65); Win32.SendMessageW(slider, 0x100, 0x27, 0); Win32.SendMessageW(slider, 0x101, 0x27, 0);
        Check(Win32.SendMessageW(slider, 0x400, 0, 0) == -64 && Read(gate) == "-64", "Native keyboard threshold adjustment failed.");
        SetWindowPos(window, 0, -32000, -32000, 0, 0, 0x15); Win32.ShowWindow(window, 4);
        var thumb = new Win32.Rect(); Win32.SendMessageW(slider, 0x419, 0, (nint)(&thumb));
        var x = (thumb.Left + thumb.Right) / 2; var y = (thumb.Top + thumb.Bottom) / 2;
        Win32.SendMessageW(slider, 0x201, 1, (y << 16) | x);
        Win32.SendMessageW(slider, 0x200, 1, (y << 16) | (x + 60));
        Win32.SendMessageW(slider, 0x202, 0, (y << 16) | (x + 60));
        var dragged = (int)Win32.SendMessageW(slider, 0x400, 0, 0);
        Check(dragged > -64 && Read(gate) == dragged.ToString(), "Native threshold drag did not update the saved draft value.");
        var tabs = Item(1014);
        Win32.GetWindowRect(Item(1043), out var audioResult);
        void Page(int page) { Win32.SendMessageW(tabs, 0x130C, (nuint)page, 0); var notify = new Win32.Notification { Window = tabs, Id = 1014, Code = -551 }; Win32.SendMessageW(window, 0x4E, 1014, (nint)(&notify)); }
        Page(1); Win32.GetWindowRect(Item(1043), out var otherResult);
        Page(0); Win32.GetWindowRect(Item(1043), out var restoredResult);
        Check(audioResult.Top < otherResult.Top && restoredResult.Top == audioResult.Top, "Audio result space was not recovered or restored across tabs.");
        Console.WriteLine("PASS compact audio: combined meter, native checkbox, exact gate Apply, invalid input, endpoints, keyboard and tab restoration.");
    }
}

internal static partial class WindowsHelpSmoke
{
    public static void RunCompactAudio()
    {
        var isolated = CreateDesktopW("SharpVoiceCompactAudio" + Guid.NewGuid().ToString("N"), 0, 0, 0, 0x10000000, 0);
        if (isolated == 0) throw new Exception("Cannot create compact audio test desktop.");
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { if (SetThreadDesktop(isolated) == 0) throw new Exception("Cannot enter compact audio test desktop."); WindowsSettingsSmoke.CompactAudio(); }
            catch (Exception ex) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
        });
        thread.Start(); thread.Join(); CloseDesktop(isolated); failure?.Throw();
    }
}
