using System.Runtime.InteropServices;

namespace SharpVoice.Platform.Windows;

/// <summary>Native SAPI ISpVoice, on its own COM-initialized worker. No dynamic COM/AOT dependency.</summary>
internal static unsafe partial class WindowsAnswerSpeech
{
    internal static Task SpeakAsync(string text, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows speech is unavailable.");
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { Speak(text, ct); complete.TrySetResult(); }
            catch (OperationCanceledException) { complete.TrySetCanceled(ct); }
            catch (Exception ex) { complete.TrySetException(ex); }
        }) { IsBackground = true, Name = "SharpVoice Spoken Answer" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return complete.Task;
    }
    private static void Speak(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2));
        nint voice = 0;
        try
        {
            var clsid = new Guid("96749377-3391-11D2-9EE3-00C04F797396"); var iid = new Guid("6C44DF74-72B9-4992-A1EC-EF996E0422D4");
            Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, 0, 1, in iid, out voice));
            var table = *(nint**)voice;
            // sapi.h: IUnknown (3), ISpNotifySource (7), ISpEventSource (3), then ISpVoice.
            var speak = (delegate* unmanaged[Stdcall]<nint, char*, uint, uint*, int>)table[20];
            fixed (char* content = text) Marshal.ThrowExceptionForHR(speak(voice, content, 1 | 2 | 16, null)); // Async, purge, plain text.
            var wait = (delegate* unmanaged[Stdcall]<nint, uint, int>)table[32];
            while (true)
            {
                ct.ThrowIfCancellationRequested(); var result = wait(voice, 30);
                if (result == 0) break;
                Marshal.ThrowExceptionForHR(result);
            }
            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            if (voice != 0)
            {
                var table = *(nint**)voice;
                _ = ((delegate* unmanaged[Stdcall]<nint, char*, uint, uint*, int>)table[20])(voice, null, 3, null);
                _ = ((delegate* unmanaged[Stdcall]<nint, uint, int>)table[32])(voice, 500); // Await purge before microphone may resume.
                _ = ((delegate* unmanaged[Stdcall]<nint, uint>)table[2])(voice);
            }
            CoUninitialize();
        }
    }
    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(nint reserved, uint mode);
    [LibraryImport("ole32.dll")] private static partial void CoUninitialize();
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
}
