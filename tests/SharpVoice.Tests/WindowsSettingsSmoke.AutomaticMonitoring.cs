using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsSettingsSmoke
{
    public static void RunAutomaticMonitoring()
    {
        var isolated = CreateMonitoringDesktop("SharpVoiceMonitoring" + Guid.NewGuid().ToString("N"), 0, 0, 0, 0x10000000, 0);
        if (isolated == 0) throw new Exception("Cannot create monitoring test desktop.");
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (SetMonitoringDesktop(isolated) == 0) throw new Exception("Cannot enter monitoring test desktop.");
                CheckAutomaticMonitoring();
            }
            catch (Exception ex) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
        });
        thread.Start(); thread.Join(); CloseMonitoringDesktop(isolated); failure?.Throw();
        Console.WriteLine("PASS automatic Audio monitoring lifecycle on an isolated desktop; no physical microphone opened.");
    }

    private static unsafe void CheckAutomaticMonitoring()
    {
        using var desktop = new NativeDesktop(new AppConfig { AudioDeviceId = "applied-unavailable-test-endpoint" });
        var changes = new List<bool>();
        desktop.InputPreviewChanged = changes.Add; // Observe capture requests without creating a recorder.
        desktop.Initialize(showSettings: false, integrateShell: false, audioFeedback: false);
        var window = desktop.SettingsHandle;
        var tabs = Win32.GetDlgItem(window, 1014);
        void Tick() => Win32.SendMessageW(window, 0x113, 3, 0);
        void Expect(params bool[] expected)
        {
            if (!changes.SequenceEqual(expected)) throw new Exception("Unexpected automatic monitoring transitions: " + string.Join(",", changes));
        }
        void Page(int page)
        {
            Win32.SendMessageW(tabs, 0x130C, (nuint)page, 0);
            var changed = new Win32.Notification { Window = tabs, Id = 1014, Code = -551 };
            Win32.SendMessageW(window, 0x4E, 1014, (nint)(&changed));
        }
        Tick(); Expect();
        Win32.ShowWindow(window, 4); Tick(); Expect(true);
        Tick(); Tick(); Expect(true);
        if (Win32.GetDlgItem(window, 111) != 0) throw new Exception("Manual preview button is still present.");
        Win32.SendMessageW(Win32.GetDlgItem(window, 1009), 0x14E, 0, 0); Tick(); Expect(true);
        Page(1); Tick(); Expect(true, false);
        Page(0); Tick(); Expect(true, false, true);
        desktop.SetActive(true); Tick(); Expect(true, false, true, false);
        desktop.SetActive(false); Tick(); Expect(true, false, true, false, true);
        var available = false;
        var gate = typeof(NativeDesktop).GetProperty("CanMonitorInput") ?? throw new Exception("No application availability gate for monitoring.");
        gate.SetValue(desktop, (Func<bool>)(() => available));
        Tick(); Expect(true, false, true, false, true, false);
        Tick(); Tick(); Expect(true, false, true, false, true, false);
        available = true; Tick(); Expect(true, false, true, false, true, false, true);
        Win32.ShowWindow(window, 0); Tick(); Expect(true, false, true, false, true, false, true, false);
        Win32.ShowWindow(window, 4); Tick();
        Win32.SendMessageW(window, 0x10, 0, 0);
        if (changes.Count != 10 || changes.Last()) throw new Exception("Closing Audio settings retained monitoring.");
        Tick(); if (changes.Count != 10) throw new Exception("Hidden timer restarted monitoring.");
        Win32.ShowWindow(window, 4); Tick();
        Win32.ShowWindow(window, 6); Tick();
        if (changes.Count != 12 || changes.Last()) throw new Exception("Minimized Settings retained microphone monitoring.");
        Win32.ShowWindow(window, 9); Tick();
        if (changes.Count != 13 || !changes.Last()) throw new Exception("Restored Audio settings did not resume monitoring.");
        Win32.SendMessageW(window, 0x10, 0, 0);
        var attempts = 0;
        desktop.InputPreviewChanged = enabled =>
        {
            if (enabled) { attempts++; throw new InvalidOperationException("Simulated disconnected microphone."); }
        };
        Win32.ShowWindow(window, 4); Tick(); Tick(); Tick();
        if (attempts != 1) throw new Exception("Unavailable microphone was retried on every timer tick.");
        available = false; Tick(); available = true; Tick();
        if (attempts != 2) throw new Exception("Monitoring did not permit retry after returning to idle.");
        Win32.SendMessageW(window, 0x10, 0, 0);

    }

    [LibraryImport("user32.dll", EntryPoint = "CreateDesktopW", StringMarshalling = StringMarshalling.Utf16)] private static partial nint CreateMonitoringDesktop(string name, nint device, nint mode, uint flags, uint access, nint security);
    [LibraryImport("user32.dll", EntryPoint = "SetThreadDesktop")] private static partial int SetMonitoringDesktop(nint desktop);
    [LibraryImport("user32.dll", EntryPoint = "CloseDesktop")] private static partial int CloseMonitoringDesktop(nint desktop);
}
