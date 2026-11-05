using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    // Render the same coordinates in the parent and its owner-drawn text controls.
    // This keeps the gradient/waves continuous behind accessible native labels.
    private void PaintSettingsHeader(nint dc, int originX = 0, int originY = 0, Win32.Rect? clip = null)
    {
        const int left = 16, top = 12, width = 696, height = 82, radius = 6;
        var brush = Win32.GetStockObject(18); // DC_BRUSH, owned by GDI.
        var previous = SetDCBrushColor(dc, theme.Header);
        void Fill(int x, int y1, int y2, uint color)
        {
            var rect = new Win32.Rect { Left = x - originX, Right = x + 2 - originX, Top = y1 - originY, Bottom = y2 - originY };
            if (clip is { } bounds)
            {
                rect.Left = Math.Max(rect.Left, bounds.Left); rect.Right = Math.Min(rect.Right, bounds.Right);
                rect.Top = Math.Max(rect.Top, bounds.Top); rect.Bottom = Math.Min(rect.Bottom, bounds.Bottom);
            }
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) return;
            SetDCBrushColor(dc, color); Win32.FillRect(dc, in rect, brush);
        }
        static float Ease(float value) { var t = Math.Clamp(value, 0, 1); return t * (2 - t); }
        try
        {
            for (var column = 0; column < width; column += 2)
            {
                var edge = Math.Min(column, width - column - 2);
                var inset = edge < radius ? (int)Math.Ceiling(radius - Math.Sqrt(radius * radius - Math.Pow(radius - edge - .5, 2))) : 0;
                var y1 = top + inset; var y2 = top + height - inset; var t = column / (float)(width - 2);
                Fill(left + column, y1, y2, theme.HeaderShade(t));
                var wave = top + height - (int)(height * .65f * Ease((t - .48f) / .52f));
                Fill(left + column, Math.Max(y1, wave), y2, theme.HeaderShade(t, .16f));
                var lowerWave = top + height - (int)(height * .28f * Ease((t - .25f) / .75f));
                Fill(left + column, Math.Max(y1, lowerWave), y2, theme.HeaderShade(t, .24f));
            }
        }
        finally { SetDCBrushColor(dc, previous); }
    }

    private unsafe bool DrawHeaderControl(Win32.DrawItem* draw)
    {
        var title = draw->Window == settingsTitle;
        if (!title && draw->Window != settingsTagline) return false;
        PaintSettingsHeader(draw->Dc, 32, title ? 20 : 62, draw->Rect);
        var previous = Win32.SelectObject(draw->Dc, title ? headingFont : font);
        try
        {
            Win32.SetBkMode(draw->Dc, 1); // TRANSPARENT, over the painted graphic.
            Win32.SetTextColor(draw->Dc, theme.HeaderText);
            var rect = draw->Rect;
            Win32.DrawTextW(draw->Dc, Text(draw->Window), -1, ref rect, 0x8824);
        }
        finally { Win32.SelectObject(draw->Dc, previous); }
        return true;
    }
}
