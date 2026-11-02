using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SharpVoice.Platform.Windows;

/// <summary>Native UI Automation client. COM pointers never leave this dedicated MTA thread.</summary>
internal static partial class UiaSelection
{
    private static readonly BlockingCollection<Action> work = new();
    private static readonly Lazy<Thread> worker = new(StartWorker);
    private static nint automation;
    private static int pending;
    private const int WaitMilliseconds = 1800;

    public static Task<SelectionSnapshot?> CaptureAsync(WindowTarget target, CancellationToken ct)
        => CaptureQueuedAsync(target.IsStillFocused, 0, ct);

    internal static Task<SelectionSnapshot?> CaptureControlForTestAsync(nint control, Func<bool> targetGuard, CancellationToken ct = default)
        => CaptureQueuedAsync(targetGuard, control, ct);

    private static async Task<SelectionSnapshot?> CaptureQueuedAsync(Func<bool> targetGuard, nint control, CancellationToken ct)
    {
        var completion = new TaskCompletionSource<SelectionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue(() =>
        {
            if (completion.Task.IsCompleted) return;
            try
            {
                var snapshot = Capture(targetGuard, control);
                if (!completion.TrySetResult(snapshot)) snapshot?.Dispose();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        try { return await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(WaitMilliseconds), ct).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            AbandonCapture(completion);
            throw SelectionSnapshot.CaptureError("the accessibility provider timed out.");
        }
        catch (OperationCanceledException) { AbandonCapture(completion); throw; }
    }

    private static void AbandonCapture(TaskCompletionSource<SelectionSnapshot?> completion)
    {
        if (!completion.TrySetCanceled() && completion.Task.IsCompletedSuccessfully) completion.Task.Result?.Dispose();
    }

    private static void Queue(Action action)
    {
        if (Interlocked.Increment(ref pending) > 8)
        {
            Interlocked.Decrement(ref pending);
            throw SelectionSnapshot.CaptureError("the accessibility provider is busy.");
        }
        _ = worker.Value;
        work.Add(() => { try { action(); } finally { Interlocked.Decrement(ref pending); } });
    }

    private static Thread StartWorker()
    {
        var desktop = GetThreadDesktop(GetCurrentThreadId());
        var thread = new Thread(() =>
        {
            if (desktop != 0) SetThreadDesktop(desktop);
            var initialized = CoInitializeEx(0, 0) >= 0; // COINIT_MULTITHREADED
            try
            {
                if (initialized) InitializeAutomation();
                foreach (var action in work.GetConsumingEnumerable()) action();
            }
            finally { Release(automation); if (initialized) CoUninitialize(); }
        }) { IsBackground = true, Name = "SharpVoice selection accessibility" };
        thread.Start();
        return thread;
    }

    private static unsafe void InitializeAutomation()
    {
        var clsid = new Guid("e22ad333-b25f-460c-83d0-0581107395c9"); // CUIAutomation8
        var iid = new Guid("34723aff-0c9d-49d0-9896-7ab52df8cd8a"); // IUIAutomation2
        nint instance = 0;
        if (CoCreateInstance(&clsid, 0, 1, &iid, &instance) < 0) return;
        automation = instance;
        // IUIAutomation has 58 vtable entries; IUIAutomation2 appends six properties.
        ((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(instance, 59))(instance, 0); // AutoSetFocus = false
        ((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(instance, 61))(instance, 250); // ConnectionTimeout
        ((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(instance, 63))(instance, 500); // TransactionTimeout
    }

    private static unsafe SelectionSnapshot? Capture(Func<bool> targetGuard, nint control)
    {
        if (!targetGuard()) throw SelectionSnapshot.CaptureError("the focused control changed.");
        if (automation == 0) return null;
        nint element = 0, pattern = 0, range = 0;
        try
        {
            if (GetElement(control, out element) < 0 || element == 0) return null; // GetFocusedElement
            if (!ElementMatchesTarget(element, targetGuard, control)) return null;
            if (GetInteger(element, 35, out var password) < 0 || password != 0) return null;
            var patternIid = new Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"); // IUIAutomationTextPattern
            // A TextPattern may be absent in custom editors. Absence means ordinary dictation.
            if (((delegate* unmanaged[Stdcall]<nint, int, Guid*, nint*, int>)Slot(element, 14))(element, 10014, &patternIid, &pattern) < 0 || pattern == 0)
                return null;
            range = SelectedRange(pattern);
            if (range == 0) return null;
            var text = ReadText(range);
            if (text.Length == 0) return null;
            if (ReadOnly(range)) throw SelectionSnapshot.CaptureError("the selected field is read-only.");
            var retainedElement = element;
            var retainedPattern = pattern;
            var retainedRange = range;
            if (!Validate(targetGuard, control, element, pattern, range, text))
                throw SelectionSnapshot.CaptureError("the selection changed while it was being read.");
            element = pattern = range = 0; // Ownership moves into snapshot; release always runs on this MTA.
            return new(text, () => ValidateOnWorker(targetGuard, control, retainedElement, retainedPattern, retainedRange, text), () =>
            {
                // Cleanup is never synchronously blocked by a remote provider.
                work.Add(() => { Release(retainedRange); Release(retainedPattern); Release(retainedElement); });
            });
        }
        finally { Release(range); Release(pattern); Release(element); }
    }

    private static bool ValidateOnWorker(Func<bool> targetGuard, nint control, nint element, nint pattern, nint range, string text)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue(() =>
        {
            if (result.Task.IsCompleted) return;
            try { result.TrySetResult(Validate(targetGuard, control, element, pattern, range, text)); }
            catch { result.TrySetResult(false); }
        });
        try { return result.Task.WaitAsync(TimeSpan.FromMilliseconds(WaitMilliseconds)).GetAwaiter().GetResult(); }
        catch { result.TrySetResult(false); return false; }
    }

    private static unsafe bool Validate(Func<bool> targetGuard, nint control, nint element, nint pattern, nint range, string text)
    {
        if (!targetGuard()) return false;
        nint focused = 0, currentRange = 0;
        try
        {
            if (GetElement(control, out focused) < 0 || focused == 0) return false;
            int same = 0;
            if (((delegate* unmanaged[Stdcall]<nint, nint, nint, int*, int>)Slot(automation, 3))(automation, element, focused, &same) < 0 || same == 0) return false;
            if (!ElementMatchesTarget(focused, targetGuard, control) || GetInteger(focused, 35, out var password) < 0 || password != 0) return false;
            currentRange = SelectedRange(pattern);
            if (currentRange == 0 || ReadOnly(currentRange)) return false;
            // Compare both endpoints, not only text: repeated identical words elsewhere are a different selection.
            int start = 1, end = 1;
            var compare = (delegate* unmanaged[Stdcall]<nint, int, nint, int, int*, int>)Slot(currentRange, 5);
            if (compare(currentRange, 0, range, 0, &start) < 0 || compare(currentRange, 1, range, 1, &end) < 0 || start != 0 || end != 0) return false;
            return ReadText(currentRange) == text && targetGuard();
        }
        finally { Release(currentRange); Release(focused); }
    }

    private static bool ElementMatchesTarget(nint element, Func<bool> targetGuard, nint control)
        // Browser accessibility elements can be served by a renderer process rather than the
        // foreground HWND process. GetFocusedElement + HasKeyboardFocus identifies the provider;
        // WindowTarget separately guards the actual foreground process and native focused HWND.
        => targetGuard() && (control != 0 || (GetInteger(element, 26, out var focused) >= 0 && focused != 0));

    private static unsafe int GetElement(nint control, out nint element)
    {
        if (control == 0) return GetObject(automation, 8, out element); // GetFocusedElement in production.
        nint local = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(automation, 6))(automation, control, &local);
        element = local;
        return hr; // ElementFromHandle, only for isolated owned control tests.
    }

    private static unsafe nint SelectedRange(nint pattern)
    {
        nint array = 0, range = 0;
        try
        {
            if (GetObject(pattern, 5, out array) < 0 || array == 0) throw SelectionSnapshot.CaptureError("the selected range is unavailable.");
            if (GetInteger(array, 3, out var count) < 0) throw SelectionSnapshot.CaptureError("the selected range is unavailable.");
            if (count == 0) return 0;
            if (count != 1) throw SelectionSnapshot.CaptureError("multiple selected ranges are not supported.");
            if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Slot(array, 4))(array, 0, &range) < 0 || range == 0)
                throw SelectionSnapshot.CaptureError("the selected range is unavailable.");
            return range;
        }
        finally { Release(array); }
    }

    private static unsafe string ReadText(nint range)
    {
        nint bstr = 0;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Slot(range, 12))(range, SelectionSnapshot.MaximumTextLength + 1, &bstr) < 0)
                throw SelectionSnapshot.CaptureError("the selected text is unavailable.");
            var length = bstr == 0 ? 0 : SysStringLen(bstr);
            if (length > SelectionSnapshot.MaximumTextLength) throw SelectionSnapshot.CaptureError($"the selection exceeds {SelectionSnapshot.MaximumTextLength:N0} characters.");
            return bstr == 0 ? "" : new string((char*)bstr, 0, (int)length);
        }
        finally { if (bstr != 0) SysFreeString(bstr); }
    }

    private static unsafe bool ReadOnly(nint range)
    {
        // VARIANT is 24 bytes on Win64; explicit layout also accommodates Win32's 16 bytes.
        Variant value = default;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, int, Variant*, int>)Slot(range, 9))(range, 40015, &value) < 0)
                throw SelectionSnapshot.CaptureError("the field's editing permissions are unavailable.");
            return value.Type == 11 && value.Boolean != 0; // UIA_IsReadOnlyAttributeId, VT_BOOL
        }
        finally { VariantClear(&value); }
    }

    private static unsafe int GetObject(nint value, int slot, out nint result)
    {
        nint local = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(value, slot))(value, &local);
        result = local;
        return hr;
    }
    private static unsafe int GetInteger(nint value, int slot, out int result)
    {
        int local = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(value, slot))(value, &local);
        result = local;
        return hr;
    }
    private static unsafe nint Slot(nint value, int slot) => (*(nint**)value)[slot];
    private static unsafe void Release(nint value) { if (value != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(value, 2))(value); }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public short Boolean; }
    [LibraryImport("user32.dll")] private static partial nint GetThreadDesktop(uint thread);
    [LibraryImport("user32.dll")] private static partial int SetThreadDesktop(nint desktop);
    [LibraryImport("kernel32.dll")] private static partial uint GetCurrentThreadId();
    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(nint reserved, uint mode);
    [LibraryImport("ole32.dll")] private static partial void CoUninitialize();
    [LibraryImport("ole32.dll")] private static unsafe partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* result);
    [LibraryImport("oleaut32.dll")] private static partial uint SysStringLen(nint value);
    [LibraryImport("oleaut32.dll")] private static partial void SysFreeString(nint value);
    [LibraryImport("oleaut32.dll")] private static unsafe partial int VariantClear(Variant* value);
}
