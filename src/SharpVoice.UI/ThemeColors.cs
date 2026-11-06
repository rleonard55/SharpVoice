using Microsoft.Win32;

namespace SharpVoice.UI;

/// <summary>Colors use Win32 COLORREF (BGR). Theme reads never alter Windows preferences.</summary>
public sealed record ThemeColors(bool Dark, uint Background, uint Surface, uint Field, uint Text, uint Muted, uint Accent, uint Selection, uint Border, uint AccentText)
{
    private static uint Rgb(uint rgb) => ((rgb & 255) << 16) | (rgb & 0xFF00) | (rgb >> 16);
    public static ThemeColors Light { get; } = new(false, Rgb(0xFAFBFE), Rgb(0xFFFFFF), Rgb(0xFFFFFF), Rgb(0x242637), Rgb(0x62697D), Rgb(0x5855DB), Rgb(0xEEECFC), Rgb(0xD7DBE7), Rgb(0xFFFFFF));
    public static ThemeColors DarkTheme { get; } = new(true, Rgb(0x20232F), Rgb(0x272B39), Rgb(0x222633), Rgb(0xF3F3FB), Rgb(0xB1B5C8), Rgb(0x5855DB), Rgb(0x38344E), Rgb(0x42475A), Rgb(0xFFFFFF));
    public uint Header => Rgb(Dark ? 0x284DB8u : 0x3048B2u);
    public uint HeaderShade(float position, float waveOpacity = 0)
    {
        static uint Mix(uint a, uint b, float t)
        {
            uint Channel(int shift) => (uint)MathF.Round(((a >> shift) & 255) * (1 - t) + ((b >> shift) & 255) * t);
            return Channel(0) | Channel(8) << 8 | Channel(16) << 16;
        }
        var baseColor = Mix(Header, Rgb(Dark ? 0x32217Du : 0x3A2A92u), Math.Clamp(position, 0, 1));
        return Mix(baseColor, Rgb(0xA18CE8), Math.Clamp(waveOpacity, 0, .24f));
    }
    public uint HeaderText => Rgb(0xFFFFFF);
    public uint Indigo => Rgb(Dark ? 0xB3ABFFu : 0x5855DBu);
    public uint Success => Rgb(Dark ? 0x70D9BAu : 0x087C65u);
    public uint Warning => Rgb(Dark ? 0xF5C77Eu : 0x8B580Bu);
    public uint Danger => Rgb(Dark ? 0xFFA0A0u : 0xA02232u);
    public uint TabAccent(int index) => Rgb(index switch
    {
        0 => Dark ? 0x75D7C7u : 0x087D73u,
        1 => Dark ? 0xBEA4FFu : 0x7551BEu,
        2 => Dark ? 0xEDC17Au : 0x986214u,
        3 => Dark ? 0x8BBDFFu : 0x296BC0u,
        4 => Dark ? 0xEDA4C0u : 0xAA4971u,
        _ => Dark ? 0xA5B4D2u : 0x586D96u
    });
    public static ThemeColors ReadSystem()
    {
        if (!OperatingSystem.IsWindows()) return Light;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0 ? DarkTheme : Light;
        }
        catch (System.Security.SecurityException) { return Light; }
        catch (UnauthorizedAccessException) { return Light; }
    }
}
