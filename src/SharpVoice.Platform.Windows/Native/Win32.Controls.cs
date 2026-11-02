using System.Runtime.InteropServices;

namespace SharpVoice.Platform.Windows.Native;

public static unsafe partial class Win32
{
    [StructLayout(LayoutKind.Sequential)] public struct CommonControls { public uint Size, Classes; }
    [StructLayout(LayoutKind.Sequential)] public struct Notification { public nint Window; public nuint Id; public int Code; }
    [StructLayout(LayoutKind.Sequential)] public struct TabItem { public uint Mask, State, StateMask; public char* Text; public int TextLength, Image; public nint Parameter; }
    [StructLayout(LayoutKind.Sequential)] public struct DrawItem { public uint Type, Id, ItemId, Action, State; public nint Window, Dc; public Rect Rect; public nuint Data; }
    [StructLayout(LayoutKind.Sequential)] public struct MeasureItem { public uint Type, Id, ItemId, Width, Height; public nuint Data; }
    [StructLayout(LayoutKind.Sequential)] public struct MenuInfo { public uint Size, Mask, Style, MaxHeight; public nint Background; public uint HelpId; public nuint Data; }
    [LibraryImport("comctl32.dll")] public static partial int InitCommonControlsEx(in CommonControls controls);
    [LibraryImport("comctl32.dll")] public static partial int SetWindowSubclass(nint window, delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> proc, nuint id, nuint data);
    [LibraryImport("comctl32.dll")] public static partial int RemoveWindowSubclass(nint window, delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> proc, nuint id);
    [LibraryImport("comctl32.dll")] public static partial nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] public static partial nint GetDlgItem(nint window, int id);
    [LibraryImport("user32.dll")] public static partial int IsWindowVisible(nint window);
    [LibraryImport("user32.dll")] public static partial int EnableWindow(nint window, int enable);
    [LibraryImport("user32.dll")] public static partial int SetMenuInfo(nint menu, in MenuInfo info);
    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW")] public static partial int AppendOwnerMenu(nint menu, uint flags, nuint id, nint data);
    [LibraryImport("user32.dll")] public static partial int DrawFocusRect(nint dc, in Rect rect);
    [LibraryImport("gdi32.dll")] public static partial int RoundRect(nint dc, int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [LibraryImport("gdi32.dll")] public static partial nint GetStockObject(int index);
    [LibraryImport("dwmapi.dll")] public static partial int DwmSetWindowAttribute(nint window, uint attribute, in int value, uint size);
    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial int SetWindowTheme(nint window, string? subApp, string? subId);
}
