using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private readonly Dictionary<nint, nint> hoverOwners = [];
    private readonly HashSet<nint> hoverFrames = [];
    private nint hoveredControlSource, hoveredControl, accentHover;

    private unsafe void InstallControlHover(nint control, string type)
    {
        if (type is not ("BUTTON" or "EDIT" or "COMBOBOX" or "msctls_trackbar32")) return;
        hoverOwners[control] = control;
        if (type is "EDIT" or "msctls_trackbar32") hoverFrames.Add(control);
        if (Win32.SetWindowSubclass(control, &ControlHoverProcedure, 2, 0) == 0)
            throw new InvalidOperationException("Could not style Settings control feedback.");
    }
    private unsafe void InstallComboEditHover(nint combo)
    {
        var child = HoverGetWindow(combo, 5); // GW_CHILD: editable combo's native edit.
        if (child == 0) return;
        hoverOwners[child] = combo;
        if (Win32.SetWindowSubclass(child, &ControlHoverProcedure, 2, 0) == 0)
            throw new InvalidOperationException("Could not style the editable model field.");
    }
    private bool ControlIsHovered(nint control) => hoverOwners.GetValueOrDefault(control, control) == hoveredControl && HoverIsWindowEnabled(control) != 0;
    private bool ControlHasFocus(nint control) => hoverOwners.GetValueOrDefault(HoverGetFocus()) == control;
    private void InvalidateControlFeedback(nint control)
    {
        if (control == 0) return;
        Win32.InvalidateRect(control, 0, 1);
        foreach (var (child, owner) in hoverOwners)
            if (owner == control && child != control) Win32.InvalidateRect(child, 0, 1);
    }
    private void ClearControlHover()
    {
        var previous = hoveredControl;
        hoveredControl = hoveredControlSource = 0;
        InvalidateControlFeedback(previous);
    }
    private unsafe void PaintControlFrame(nint hwnd, nint dc)
    {
        if (!hoverFrames.Contains(hwnd) || HoverIsWindowEnabled(hwnd) == 0) return;
        var focus = ControlHasFocus(hwnd);
        if (!focus && !ControlIsHovered(hwnd)) return;
        Win32.GetClientRect(hwnd, out var rect);
        HoverFrameRect(dc, &rect, focus ? accent : borderBrush);
        if (focus)
        {
            rect.Left++; rect.Top++; rect.Right--; rect.Bottom--;
            HoverFrameRect(dc, &rect, accent);
        }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint ControlHoverProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var self = current;
        if (self is null) return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
        if (message == 0x200 && HoverIsWindowEnabled(hwnd) != 0 && Win32.IsWindowVisible(hwnd) != 0)
        {
            var owner = self.hoverOwners.GetValueOrDefault(hwnd);
            var previous = self.hoveredControl;
            self.hoveredControlSource = hwnd;
            self.hoveredControl = owner;
            var tracking = new HelpMouseTracking { Size = (uint)sizeof(HelpMouseTracking), Flags = 2, Window = hwnd };
            if (TrackMouseEvent(&tracking) == 0) self.ClearControlHover();
            if (previous != self.hoveredControl) { self.InvalidateControlFeedback(previous); self.InvalidateControlFeedback(owner); }
        }
        if ((message == 0x2A3 && self.hoveredControlSource == hwnd) ||
            (message is 0xA or 0x18 && wParam == 0 && self.hoverOwners.GetValueOrDefault(hwnd) == self.hoveredControl)) self.ClearControlHover();
        var result = Win32.DefSubclassProc(hwnd, message, wParam, lParam);
        if (message is 7 or 8) self.InvalidateControlFeedback(self.hoverOwners.GetValueOrDefault(hwnd));
        if (message == 0x318) self.PaintControlFrame(hwnd, (nint)wParam);
        if (message == 0xF && self.hoverFrames.Contains(hwnd))
        {
            var dc = HoverGetDC(hwnd);
            if (dc != 0) { try { self.PaintControlFrame(hwnd, dc); } finally { HoverReleaseDC(hwnd, dc); } }
        }
        if (message == 0x82)
        {
            if (self.hoveredControlSource == hwnd) self.ClearControlHover();
            self.hoverOwners.Remove(hwnd); self.hoverFrames.Remove(hwnd);
            Win32.RemoveWindowSubclass(hwnd, &ControlHoverProcedure, id);
        }
        return result;
    }
    private static uint BlendHover(uint color, uint towards)
    {
        uint result = 0;
        for (var shift = 0; shift <= 16; shift += 8)
            result |= (uint)((((color >> shift) & 255) * 85 + ((towards >> shift) & 255) * 15) / 100) << shift;
        return result;
    }
    [LibraryImport("user32.dll", EntryPoint = "GetFocus")] private static partial nint HoverGetFocus();
    [LibraryImport("user32.dll", EntryPoint = "GetWindow")] private static partial nint HoverGetWindow(nint hwnd, uint command);
    [LibraryImport("user32.dll", EntryPoint = "IsWindowEnabled")] private static partial int HoverIsWindowEnabled(nint hwnd);
    [LibraryImport("user32.dll", EntryPoint = "GetDC")] private static partial nint HoverGetDC(nint hwnd);
    [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")] private static partial int HoverReleaseDC(nint hwnd, nint dc);
    [LibraryImport("user32.dll", EntryPoint = "FrameRect")] private static unsafe partial int HoverFrameRect(nint dc, Win32.Rect* rect, nint brush);
}
