using System.Runtime.InteropServices;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    private static unsafe void CheckProviderIcons(NativeDesktop desktop, Action<ThemeColors> changeTheme, string? output)
    {
        var window = desktop.SettingsHandle;
        var tabs = Win32.GetDlgItem(window, 1014);
        Win32.SendMessageW(tabs, 0x130C, 1, 0);
        var notification = new Win32.Notification { Window = tabs, Id = 1014, Code = -551 };
        Win32.SendMessageW(window, 0x4E, 1014, (nint)(&notification));
        var combo = Win32.GetDlgItem(window, 1002);
        if (Win32.SendMessageW(combo, 0x146, 0, 0) != 6) throw new Exception("Provider icons changed the available choices.");
        foreach (var palette in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
        {
            changeTheme(palette); Win32.SendMessageW(window, 0x1A, 0, 0);
            Win32.SendMessageW(combo, 0x14E, 1, 0);
            Win32.SendMessageW(window, 0x111, (1u << 16) | 1002, combo);
            if (Win32.SendMessageW(combo, 0x147, 0, 0) != 1) throw new Exception("Drawing changed the selected provider.");
            if (output is null) continue;
            var style = palette.Dark ? "dark" : "light";
            RenderHelp(window, output + "-providers-selected-" + style + ".bmp");
            Win32.SendMessageW(combo, 0x14F, 1, 0);
            Pump();
            var info = new ProviderComboInfo { Size = (uint)sizeof(ProviderComboInfo) };
            if (Win32.SendMessageW(combo, 0x164, 0, (nint)(&info)) == 0 || info.List == 0)
                throw new Exception("Cannot inspect provider dropdown rendering.");
            RenderHelp(info.List, output + "-providers-list-" + style + ".bmp");
            Win32.SendMessageW(combo, 0x14F, 0, 0);
        }
        Console.WriteLine("PASS native provider dropdown choices, selection and light/dark icon rendering.");
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderComboInfo
    {
        public uint Size;
        public Win32.Rect Item, Button;
        public uint ButtonState;
        public nint Combo, Edit, List;
    }
}
