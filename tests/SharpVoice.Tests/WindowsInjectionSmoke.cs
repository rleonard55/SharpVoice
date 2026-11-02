using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using SharpVoice.Platform.Windows;
using SharpVoice.Platform.Windows.Native;

internal static partial class WindowsInjectionSmoke
{
    private const string Fixture = "All right, did you get all your stuff? All right, you did good. Café — hello! 🙂";
    [LibraryImport("user32.dll")] private static partial nint SetFocus(nint window);
    [LibraryImport("user32.dll")] private static partial int PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);
    [LibraryImport("kernel32.dll")] private static partial uint GetCurrentThreadId();
    [LibraryImport("ole32.dll")] private static partial int OleInitialize(nint reserved);
    [LibraryImport("ole32.dll")] private static partial void OleUninitialize();

    public static void Run(bool richEdit = false, string method = "typing")
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        ExceptionDispatchInfo? error = null;
        var uiThread = new Thread(() =>
        {
            var initialized = false;
            try
            {
                Marshal.ThrowExceptionForHR(OleInitialize(0));
                initialized = true;
                RunOnUiThread(richEdit, method);
            }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
            finally { if (initialized) OleUninitialize(); }
        });
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        uiThread.Join();
        error?.Throw();
    }

    private static void RunOnUiThread(bool richEdit, string method)
    {
        var original = Win32.GetForegroundWindow();
        var thread = GetCurrentThreadId();
        var window = Win32.CreateWindowExW(0, "STATIC", "SharpVoice isolated insertion check", method == "clipboard" ? 0x00CF0000u : 0x10CF0000u, 120, 120, 650, 220, 0, 0, Win32.GetModuleHandleW(null), 0);
        if (window == 0) throw new Exception("Could not create isolated test window.");
        if (richEdit) NativeLibrary.Load("msftedit.dll");
        var edit = Win32.CreateWindowExW(0, richEdit ? "RICHEDIT50W" : "EDIT", "", 0x50800044, 10, 10, 610, 150, window, 0, Win32.GetModuleHandleW(null), 0);
        if (edit == 0) throw new Exception("Could not create isolated text field.");
        if (method == "clipboard")
        {
            try
            {
                // Test the real clipboard transfer without taking focus from the user's desktop.
                TextInjector.SetClipboardText(Fixture, window);
                Win32.SendMessageW(edit, 0x302, 0, 0); // WM_PASTE, also used by Ctrl+V.
                var actual = ReadText(edit);
                if (actual != Fixture) throw new Exception($"Clipboard fixture mismatch: expected length {Fixture.Length}, actual length {actual.Length}; actual text: {actual}; units: {string.Join(',', actual.Select(c => ((int)c).ToString("X4")))}");
                Console.WriteLine("PASS clipboard-to-RichEdit paste preserves the screenshot sentence and Unicode, using a hidden test window.");
                return;
            }
            finally { Win32.DestroyWindow(window); }
        }
        Win32.SetForegroundWindow(window);
        SetFocus(edit);
        var target = WindowTarget.Capture();
        if (target.Handle != window || target.Focus != edit) { Win32.DestroyWindow(window); throw new Exception("Test could not focus its own text field; no text was sent."); }
        var work = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await TextInjector.InsertAsync(Fixture, target, method, 100, timeout.Token);
                await Task.Delay(250, timeout.Token);
                var actual = ReadText(edit);
                if (actual != Fixture) throw new Exception($"Unicode insertion mismatch. Expected: {Fixture}; actual: {actual}");
                Console.WriteLine($"PASS production {method} insertion into an isolated Windows {(richEdit ? "RichEdit" : "EDIT")} field, preserving the screenshot's sentence and Unicode.");
            }
            finally { PostThreadMessageW(thread, 0x12, 0, 0); }
        });
        try
        {
            while (Win32.GetMessageW(out var message, 0, 0, 0) > 0)
            {
                Win32.TranslateMessage(message);
                Win32.DispatchMessageW(message);
            }
            work.GetAwaiter().GetResult();
        }
        finally { Win32.DestroyWindow(window); Win32.SetForegroundWindow(original); }
    }

    private static unsafe string ReadText(nint window)
    {
        var length = Win32.GetWindowTextLengthW(window);
        var buffer = new char[length + 1];
        fixed (char* pointer = buffer) Win32.GetWindowTextW(window, pointer, buffer.Length);
        return new string(buffer, 0, length);
    }
}
