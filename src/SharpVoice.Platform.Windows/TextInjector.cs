using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.Platform.Windows;

public sealed record WindowTarget(nint Handle, uint ProcessId, nint Focus, string ProcessName)
{
    public static WindowTarget Capture()
    {
        var hwnd = Win32.GetForegroundWindow();
        var thread = Win32.GetWindowThreadProcessId(hwnd, out var pid);
        var info = new Win32.GuiThreadInfo { Size = (uint)Marshal.SizeOf<Win32.GuiThreadInfo>() };
        Win32.GetGUIThreadInfo(thread, ref info);
        string processName;
        try { using var process = Process.GetProcessById((int)pid); processName = process.ProcessName; }
        catch { processName = ""; }
        return new(hwnd, pid, info.Focus, processName);
    }

    public bool IsStillFocused()
    {
        if (Handle == 0 || Win32.GetForegroundWindow() != Handle) return false;
        var thread = Win32.GetWindowThreadProcessId(Handle, out var pid);
        if (pid != ProcessId) return false;
        var info = new Win32.GuiThreadInfo { Size = (uint)Marshal.SizeOf<Win32.GuiThreadInfo>() };
        return Win32.GetGUIThreadInfo(thread, ref info) != 0 && info.Focus == Focus;
    }
}

public static class TextInjector
{
    public static async Task InsertAsync(string text, WindowTarget target, string method, int delayMs, CancellationToken ct, SelectionSnapshot? selection = null)
    {
        await Task.Delay(delayMs, ct);
        var deadline = Environment.TickCount64 + 2000;
        while (ModifiersDown())
        {
            if (Environment.TickCount64 >= deadline) throw new InvalidOperationException("Release Ctrl, Alt, Shift and Windows keys before inserting. Text is retained in the preview.");
            await Task.Delay(20, ct);
        }
        EnsureTarget(target);
        ct.ThrowIfCancellationRequested();
        if (method == "clipboard")
        {
            EnsureSelection(selection);
            SetClipboardText(text, 0);
            EnsureTarget(target);
            EnsureSelection(selection);
            SendPaste();
        }
        else
        {
            for (var offset = 0; offset < text.Length;)
            {
                ct.ThrowIfCancellationRequested();
                EnsureTarget(target);
                if (ModifiersDown()) throw new InvalidOperationException("Typing stopped because a modifier was pressed. Some text may already have been inserted.");
                // Give the target's text services time to translate each VK_PACKET
                // instead of queuing a whole sentence at once. Keep surrogate pairs together.
                var count = char.IsHighSurrogate(text[offset]) && offset + 1 < text.Length && char.IsLowSurrogate(text[offset + 1]) ? 2 : 1;
                if (offset == 0) EnsureSelection(selection);
                SendUnicode(text.AsSpan(offset, count));
                offset += count;
                await Task.Delay(10, ct);
            }
        }
    }

    private static void EnsureSelection(SelectionSnapshot? selection)
    {
        if (selection is not null && !selection.IsStillSelected())
            throw new InvalidOperationException("The original selected text or focused control changed. Automatic replacement stopped; text is retained in the preview.");
    }

    private static bool ModifiersDown() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => (Win32.GetAsyncKeyState(key) & 0x8000) != 0);
    private static void EnsureTarget(WindowTarget target)
    {
        if (!target.IsStillFocused()) throw new InvalidOperationException("The active window or focused control changed. Automatic insertion stopped; text is retained in the preview.");
    }

    private static unsafe void SendUnicode(ReadOnlySpan<char> text)
    {
        var inputs = stackalloc Win32.Input[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = Key(0, text[i], 4);
            inputs[i * 2 + 1] = Key(0, text[i], 6);
        }
        var count = (uint)text.Length * 2;
        if (Win32.SendInput(count, inputs, sizeof(Win32.Input)) != count) throw new InvalidOperationException("Windows blocked text insertion, possibly into an elevated app. Some text may already have been inserted; use the retained preview.");
    }

    private static unsafe void SendPaste()
    {
        Win32.Input* inputs = stackalloc Win32.Input[4] { Key(0x11, 0, 0), Key(0x56, 0, 0), Key(0x56, 0, 2), Key(0x11, 0, 2) };
        if (Win32.SendInput(4, inputs, sizeof(Win32.Input)) != 4)
        {
            var release = Key(0x11, 0, 2);
            Win32.SendInput(1, &release, sizeof(Win32.Input));
            throw new InvalidOperationException("Windows blocked clipboard paste. Text remains on the clipboard.");
        }
    }

    private static Win32.Input Key(ushort key, ushort scan, uint flags) => new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Scan = scan, Flags = flags } } };

    public static void SetClipboardText(string text, nint owner)
    {
        // OpenClipboard(NULL) + EmptyClipboard makes SetClipboardData fail: use a real owner window.
        if (owner == 0) owner = Win32.GetForegroundWindow();
        if (Win32.OpenClipboard(owner) == 0) throw new InvalidOperationException("Clipboard is busy. Try copying again.");
        try
        {
            if (Win32.EmptyClipboard() == 0) throw new InvalidOperationException("Could not clear clipboard.");
            PutClipboard(Win32.RegisterClipboardFormatW("CanIncludeInClipboardHistory"), BitConverter.GetBytes(0));
            PutClipboard(Win32.RegisterClipboardFormatW("CanUploadToCloudClipboard"), BitConverter.GetBytes(0));
            PutClipboard(Win32.RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing"), BitConverter.GetBytes(1));
            PutClipboard(13, Encoding.Unicode.GetBytes(text + '\0'));
        }
        finally { Win32.CloseClipboard(); }
    }

    private static unsafe void PutClipboard(uint format, ReadOnlySpan<byte> bytes)
    {
        if (format == 0) throw new InvalidOperationException("Clipboard format registration failed.");
        var memory = Win32.GlobalAlloc(0x42, (nuint)bytes.Length);
        if (memory == 0) throw new OutOfMemoryException();
        try
        {
            var pointer = Win32.GlobalLock(memory);
            if (pointer == 0) throw new InvalidOperationException("Could not lock clipboard memory.");
            try { bytes.CopyTo(new Span<byte>((void*)pointer, bytes.Length)); }
            finally { Win32.GlobalUnlock(memory); }
            if (Win32.SetClipboardData(format, memory) == 0) throw new InvalidOperationException("Could not write clipboard.");
            memory = 0; // Windows owns the memory after successful SetClipboardData.
        }
        finally { if (memory != 0) Win32.GlobalFree(memory); }
    }
}
