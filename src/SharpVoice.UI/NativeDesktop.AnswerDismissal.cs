using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private nint answerMouseHook;
    private long answerDisplayGeneration;

    private unsafe void StartAnswerClickObservation()
    {
        if (answerMouseHook != 0) return;
        ++answerDisplayGeneration;
        answerMouseHook = Win32.SetWindowsHookExW(14, &AnswerMouseProcedure, Win32.GetModuleHandleW(null), 0);
        if (answerMouseHook == 0) throw new InvalidOperationException("Could not enable click-away dismissal for answers.");
    }

    private void StopAnswerClickObservation()
    {
        ++answerDisplayGeneration;
        if (answerMouseHook != 0) Win32.UnhookWindowsHookEx(answerMouseHook);
        answerMouseHook = 0;
    }

    // The hook never consumes a click or runs cancellation/speech work itself.
    // Checking the actual target also works before the non-activating pill has focus.
    internal unsafe void ObserveAnswerClick(nint target)
    {
        if (answerWindow == 0 || Win32.IsWindowVisible(answerWindow) == 0 || target == answerWindow || AnswerIsChild(answerWindow, target) != 0) return;
        var name = stackalloc char[32];
        var length = AnswerGetClassName(target, name, 32);
        if (new ReadOnlySpan<char>(name, length).SequenceEqual("#32768"))
        {
            // Standard EDIT context menus belong to their child control rather than
            // the pill itself. Preserve those as well as the conversation menu.
            var thread = Win32.GetWindowThreadProcessId(answerWindow, out _);
            var info = new Win32.GuiThreadInfo { Size = (uint)sizeof(Win32.GuiThreadInfo) };
            if (Win32.GetGUIThreadInfo(thread, ref info) != 0 &&
                (info.MenuOwner == answerWindow || AnswerIsChild(answerWindow, info.MenuOwner) != 0)) return;
        }
        var display = answerDisplayGeneration;
        Post(() => { if (display == answerDisplayGeneration) HideAnswer(); });
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint AnswerMouseProcedure(int code, nuint message, nint data)
    {
        if (code >= 0 && message is 0x201 or 0x204 or 0x207 or 0x20B && current is { } self)
        {
            try
            {
                // MSLLHOOKSTRUCT begins with a screen-coordinate POINT.
                var point = *(AnswerPoint*)data;
                self.ObserveAnswerClick(AnswerWindowFromPoint(point));
            }
            catch { /* Never let managed exceptions escape a native hook. */ }
        }
        return Win32.CallNextHookEx(0, code, message, data);
    }

    [StructLayout(LayoutKind.Sequential)] private struct AnswerPoint { public int X, Y; }
    [LibraryImport("user32.dll", EntryPoint = "WindowFromPoint")] private static partial nint AnswerWindowFromPoint(AnswerPoint point);
    [LibraryImport("user32.dll", EntryPoint = "IsChild")] private static partial int AnswerIsChild(nint parent, nint child);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")] private static unsafe partial int AnswerGetClassName(nint window, char* name, int maximum);
}
