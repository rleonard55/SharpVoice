using System.Runtime.InteropServices;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    private static unsafe void CheckControlHover(NativeDesktop desktop, Action<ThemeColors> changeTheme, string? output)
    {
        var window = desktop.SettingsHandle;
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        void Page(int page)
        {
            var tabs = Win32.GetDlgItem(window, 1014);
            Win32.SendMessageW(tabs, 0x130C, (nuint)page, 0);
            var notification = new Win32.Notification { Window = tabs, Id = 1014, Code = -551 };
            Win32.SendMessageW(window, 0x4E, 1014, (nint)(&notification));
        }
        uint Pixel(nint control)
        {
            Win32.GetClientRect(control, out var rect);
            var screen = GetDC(0); var dc = CreateCompatibleDC(screen);
            var bitmap = CreateCompatibleBitmap(screen, rect.Right, rect.Bottom);
            var previous = Win32.SelectObject(dc, bitmap);
            try
            {
                Check(PrintWindow(control, dc, 3) != 0, "Cannot render Settings control.");
                Win32.SelectObject(dc, previous);
                var info = new BitmapInfo { Size = 40, Width = rect.Right, Height = -rect.Bottom, Planes = 1, Bits = 32 };
                var pixels = new byte[rect.Right * rect.Bottom * 4];
                fixed (byte* bytes = pixels)
                    Check(GetDIBits(screen, bitmap, 0, (uint)rect.Bottom, bytes, ref info, 0) == rect.Bottom, "Cannot read control rendering.");
                uint hash = 2166136261;
                foreach (var value in pixels) hash = unchecked((hash ^ value) * 16777619);
                return hash;
            }
            finally { Win32.SelectObject(dc, previous); Win32.DeleteObject(bitmap); DeleteDC(dc); ReleaseDC(0, screen); }
        }
        Page(0);
        var focus = GetFocus();
        foreach (var palette in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
        {
            changeTheme(palette); Win32.SendMessageW(window, 0x1A, 0, 0);
            foreach (var id in new[] { 105, 101, 1009, 1008, 1015, 3008 })
            {
                var control = Win32.GetDlgItem(window, id);
                Win32.SendMessageW(control, 0x2A3, 0, 0); var normal = Pixel(control);
                Win32.SendMessageW(control, 0x200, 0, (12 << 16) | 12);
                Check(Pixel(control) != normal, $"Control {id} has no hover feedback in {(palette.Dark ? "dark" : "light")} theme.");
                Check(GetFocus() == focus, "Hover stole keyboard focus.");
                Win32.SendMessageW(control, 0x2A3, 0, 0);
                Check(Pixel(control) == normal, $"Control {id} retained hover after pointer leave.");
                Win32.SendMessageW(control, 0x200, 0, 0);
                Win32.EnableWindow(control, 0);
                var disabled = Pixel(control);
                Win32.SendMessageW(control, 0x200, 0, 0);
                var disabledAfterMove = Pixel(control);
                Check(disabledAfterMove == disabled, $"Disabled control {id} retained an interactive hover in {(palette.Dark ? "dark" : "light")} theme ({disabled:X8} -> {disabledAfterMove:X8}).");
                Win32.EnableWindow(control, 1);
                Win32.SendMessageW(control, 0x2A3, 0, 0);
            }
            var edit = Win32.GetDlgItem(window, 1008);
            FocusHoverControl(edit);
            Check(GetFocus() == edit, "Cannot exercise keyboard focus on isolated desktop.");
            var focused = Pixel(edit);
            Win32.SendMessageW(edit, 0x200, 0, 0);
            Win32.SendMessageW(edit, 0x2A3, 0, 0);
            Check(Pixel(edit) == focused, "Pointer leave removed the keyboard-focus border.");
            FocusHoverControl(focus);
            Win32.SendMessageW(Win32.GetDlgItem(window, 105), 0x200, 0, 0);
            if (output is not null) RenderHelp(window, output + (palette.Dark ? "-controls-dark.bmp" : "-controls-light.bmp"));
            var button = Win32.GetDlgItem(window, 105);
            Win32.SendMessageW(window, 0x6, 0, 0);
            var after = Pixel(button);
            Win32.SendMessageW(button, 0x2A3, 0, 0);
            Check(Pixel(button) == after, "Deactivation retained a hover highlight.");
        }
        Page(1);
        var combo = Win32.GetDlgItem(window, 1003);
        var child = HoverChildWindow(combo, 5);
        Check(child != 0, "Editable model combo has no native edit.");
        var originalText = new char[4096];
        fixed (char* text = originalText) Win32.GetWindowTextW(combo, text, originalText.Length);
        var beforeChild = Pixel(combo);
        Win32.SendMessageW(child, 0x200, 0, 0);
        Check(Pixel(combo) != beforeChild, "Editable combo child does not highlight its parent field.");
        Win32.SendMessageW(combo, 0x2A3, 0, 0);
        Check(Pixel(combo) != beforeChild, "Parent leave cleared the active child hover.");
        Win32.SendMessageW(child, 0x2A3, 0, 0);
        Check(Pixel(combo) == beforeChild, "Editable combo retained child hover.");
        var afterText = new char[4096];
        fixed (char* text = afterText) Win32.GetWindowTextW(combo, text, afterText.Length);
        Check(originalText.SequenceEqual(afterText), "Hover changed the model draft.");
        Page(0);
        var apply = Win32.GetDlgItem(window, 101);
        var plain = Pixel(apply);
        Win32.SendMessageW(apply, 0x200, 0, 0);
        Page(1);
        Check(Pixel(apply) == plain, "Tab navigation retained a footer hover.");
        Console.WriteLine("PASS native control hover, pointer leave, disabled controls, keyboard focus, editable combo children, draft preservation and both themes.");
    }
    [LibraryImport("user32.dll", EntryPoint = "SetFocus")] private static partial nint FocusHoverControl(nint window);
    [LibraryImport("user32.dll", EntryPoint = "GetWindow")] private static partial nint HoverChildWindow(nint window, uint command);
}
