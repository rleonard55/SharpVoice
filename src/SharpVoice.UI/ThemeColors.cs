using Microsoft.Win32;

namespace SharpVoice.UI;

/// <summary>Colors use Win32 COLORREF (BGR). Theme reads never alter Windows preferences.</summary>
public sealed record ThemeColors(bool Dark, uint Background, uint Surface, uint Field, uint Text, uint Muted, uint Accent, uint Selection, uint Border, uint AccentText)
{
    private static uint Rgb(uint rgb) => ((rgb & 255) << 16) | (rgb & 0xFF00) | (rgb >> 16);
    public static ThemeColors Light { get; } = new(false, Rgb(0xF5F7FA), Rgb(0xFFFFFF), Rgb(0xFFFFFF), Rgb(0x182230), Rgb(0x536173), Rgb(0x0067C0), Rgb(0xE4EFFA), Rgb(0xD5DDE7), Rgb(0xFFFFFF));
    public static ThemeColors DarkTheme { get; } = new(true, Rgb(0x171B22), Rgb(0x212731), Rgb(0x282F3B), Rgb(0xF2F5FA), Rgb(0xADB9CA), Rgb(0x86BEFF), Rgb(0x2B4059), Rgb(0x465365), Rgb(0x102035));
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
