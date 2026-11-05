using SharpVoice.Platform.Windows.Native;
using SharpVoice.Platform.Windows;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private int hoveredTab = -1;
    internal int HoveredTab => hoveredTab;
    private sealed record MenuEntry(string Label, bool Checked = false, bool Submenu = false, string? Glyph = null);
    private readonly Dictionary<nint, MenuEntry> menuEntries = [];
    private readonly List<AccessibleMenuLabel> menuLabels = [];

    private void CreateThemeBrushes()
    {
        background = Win32.CreateSolidBrush(theme.Background);
        inputBackground = Win32.CreateSolidBrush(theme.Field);
        accent = Win32.CreateSolidBrush(theme.Accent);
        accentHover = Win32.CreateSolidBrush(BlendHover(theme.Accent, theme.Text));
        surfaceBrush = Win32.CreateSolidBrush(theme.Surface);
        borderBrush = Win32.CreateSolidBrush(theme.Border);
        selectionBrush = Win32.CreateSolidBrush(theme.Selection);
        successBrush = Win32.CreateSolidBrush(theme.Success);
    }
    private void DeleteThemeBrushes()
    {
        foreach (var brush in new[] { background, inputBackground, accent, surfaceBrush, borderBrush, selectionBrush, accentHover, successBrush })
            if (brush != 0) Win32.DeleteObject(brush);
    }
    private void ApplyTheme()
    {
        var next = themeProvider();
        if (theme != next) { DeleteThemeBrushes(); theme = next; CreateThemeBrushes(); }
        StyleHelp();
        if (setupWindow != 0)
        {
            var setupDark = theme.Dark ? 1 : 0;
            Win32.DwmSetWindowAttribute(setupWindow, 20, in setupDark, 4);
            Win32.InvalidateRect(setupWindow, 0, 1);
            if (setupMeter != 0)
            {
                Win32.SetWindowTheme(setupMeter, "", "");
                Win32.SendMessageW(setupMeter, 0x2001, 0, (nint)theme.Field);
                Win32.SendMessageW(setupMeter, 0x409, 0, (nint)theme.Accent);
            }
        }
        if (connectedWindow != 0) { var connectedDark=theme.Dark?1:0; Win32.DwmSetWindowAttribute(connectedWindow,20,in connectedDark,4); Win32.InvalidateRect(connectedWindow,0,1); }
        if (answerWindow != 0)
        {
            var answerDark = theme.Dark ? 1 : 0;
            Win32.DwmSetWindowAttribute(answerWindow, 20, in answerDark, 4);
            Win32.InvalidateRect(answerWindow, 0, 1);
        }
        if (toolsWindow != 0)
        {
            var darkTool = theme.Dark ? 1 : 0;
            if (Win32.DwmSetWindowAttribute(toolsWindow, 20, in darkTool, 4) < 0)
                Win32.DwmSetWindowAttribute(toolsWindow, 19, in darkTool, 4);
            Win32.InvalidateRect(toolsWindow, 0, 1);
        }
        var dark = theme.Dark ? 1 : 0;
        if (Win32.DwmSetWindowAttribute(settingsWindow, 20, in dark, 4) < 0)
            Win32.DwmSetWindowAttribute(settingsWindow, 19, in dark, 4);
        foreach (var control in pages.SelectMany(x => x).Concat(fields.Values).Concat(toolControls).Concat(answerControls).Concat(connectedControls).Concat(setupControls).Distinct())
            Win32.SetWindowTheme(control, theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
        Win32.SetWindowTheme(tabControl, "", "");
        Win32.SetWindowTheme(inputMeter, "", "");
        Win32.SetWindowTheme(localDownloadBar, "", "");
        Win32.SendMessageW(localDownloadBar, 0x2001, 0, (nint)theme.Field);
        Win32.SendMessageW(localDownloadBar, 0x409, 0, (nint)theme.Accent);
        Win32.SendMessageW(inputMeter, 0x2001, 0, (nint)theme.Field); // PBM_SETBKCOLOR
        Win32.SendMessageW(inputMeter, 0x409, 0, (nint)theme.Success); // PBM_SETBARCOLOR
        Win32.InvalidateRect(settingsWindow, 0, 1);
        Win32.InvalidateRect(hudWindow, 0, 1);
        foreach (var control in pages.SelectMany(x => x)) Win32.InvalidateRect(control, 0, 1);
    }
    private static void Rounded(nint dc, Win32.Rect rect, nint brush, int radius = 12)
    {
        var oldBrush = Win32.SelectObject(dc, brush);
        var oldPen = Win32.SelectObject(dc, Win32.GetStockObject(8)); // NULL_PEN
        Win32.RoundRect(dc, rect.Left, rect.Top, rect.Right, rect.Bottom, radius, radius);
        Win32.SelectObject(dc, oldPen);
        Win32.SelectObject(dc, oldBrush);
    }
    private void DrawLabel(nint dc, string text, Win32.Rect rect, uint color, uint flags = 0x8824)
    {
        var old = Win32.SelectObject(dc, font);
        Win32.SetBkMode(dc, 1);
        Win32.SetTextColor(dc, color);
        Win32.DrawTextW(dc, text, -1, ref rect, flags);
        Win32.SelectObject(dc, old);
    }
    private static readonly string[] SettingsTabIcons = ["\uE720", "\uE8F1", "\uE70F", "\uE765", "\uE77F", "\uE713"];
    private string? SettingsActionIcon(uint id) => id switch
    {
        101 => "\uE73E", // Apply
        102 => "\uE77B", // Sign in, matching the tray's account style
        103 => "\uE77B", // Account actions
        104 => "\uE8C8", // Copy, matching the tray
        105 or 109 => "\uE72C", // Refresh microphones/models
        107 or 110 => "\uE765", // Record keyboard shortcut
        113 => localDownloadCancellation is null ? "\uE896" : "\uE711", // Download / cancel
        112 => connectionCancellation is null ? "\uE768" : "\uE711", // Run / cancel test
        4204 => "\uE73E",
        4304 => "\uE8C8",
        4303 => historyRetryCancellation is null ? "\uE72C" : "\uE711",
        114 => "\uE8F1",
        115 => "\uE8A5",
        116 => "\uE713",
        117 => "\uE81C",
        118 => "\uE774", // Plugins
        119 => "\uE8F2", // Spoken answers
        121 => "\uE720", // Wake microphone
        122 => "\uE895", // Updates
        4701 => "\uE73E",
        4702 => "\uE72C",
        4703 => "\uE896",
        4704 => "\uE72C",
        4705 => "\uE946",
        4481 => "\uE73E",
        4482 => "\uE72C",
        4483 => "\uE711",
        4484 => "\uE897",
        4451 => "\uE73E",
        4501 => "\uE71A",
        4502 => "\uE72C",
        4503 => "\uE8C8",
        4505 => "\uE711",
        4509 => "\uE720",
        4510 => "\uE765",
        4512 => "\uE724",
        4401 => "\uE896", // Install
        4402 => "\uE72C", // Refresh
        108 => "\uE711", // Cancel
        4801 => "\uE711",
        4802 => "\uE72B",
        4803 => "\uE76C",
        4804 => "\uE720",
        4805 => "\uE765",
        4806 => "\uE8F1",
        4807 => "\uE720",
        _ => null
    };
    private void DrawIconLabel(nint dc, string text, string? glyph, Win32.Rect rect, uint color, uint? iconColor = null)
    {
        if (glyph is null) { DrawLabel(dc, text, rect, color, 0x8825); return; }
        var measure = new Win32.Rect();
        var previous = Win32.SelectObject(dc, font);
        Win32.DrawTextW(dc, text, -1, ref measure, 0xC20); // Calculate single-line text width, retaining ampersands.
        Win32.SelectObject(dc, previous);
        var textWidth = Math.Min(measure.Right - measure.Left, Math.Max(0, rect.Right - rect.Left - 28));
        var left = rect.Left + Math.Max(2, (rect.Right - rect.Left - textWidth - 24) / 2);
        var icon = rect; icon.Left = left; icon.Right = left + 18;
        DrawMenuGlyph(dc, glyph, icon, iconColor ?? color);
        rect.Left = left + 24; rect.Right = rect.Left + textWidth;
        DrawLabel(dc, text, rect, color);
    }
    private void AddMenu(nint menu, int id, string label, bool selected = false, bool enabled = true)
    {
        var glyph = id switch
        {
            201 => "\uE72C", // Reconnect
            202 => "\uF3B1", // Sign out
            1 => "\uE713", // Settings
            2 => paused ? "\uE768" : "\uE769", // Play / pause
            6 => "\uE8C8", // Copy
            7 => "\uE7E8", // Power
            _ => null
        };
        var data = MenuLabel(new(label, selected, Glyph: glyph));
        if (Win32.AppendOwnerMenu(menu, 0x100u | (selected ? 8u : 0u) | (enabled ? 0u : 1u), (nuint)id, data) == 0) throw new InvalidOperationException("Could not create tray menu entry.");
    }
    private void DrawMenuGlyph(nint dc, string glyph, Win32.Rect rect, uint color)
    {
        var previous = Win32.SelectObject(dc, menuIconFont);
        try
        {
            Win32.SetBkMode(dc, 1);
            Win32.SetTextColor(dc, color);
            Win32.DrawTextW(dc, glyph, -1, ref rect, 0x825);
        }
        finally { Win32.SelectObject(dc, previous); }
    }
    private nint MenuLabel(MenuEntry entry)
    {
        var label = new AccessibleMenuLabel(entry.Label);
        menuLabels.Add(label); menuEntries[label.Data] = entry;
        return label.Data;
    }
    private void ClearMenuLabels()
    {
        foreach (var label in menuLabels) label.Dispose();
        menuLabels.Clear(); menuEntries.Clear();
    }
    private unsafe void StyleMenu(nint menu)
    {
        var info = new Win32.MenuInfo { Size = (uint)sizeof(Win32.MenuInfo), Mask = 2, Background = surfaceBrush };
        Win32.SetMenuInfo(menu, in info);
    }
    private unsafe bool HandlePresentationMessage(nint hwnd, uint message, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (message is 0x1A or 0x31A or 0x15)
        {
            // System theme broadcasts arrive at each top-level window; update all surfaces once.
            if (hwnd == messageWindow || hwnd == settingsWindow || hwnd == setupWindow) ApplyTheme();
            return false;
        }
        if (message == 0x14 && (hwnd == settingsWindow || hwnd == hudWindow || hwnd == toolsWindow || hwnd == answerWindow || hwnd == connectedWindow || hwnd == setupWindow))
        {
            Win32.GetClientRect(hwnd, out var rect);
            Win32.FillRect((nint)wParam, in rect, background);
            if (hwnd == settingsWindow)
            {
                PaintSettingsHeader((nint)wParam);
                var card = new Win32.Rect { Left = 24, Top = 186, Right = 704, Bottom = 544 };
                Rounded((nint)wParam, card, surfaceBrush, 16);
            }
            result = 1; return true;
        }
        if (message == 0x2C && lParam != 0) // WM_MEASUREITEM
        {
            var item = (Win32.MeasureItem*)lParam;
            if (item->Type == 3) { item->Height = 28; result = 1; return true; }
            if (item->Type != 1) return false;
            item->Width = 266; item->Height = 34; result = 1; return true;
        }
        if (message != 0x2B || lParam == 0) return false; // WM_DRAWITEM
        var draw = (Win32.DrawItem*)lParam;
        var box = draw->Rect;
        if (draw->Type == 5 && DrawHeaderControl(draw)) { result = 1; return true; }
        if (draw->Type == 3)
        {
            var selected = (draw->State & 1) != 0;
            Win32.FillRect(draw->Dc, in box, selected ? selectionBrush : inputBackground);
            box.Left += 10; box.Right -= 8;
            DrawComboLabel(draw->Window, draw->Dc, ComboText(draw->Window, draw->ItemId), box, theme.Text);
            result = 1; return true;
        }
        if (draw->Type == 1)
        {
            if (!menuEntries.TryGetValue((nint)draw->Data, out var entry)) return false;
            var selected = (draw->State & 1) != 0;
            Win32.FillRect(draw->Dc, in box, surfaceBrush);
            if (selected)
            {
                var hover = box; hover.Left += 4; hover.Right -= 4; hover.Top += 1; hover.Bottom -= 1;
                Rounded(draw->Dc, hover, selectionBrush, 8);
            }
            var label = box; label.Left += 34; label.Right -= 26;
            DrawLabel(draw->Dc, entry.Label, label, (draw->State & 6) != 0 ? theme.Muted : theme.Text);
            var mark = box; mark.Left += 10; mark.Right = mark.Left + 20;
            if (entry.Checked) DrawLabel(draw->Dc, "✓", mark, theme.Accent);
            else if (entry.Glyph is not null) DrawMenuGlyph(draw->Dc, entry.Glyph, mark, theme.Muted);
            if (entry.Submenu) { mark.Left = box.Right - 25; mark.Right = box.Right - 8; DrawLabel(draw->Dc, "›", mark, theme.Muted); }
            result = 1; return true;
        }
        if (draw->Type == 101 && draw->Id == 1014)
        {
            var selected = Win32.SendMessageW(tabControl, 0x130B, 0, 0) == draw->ItemId;
            Win32.FillRect(draw->Dc, in box, background);
            if (selected) Rounded(draw->Dc, box, selectionBrush, 8);
            if (draw->ItemId < TabNames.Length) DrawIconLabel(draw->Dc, TabNames[draw->ItemId], SettingsTabIcons[draw->ItemId], box, selected ? theme.Text : theme.Muted, theme.TabAccent((int)draw->ItemId));
            result = 1; return true;
        }
        if (draw->Type == 4)
        {
            Win32.FillRect(draw->Dc, in box, pages.Any(page => page.Contains(draw->Window)) ? surfaceBrush : background);
            var hot = ControlIsHovered(draw->Window) && (draw->State & 4) == 0;
            if (helpEntries.ContainsKey((int)draw->Id))
            {
                if ((draw->State & 1) != 0 || hot) Rounded(draw->Dc, box, selectionBrush, 28);
                DrawMenuGlyph(draw->Dc, "\uE946", box, theme.Muted);
                if ((draw->State & 0x10) != 0) Win32.DrawFocusRect(draw->Dc, in box);
                result = 1; return true;
            }
            var primary = draw->Id is 101 or 4803;
            var pressed = (draw->State & 1) != 0;
            Rounded(draw->Dc, box, primary ? hot ? accentHover : accent : hot ? accent : borderBrush, 10);
            if (!primary) { box.Left++; box.Right--; box.Top++; box.Bottom--; Rounded(draw->Dc, box, pressed || hot ? selectionBrush : inputBackground, 8); }
            DrawIconLabel(draw->Dc, Text(draw->Window), SettingsActionIcon(draw->Id), box, (draw->State & 4) != 0 ? theme.Muted : primary ? theme.AccentText : theme.Text);
            if ((draw->State & 0x10) != 0)
            {
                box.Left += 4; box.Top += 4; box.Right -= 4; box.Bottom -= 4;
                Win32.DrawFocusRect(draw->Dc, in box);
            }
            result = 1; return true;
        }
        return false;
    }

    private unsafe void PaintTabs(nint dc)
    {
        Win32.GetClientRect(tabControl, out var area);
        Win32.FillRect(dc, in area, background);
        var selected = (int)Win32.SendMessageW(tabControl, 0x130B, 0, 0);
        for (var i = 0; i < TabNames.Length; i++)
        {
            var rect = new Win32.Rect();
            Win32.SendMessageW(tabControl, 0x130A, (nuint)i, (nint)(&rect));
            rect.Left += 2; rect.Right -= 2; rect.Top += 1; rect.Bottom -= 1;
            if (i == selected || i == hoveredTab) Rounded(dc, rect, selectionBrush, 8);
            DrawIconLabel(dc, TabNames[i], SettingsTabIcons[i], rect, i == selected || i == hoveredTab ? theme.Text : theme.Muted, theme.TabAccent(i));
            if (i == selected) { var underline = rect; underline.Top = underline.Bottom - 3; Win32.FillRect(dc, in underline, accent); }
        }
    }

    private static unsafe string ComboText(nint window, uint index)
    {
        if (index == uint.MaxValue) return "";
        var length = (int)Win32.SendMessageW(window, 0x149, index, 0);
        if (length is < 0 or > 4096) return "";
        var text = new char[length + 1];
        fixed (char* pointer = text) Win32.SendMessageW(window, 0x148, index, (nint)pointer);
        return new string(text, 0, length);
    }
    private void PaintCombo(nint window, nint dc)
    {
        Win32.GetClientRect(window, out var rect);
        Win32.FillRect(dc, in rect, surfaceBrush);
        var focused = ControlHasFocus(window);
        var hot = ControlIsHovered(window);
        Rounded(dc, rect, focused || hot ? accent : borderBrush, 8);
        rect.Left++; rect.Right--; rect.Top++; rect.Bottom--;
        var fill = rect;
        if (focused) { fill.Left++; fill.Right--; fill.Top++; fill.Bottom--; }
        Rounded(dc, fill, hot ? selectionBrush : inputBackground, 6);
        var arrow = rect; arrow.Left = arrow.Right - 29;
        DrawLabel(dc, "▾", arrow, theme.Muted, 0x8825);
        if (!editableCombos.Contains(window))
        {
            rect.Left += 11; rect.Right -= 32;
            DrawComboLabel(window, dc, Text(window), rect, theme.Text);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint ComboProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var self = current;
        if (self is null) return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
        if (message is 0x133 or 0x134 or 0x138)
        {
            Win32.SetTextColor((nint)wParam, self.theme.Text);
            var hot = self.ControlIsHovered(hwnd);
            Win32.SetBkColor((nint)wParam, hot ? self.theme.Selection : self.theme.Field);
            return hot ? self.selectionBrush : self.inputBackground;
        }
        if (message == 0x14) return 1;
        if (message == 0x318) { self.PaintCombo(hwnd, (nint)wParam); return 0; }
        if (message == 0xF)
        {
            var dc = Win32.BeginPaint(hwnd, out var paint);
            try { self.PaintCombo(hwnd, dc); } finally { Win32.EndPaint(hwnd, in paint); }
            return 0;
        }
        if (message == 0x82) Win32.RemoveWindowSubclass(hwnd, &ComboProcedure, id);
        return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint TabProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var self = current;
        if (self is null) return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
        if (message == 0x14) return 1;
        if (message == 0x200)
        {
            var x = (short)((long)lParam & 0xFFFF); var y = (short)(((long)lParam >> 16) & 0xFFFF);
            var hover = -1;
            for (var index = 0; index < TabNames.Length; index++)
            {
                var box = new Win32.Rect();
                Win32.SendMessageW(hwnd, 0x130A, (nuint)index, (nint)(&box));
                if (x >= box.Left && x < box.Right && y >= box.Top && y < box.Bottom) { hover = index; break; }
            }
            if (self.hoveredTab != hover) { self.hoveredTab = hover; Win32.InvalidateRect(hwnd, 0, 0); }
            var tracking = new HelpMouseTracking { Size = (uint)sizeof(HelpMouseTracking), Flags = 2, Window = hwnd };
            TrackMouseEvent(&tracking);
        }
        if (message == 0x2A3) { self.hoveredTab = -1; Win32.InvalidateRect(hwnd, 0, 0); }
        if (message == 0x318) { self.PaintTabs((nint)wParam); return 0; }
        if (message == 0xF)
        {
            var dc = Win32.BeginPaint(hwnd, out var paint);
            try { self.PaintTabs(dc); } finally { Win32.EndPaint(hwnd, in paint); }
            return 0;
        }
        if (message == 0x82) Win32.RemoveWindowSubclass(hwnd, &TabProcedure, id);
        return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
    }
}
