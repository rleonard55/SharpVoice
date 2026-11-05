using SharpVoice.UI;

internal static class ThemeChecks
{
    public static Task IndigoContrast()
    {
        static double Luminance(uint color)
        {
            static double Linear(uint channel) { var c = channel / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
            return .2126 * Linear(color & 255) + .7152 * Linear((color >> 8) & 255) + .0722 * Linear((color >> 16) & 255);
        }
        static void Contrast(uint text, uint surface, double minimum, string label)
        {
            var a = Luminance(text); var b = Luminance(surface);
            var ratio = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
            if (ratio < minimum) throw new Exception($"{label} contrast {ratio:F2}:1 is below {minimum}:1.");
        }
        foreach (var p in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
        {
            Contrast(p.Text, p.Background, 4.5, "Body text");
            Contrast(p.Text, p.Field, 4.5, "Input text");
            Contrast(p.Muted, p.Surface, 4.5, "Secondary text");
            Contrast(p.HeaderText, p.Header, 4.5, "Brand header");
            for (var sample = 0; sample <= 20; sample++)
                foreach (var opacity in new[] { 0f, .16f, .24f })
                    Contrast(p.HeaderText, p.HeaderShade(sample / 20f, opacity), 4.5, "Header gradient and wave text");
            Contrast(p.AccentText, p.Accent, 4.5, "Primary action");
            foreach (var color in new[] { p.Indigo, p.Success, p.Warning, p.Danger }) Contrast(color, p.Surface, 4.5, "Semantic status");
            for (var i = 0; i < 6; i++) Contrast(p.TabAccent(i), p.Background, 3, "Tab icon");
        }
        return Task.CompletedTask;
    }
}
