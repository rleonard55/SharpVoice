using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using SharpVoice.Platform.Windows;
using SharpVoice.Platform.Windows.Native;

internal static partial class WindowsSelectionSmoke
{
    [LibraryImport("user32.dll")] private static partial nint SetFocus(nint window);
    [LibraryImport("user32.dll")] private static partial uint GetClipboardSequenceNumber();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint CreateDesktopW(string name, nint device, nint mode, uint flags, uint access, nint security);
    [LibraryImport("user32.dll")] private static partial int SetThreadDesktop(nint desktop);
    [LibraryImport("user32.dll")] private static partial int CloseDesktop(nint desktop);
    [LibraryImport("user32.dll")] private static unsafe partial int PeekMessageW(Win32.Msg* message, nint window, uint min, uint max, uint remove);

    public static void Run()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var desktop = CreateDesktopW("SharpVoiceSelectionCheck" + Guid.NewGuid().ToString("N"), 0, 0, 0, 0x10000000, 0);
        if (desktop == 0) throw new Exception("Cannot create isolated desktop for selection checks.");
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (SetThreadDesktop(desktop) == 0) throw new Exception("Cannot enter isolated selection desktop.");
                RunIsolated();
            }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.Start();
        thread.Join();
        CloseDesktop(desktop);
        failure?.Throw();
    }

    private static void RunIsolated()
    {
        var window = Win32.CreateWindowExW(0, "STATIC", "SharpVoice isolated selection check", 0x10CF0000, 120, 120, 650, 220, 0, 0, Win32.GetModuleHandleW(null), 0);
        if (window == 0) throw new Exception("Cannot create selection fixture.");
        try
        {
            var edit = Win32.CreateWindowExW(0, "EDIT", "Before café 🙂 after", 0x50800044, 10, 10, 610, 150, window, 0, Win32.GetModuleHandleW(null), 0);
            if (edit == 0) throw new Exception("Cannot create selection fixture field.");
            SetFocus(edit);
            var targetCurrent = true;
            Func<bool> targetGuard = () => Volatile.Read(ref targetCurrent);
            var clipboard = GetClipboardSequenceNumber();
            Win32.SendMessageW(edit, 0xB1, 7, 14); // EM_SETSEL: café + space + surrogate pair.
            using var snapshot = SelectionSnapshot.CaptureControlForTest(edit, targetGuard) ?? throw new Exception("Expected selected text.");
            if (snapshot.Text != "café 🙂" || !snapshot.IsStillSelected()) throw new Exception("Wrong selected substring or immediately invalid snapshot.");
            targetCurrent = false;
            if (snapshot.IsStillSelected()) throw new Exception("Changed focus guard was accepted.");
            targetCurrent = true;
            Win32.SendMessageW(edit, 0xB1, 0, 6);
            if (snapshot.IsStillSelected()) throw new Exception("Changed range was accepted.");
            Win32.SendMessageW(edit, 0xB1, 7, 14);
            Win32.SetWindowTextW(edit, "Before changed after");
            Win32.SendMessageW(edit, 0xB1, 7, 14);
            if (snapshot.IsStillSelected()) throw new Exception("Changed selected text was accepted.");
            Win32.SendMessageW(edit, 0xB1, 0, 0);
            if (SelectionSnapshot.CaptureControlForTest(edit, targetGuard) is not null) throw new Exception("Empty range was treated as selection.");
            Win32.SendMessageW(edit, 0xCC, '*', 0); // EM_SETPASSWORDCHAR, including dynamically protected fields.
            Win32.SendMessageW(edit, 0xB1, 0, 6);
            if (SelectionSnapshot.CaptureControlForTest(edit, targetGuard) is not null) throw new Exception("Password field exposed text.");
            Win32.SendMessageW(edit, 0xCC, 0, 0);
            Win32.SendMessageW(edit, 0xCF, 1, 0); // EM_SETREADONLY
            ExpectCaptureFailure(edit, "read-only");
            Win32.SendMessageW(edit, 0xCF, 0, 0);
            Win32.SetWindowTextW(edit, new string('x', SelectionSnapshot.MaximumTextLength + 1));
            Win32.SendMessageW(edit, 0xB1, 0, 1);
            ExpectCaptureFailure(edit, "capture limit");
            // Exercise the native UIA vtables through RichEdit's TextPattern as well.
            NativeLibrary.Load("msftedit.dll");
            var rich = Win32.CreateWindowExW(0, "RICHEDIT50W", "Before café 🙂 after", 0x50800044, 10, 10, 610, 150, window, 0, Win32.GetModuleHandleW(null), 0);
            if (rich == 0) throw new Exception("Cannot create RichEdit selection fixture.");
            SetFocus(rich);
            Win32.SendMessageW(rich, 0xB1, 7, 14);
            using var richSnapshot = Pump(UiaSelection.CaptureControlForTestAsync(rich, targetGuard)) ?? throw new Exception("RichEdit TextPattern returned no selection.");
            if (richSnapshot.Text != "café 🙂") throw new Exception("RichEdit TextPattern returned wrong Unicode range.");
            if (!Pump(Task.Run(richSnapshot.IsStillSelected))) throw new Exception("UIA snapshot rejected unchanged selection.");
            targetCurrent = false;
            if (Pump(Task.Run(richSnapshot.IsStillSelected))) throw new Exception("UIA snapshot accepted a changed focus guard.");
            targetCurrent = true;
            Win32.SendMessageW(rich, 0xB1, 0, 6);
            if (Pump(Task.Run(richSnapshot.IsStillSelected))) throw new Exception("UIA snapshot accepted a changed range.");
            Win32.SetWindowTextW(rich, "Before changed after");
            Win32.SendMessageW(rich, 0xB1, 7, 14);
            if (Pump(Task.Run(richSnapshot.IsStillSelected))) throw new Exception("UIA snapshot accepted changed selected text.");
            snapshot.Dispose();
            if (snapshot.IsStillSelected()) throw new Exception("Disposed snapshot was accepted.");
            if (GetClipboardSequenceNumber() != clipboard) throw new Exception("Selection discovery altered the clipboard.");
            Console.WriteLine("PASS isolated selection exact Unicode range, changed range/text rejection, empty/password exclusion, unchanged clipboard, and native UIA RichEdit range validation on a separate desktop.");
        }
        finally { Win32.DestroyWindow(window); }
    }
    private static void ExpectCaptureFailure(nint edit, string reason)
    {
        try { using var unexpected = SelectionSnapshot.CaptureControlForTest(edit, () => true); }
        catch (InvalidOperationException ex) when (ex.Message.Contains(reason, StringComparison.Ordinal)) { return; }
        throw new Exception("Unsafe selected source did not fail closed: " + reason);
    }

    private static unsafe T Pump<T>(Task<T> task)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!task.IsCompleted)
        {
            Win32.Msg message;
            while (PeekMessageW(&message, 0, 0, 0, 1) != 0)
            {
                Win32.TranslateMessage(message);
                Win32.DispatchMessageW(message);
            }
            if (Environment.TickCount64 > deadline) throw new TimeoutException("Isolated selection provider did not finish.");
            Thread.Sleep(1);
        }
        return task.GetAwaiter().GetResult();
    }
}
