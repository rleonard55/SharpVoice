using System.Runtime.InteropServices;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private void DrawComboLabel(nint window, nint dc, string label, Win32.Rect rect, uint color)
    {
        if (fields.GetValueOrDefault("Backend") == window || toolFields.GetValueOrDefault("Backend") == window || toolFields.GetValueOrDefault("RetryBackend") == window)
        {
            // Keep labels/IDs native and unchanged for accessibility, selection and persistence.
            var provider = choices["Backend"].FirstOrDefault(choice => choice.Label == label).Id;
            var icon = rect;
            icon.Top += Math.Max(0, (rect.Bottom - rect.Top - 20) / 2);
            icon.Bottom = icon.Top + 20; icon.Right = icon.Left + 20;
            if (provider is "none" or "embedded")
                DrawMenuGlyph(dc, provider == "none" ? "\uE720" : "\uE977", icon, theme.Muted); // Mic / local device.
            else if (provider is not null && providerIcons?.Find(provider, theme.Dark) is nint handle && handle != 0)
                DrawProviderIcon(dc, icon.Left, icon.Top, handle, 20, 20, 0, 0, 3); // DI_NORMAL: preserve alpha.
            rect.Left += 28;
        }
        DrawLabel(dc, label, rect, color);
    }
    [LibraryImport("user32.dll", EntryPoint = "DrawIconEx")]
    private static partial int DrawProviderIcon(nint dc, int x, int y, nint icon, int width, int height, uint frame, nint brush, uint flags);
}
