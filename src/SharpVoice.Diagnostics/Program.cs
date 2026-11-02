using SharpVoice.Diagnostics;
using SharpVoice.Platform.Windows.Native;

if (!OperatingSystem.IsWindows()) return 1;
try
{
    using var window = new DiagnosticWindow(new DiagnosticSession());
    return window.Run();
}
catch (Exception ex)
{
    Win32.MessageBoxW(0, "Wake Diagnostics could not start: " + ex.Message, "SharpVoice Wake Diagnostics", 0x10);
    return 1;
}
