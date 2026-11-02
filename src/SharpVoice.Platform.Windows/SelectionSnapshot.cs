using System.Runtime.InteropServices;

namespace SharpVoice.Platform.Windows;

/// <summary>A read-only view of the source highlighted when recording starts.</summary>
public sealed partial class SelectionSnapshot : IDisposable
{
    public const int MaximumTextLength = 32_000;
    private readonly object sync = new();
    private readonly Func<bool> validate;
    private Action? release;
    private int disposed;
    public string Text { get; }

    internal SelectionSnapshot(string text, Func<bool> validate, Action? release = null)
        => (Text, this.validate, this.release) = (text, validate, release);

    /// <summary>Fast, bounded capture for classic EDIT controls. Other controls require CaptureAsync.</summary>
    public static SelectionSnapshot? TryCapture(WindowTarget target)
        => TryCaptureClassic(target, out var snapshot) ? snapshot : null;

    /// <summary>Classic controls are captured before returning; accessibility providers run on a separate MTA.</summary>
    public static Task<SelectionSnapshot?> CaptureAsync(WindowTarget target, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return Task.FromResult<SelectionSnapshot?>(null);
        if (!target.IsStillFocused()) throw CaptureError("the focused control changed.");
        if (TryCaptureClassic(target, out var snapshot)) return Task.FromResult(snapshot);
        return UiaSelection.CaptureAsync(target, ct);
    }

    public bool IsStillSelected()
    {
        lock (sync)
        {
            if (disposed != 0) return false;
            try { return validate(); }
            catch { return false; } // Inaccessible, timed out or destroyed controls must fail closed.
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed != 0) return;
            disposed = 1;
            release?.Invoke();
            release = null;
        }
    }

    internal static InvalidOperationException CaptureError(string detail)
        => new($"Selected text could not be used: {detail} Recording was stopped to preserve the selection.");

    private static unsafe bool TryCaptureClassic(WindowTarget target, out SelectionSnapshot? snapshot)
    {
        return TryCaptureClassicControl(target.Focus, target.IsStillFocused, out snapshot);
    }

    internal static SelectionSnapshot? CaptureControlForTest(nint control, Func<bool> targetGuard)
        => TryCaptureClassicControl(control, targetGuard, out var snapshot) ? snapshot : null;

    private static unsafe bool TryCaptureClassicControl(nint control, Func<bool> targetGuard, out SelectionSnapshot? snapshot)
    {
        snapshot = null;
        if (!OperatingSystem.IsWindows() || control == 0) return false;
        Span<char> name = stackalloc char[64];
        int length;
        fixed (char* p = name) length = GetClassNameW(control, p, name.Length);
        // RichEdit uses different text/offset conventions; let its native TextPattern handle those.
        if (!MemoryExtensions.Equals(name[..Math.Max(0, length)], "Edit".AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!targetGuard()) throw CaptureError("the focused control changed.");
        var range = ReadRange(control);
        if (range.Start == range.End) return true;
        var style = GetWindowLongW(control, -16);
        if ((style & 0x20) != 0 || Message(control, 0xD2, 0, 0) != 0) return true; // ES_PASSWORD / EM_GETPASSWORDCHAR
        if ((style & 0x800) != 0) throw CaptureError("the selected field is read-only.");
        var text = ReadClassicText(control, range.Start, range.End);
        var finalRange = ReadRange(control);
        if (finalRange != range || !targetGuard()) throw CaptureError("the selection changed while it was being read.");
        snapshot = new(text, () =>
        {
            if (!targetGuard()) return false;
            var currentStyle = GetWindowLongW(control, -16);
            if ((currentStyle & (0x20 | 0x800)) != 0 || Message(control, 0xD2, 0, 0) != 0) return false;
            var current = ReadRange(control);
            if (current != range) return false;
            var selected = ReadClassicText(control, current.Start, current.End);
            return selected == text && ReadRange(control) == range && targetGuard();
        });
        return true;
    }

    private static unsafe (uint Start, uint End) ReadRange(nint hwnd)
    {
        uint start = 0, end = 0;
        Message(hwnd, 0xB0, (nuint)(&start), (nint)(&end)); // EM_GETSEL, system message marshalling across processes.
        return (start, end);
    }

    private static unsafe string ReadClassicText(nint hwnd, uint start, uint end)
    {
        var length = Message(hwnd, 0xE, 0, 0); // WM_GETTEXTLENGTH
        if (end <= start || end - start > MaximumTextLength || length > MaximumTextLength)
            throw CaptureError($"the text exceeds the {MaximumTextLength:N0}-character capture limit.");
        if (end > length) throw CaptureError("the text changed while it was being read.");
        var buffer = new char[(int)length + 1];
        nuint read;
        fixed (char* pointer = buffer) read = Message(hwnd, 0xD, (nuint)buffer.Length, (nint)pointer);
        if (read != length || end > read) throw CaptureError("the text changed while it was being read.");
        return new string(buffer, (int)start, (int)(end - start));
    }

    private static nuint Message(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        // SMTO_ABORTIFHUNG | SMTO_BLOCK | SMTO_ERRORONEXIT. Never send copy or change modifiers.
        if (SendMessageTimeoutW(hwnd, message, wParam, lParam, 0x23, 60, out var result) == 0)
            throw CaptureError("the selected application did not respond.");
        return result;
    }

    [LibraryImport("user32.dll")] private static unsafe partial int GetClassNameW(nint hwnd, char* name, int count);
    [LibraryImport("user32.dll")] private static partial int GetWindowLongW(nint hwnd, int index);
    [LibraryImport("user32.dll")] private static partial nint SendMessageTimeoutW(nint hwnd, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
}
