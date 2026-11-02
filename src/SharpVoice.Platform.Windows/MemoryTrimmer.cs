using System.Runtime.InteropServices;

namespace SharpVoice.Platform.Windows;

/// <summary>Releases idle working-set pages; committed model memory remains available for reuse.</summary>
public static partial class MemoryTrimmer
{
    public static bool Trim()
    {
        // Aggressive mode requires a blocking, compacting collection; use the supported
        // nonblocking mode so a dictation cleanup does not force a full compacting pause.
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        return OperatingSystem.IsWindows() && K32EmptyWorkingSet(GetCurrentProcess()) != 0;
    }

    [LibraryImport("kernel32.dll")] private static partial nint GetCurrentProcess();
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int K32EmptyWorkingSet(nint process);
}
