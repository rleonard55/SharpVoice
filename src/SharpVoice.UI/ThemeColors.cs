using Microsoft.Win32;

namespace SharpVoice.UI;

/// <summary>Colors use Win32 COLORREF (BGR). Theme reads never alter Windows preferences.</summary>
public sealed record ThemeColors(bool Dark, uint Background, uint Surface, uint Field, uint Text, uint Muted, uint Accent, uint Selection, uint Border, uint AccentText)
{
    private static uint Rgb(uint rgb) => ((rgb & 255) << 16) | (rgb & 0xFF00) | (rgb >> 16);
    public static ThemeColors Light { get; } = new(false, Rgb(0xF2F5FF), Rgb(0xE8EEFC), Rgb(0xFFFFFF), Rgb(0x182744), Rgb(0x4C5F80), Rgb(0x225CE0), Rgb(0xDBE6FF), Rgb(0x9AABCA), Rgb(0xFFFFFF));
    public static ThemeColors DarkTheme { get; } = new(true, Rgb(0x151C2B), Rgb(0x1C2940), Rgb(0x22324D), Rgb(0xF2F5FA), Rgb(0xBAC7DE), Rgb(0x479CFF), Rgb(0x30476C), Rgb(0x657DA3), Rgb(0x0B1830));
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
    public uint Indigo => Rgb(Dark ? 0xB1A2FFu : 0x6451B8u);
    public uint Success => Rgb(Dark ? 0x54CCA2u : 0x176B4Fu);
    public uint Warning => Rgb(Dark ? 0xF4C26Cu : 0x825300u);
    public uint Danger => Rgb(Dark ? 0xFFA0A0u : 0xA02232u);
    public uint TabAccent(int index) => index switch { 0 => Success, 1 or 3 => Indigo, 2 => Warning, 4 => Accent, _ => Muted };
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
