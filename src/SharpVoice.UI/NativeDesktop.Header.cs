using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    // The same compact brand treatment is shared by Settings and first-run setup.
    private void PaintSettingsHeader(nint dc, int originX = 0, int originY = 0, Win32.Rect? clip = null)
    {
        var area = clip ?? new Win32.Rect { Left = 16, Top = 12, Right = 712, Bottom = 94 };
        Win32.FillRect(dc, in area, background);
        if (clip is not null) return;
        var mark = new Win32.Rect { Left = 32, Top = 24, Right = 80, Bottom = 72 };
        Rounded(dc, mark, accent, 18);
        DrawMenuGlyph(dc, "\uE720", mark, theme.AccentText);
    }

    private unsafe bool DrawHeaderControl(Win32.DrawItem* draw)
    {
        var title = draw->Window == settingsTitle || draw->Window == setupTitle;
        var tagline = draw->Window == settingsTagline || draw->Window == setupTagline;
        if (!title && !tagline) return false;
        Win32.FillRect(draw->Dc, in draw->Rect, background);
        var previous = Win32.SelectObject(draw->Dc, title ? headingFont : font);
        try
        {
            Win32.SetBkMode(draw->Dc, 1);
            Win32.SetTextColor(draw->Dc, title ? theme.Text : theme.Muted);
            var rect = draw->Rect;
            Win32.DrawTextW(draw->Dc, Text(draw->Window), -1, ref rect, 0x8824);
        }
        finally { Win32.SelectObject(draw->Dc, previous); }
        return true;
    }

    private void PaintCard(nint dc, Win32.Rect bounds, int section)
    {
        Rounded(dc, bounds, borderBrush, 18);
        bounds.Left++; bounds.Top++; bounds.Right--; bounds.Bottom--;
        Rounded(dc, bounds, surfaceBrush, 16);
        var line = new Win32.Rect { Left = bounds.Left + 15, Right = bounds.Left + 55, Top = bounds.Top, Bottom = bounds.Top + 3 };
        var brush = Win32.GetStockObject(18);
        var previous = SetDCBrushColor(dc, theme.TabAccent(section));
        Win32.FillRect(dc, in line, brush);
        SetDCBrushColor(dc, previous);
    }
}
