using System.Runtime.InteropServices;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHudSmoke
{
    public static void Run(string? output = null)
    {
        var screen = GetDC(0);
        var dc = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, 520, 96);
        var previous = Win32.SelectObject(dc, bitmap);
        try
        {
            using var hud = new Direct2DHud();
            var rect = new Win32.Rect { Right = 520, Bottom = 96 };
            foreach (var theme in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
            {
                if (!hud.Draw(dc, rect, theme, "Done", "This is a longer dictated sentence that now has room to wrap onto a second line inside the pill.", [0, 0.5f, 1], 0, "Whisper tiny.en-q5_1 · AI cleanup skipped", false))
                    throw new Exception("Direct2D renderer fell back instead of drawing.");
                if (GetPixel(dc, 260, 8) != theme.Surface) throw new Exception("Pill surface did not follow theme.");
                if (GetPixel(dc, 1, 1) != theme.Background) throw new Exception("Direct2D background did not follow theme.");
                if (GetPixel(dc, 442, 48) != theme.Accent) throw new Exception("Direct2D waveform missing.");
                if (output is not null)
                {
                    // Read a completed offscreen frame without opening or activating a window.
                    Win32.SelectObject(dc, previous);
                    try { SaveBitmap(screen, bitmap, output + (theme.Dark ? "-dark.bmp" : "-light.bmp")); }
                    finally { Win32.SelectObject(dc, bitmap); }
                }
            }
            Console.WriteLine("PASS native Direct2D/DirectWrite HUD draws rounded pill surface, themed background and compact waveform offscreen.");
        }
        finally
        {
            Win32.SelectObject(dc, previous); Win32.DeleteObject(bitmap); DeleteDC(dc); ReleaseDC(0, screen);
        }
    }
    private static unsafe void SaveBitmap(nint screen, nint bitmap, string path)
    {
        var info = new BitmapInfo { Size = 40, Width = 520, Height = -96, Planes = 1, Bits = 32 };
        var pixels = new byte[520 * 96 * 4];
        fixed (byte* data = pixels)
            if (GetDIBits(screen, bitmap, 0, 96, data, ref info, 0) != 96) throw new Exception("Cannot read HUD frame.");
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4D42); writer.Write(54 + pixels.Length); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(520); writer.Write(-96); writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write(0); writer.Write(pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        writer.Write(pixels);
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int Xppm, Yppm; public uint Used, Important; }
    [LibraryImport("gdi32.dll")] private static unsafe partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte* bits, ref BitmapInfo info, uint usage);
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint window, nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")] private static partial int DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleBitmap(nint dc, int width, int height);
    [LibraryImport("gdi32.dll")] private static partial uint GetPixel(nint dc, int x, int y);
}
