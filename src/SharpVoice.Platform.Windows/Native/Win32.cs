using System.Runtime.InteropServices;

namespace SharpVoice.Platform.Windows.Native;

public static unsafe partial class Win32
{
    [LibraryImport("user32.dll")] public static partial int GetWindowRect(nint hwnd, out Rect rect);
    public const uint WmApp = 0x8001, WmTray = 0x8002;
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Msg { public nint Hwnd; public uint Message; public nuint WParam; public nint LParam; public uint Time; public Point Point; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] public struct WindowClass
    {
        public uint Size, Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> Proc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] public struct KeyboardHookData { public uint Key, ScanCode, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] public struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] public struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Sequential)] public struct GuiThreadInfo { public uint Size, Flags; public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; public Rect CaretRect; }
    [StructLayout(LayoutKind.Sequential)] public struct PaintStruct { public nint Dc; public int Erase; public Rect Paint; public int Restore, IncUpdate; public fixed byte Reserved[32]; }
    [StructLayout(LayoutKind.Sequential)] public struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        public fixed char Tip[128];
        public uint State, StateMask;
        public fixed char Info[256];
        public uint Timeout;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial nint GetModuleHandleW(string? module);
    [LibraryImport("user32.dll")] public static partial ushort RegisterClassExW(in WindowClass windowClass);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial nint CreateWindowExW(uint extended, string className, string text, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll")] public static partial nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] public static partial int DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")] public static partial int GetMessageW(out Msg message, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] public static partial int TranslateMessage(in Msg message);
    [LibraryImport("user32.dll")] public static partial nint DispatchMessageW(in Msg message);
    [LibraryImport("user32.dll")] public static partial int IsDialogMessageW(nint hwnd, in Msg message);
    [LibraryImport("user32.dll")] public static partial void PostQuitMessage(int code);
    [LibraryImport("user32.dll")] public static partial int PostMessageW(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] public static partial nint SendMessageW(nint hwnd, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] public static partial int ShowWindow(nint hwnd, int command);
    [LibraryImport("user32.dll")] public static partial int SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] public static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] public static partial uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [LibraryImport("user32.dll")] public static partial int GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [LibraryImport("user32.dll")] public static partial short GetAsyncKeyState(int key);
    [LibraryImport("user32.dll")] public static partial nint SetWindowsHookExW(int type, delegate* unmanaged[Stdcall]<int, nuint, nint, nint> callback, nint module, uint thread);
    [LibraryImport("user32.dll")] public static partial int UnhookWindowsHookEx(nint hook);
    [LibraryImport("user32.dll")] public static partial nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] public static partial uint SendInput(uint count, Input* inputs, int size);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial int SetWindowTextW(nint hwnd, string text);
    [LibraryImport("user32.dll")] public static partial int GetWindowTextLengthW(nint hwnd);
    [LibraryImport("user32.dll")] public static partial int GetWindowTextW(nint hwnd, char* text, int maximum);
    [LibraryImport("user32.dll")] public static partial nint LoadIconW(nint instance, nint name);
    [LibraryImport("user32.dll")] public static partial nint CreateIconFromResourceEx(byte* data, uint length, int icon, uint version, int width, int height, uint flags);
    [LibraryImport("user32.dll")] public static partial int DestroyIcon(nint icon);
    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")] public static partial int PlaySound(nint sound, nint module, uint flags);
    [LibraryImport("user32.dll")] public static partial int MessageBeep(uint type);
    [LibraryImport("user32.dll")] public static partial nint LoadCursorW(nint instance, nint name);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial int MessageBoxW(nint hwnd, string text, string caption, uint type);
    [LibraryImport("user32.dll")] public static partial nint CreatePopupMenu();
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial int AppendMenuW(nint menu, uint flags, nuint id, string text);
    [LibraryImport("user32.dll")] public static partial int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint owner, nint rect);
    [LibraryImport("user32.dll")] public static partial int DestroyMenu(nint menu);
    [LibraryImport("user32.dll")] public static partial int GetCursorPos(out Point point);
    [LibraryImport("shell32.dll")] public static partial int Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [LibraryImport("user32.dll")] public static partial int SystemParametersInfoW(uint action, uint parameter, out Rect value, uint flags);
    [LibraryImport("user32.dll")] public static partial int InvalidateRect(nint hwnd, nint rect, int erase);
    [LibraryImport("user32.dll")] public static partial nint BeginPaint(nint hwnd, out PaintStruct paint);
    [LibraryImport("user32.dll")] public static partial int EndPaint(nint hwnd, in PaintStruct paint);
    [LibraryImport("user32.dll")] public static partial int GetClientRect(nint hwnd, out Rect rect);
    [LibraryImport("user32.dll")] public static partial int FillRect(nint dc, in Rect rect, nint brush);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial int DrawTextW(nint dc, string text, int count, ref Rect rect, uint flags);
    [LibraryImport("user32.dll")] public static partial int SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [LibraryImport("user32.dll")] public static partial nuint SetTimer(nint hwnd, nuint id, uint interval, nint callback);
    [LibraryImport("user32.dll")] public static partial int KillTimer(nint hwnd, nuint id);
    [LibraryImport("gdi32.dll")] public static partial nint CreateSolidBrush(uint color);
    [LibraryImport("gdi32.dll")] public static partial int DeleteObject(nint obj);
    [LibraryImport("gdi32.dll")] public static partial uint SetTextColor(nint dc, uint color);
    [LibraryImport("gdi32.dll")] public static partial int SetBkMode(nint dc, int mode);
    [LibraryImport("gdi32.dll")] public static partial uint SetBkColor(nint dc, uint color);
    [LibraryImport("gdi32.dll")] public static partial nint SelectObject(nint dc, nint obj);
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeout, uint charset, uint output, uint clip, uint quality, uint pitch, string face);
    [LibraryImport("user32.dll")] public static partial int OpenClipboard(nint hwnd);
    [LibraryImport("user32.dll")] public static partial int CloseClipboard();
    [LibraryImport("user32.dll")] public static partial int EmptyClipboard();
    [LibraryImport("user32.dll")] public static partial nint SetClipboardData(uint format, nint memory);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial uint RegisterClipboardFormatW(string name);
    [LibraryImport("kernel32.dll")] public static partial nint GlobalAlloc(uint flags, nuint size);
    [LibraryImport("kernel32.dll")] public static partial nint GlobalLock(nint memory);
    [LibraryImport("kernel32.dll")] public static partial int GlobalUnlock(nint memory);
    [LibraryImport("kernel32.dll")] public static partial nint GlobalFree(nint memory);
}
