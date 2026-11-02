using System.Runtime.InteropServices;
using SharpVoice.Platform.Windows.Native;

internal static partial class WindowsHelpSmoke
{
    private static unsafe uint RenderHelp(nint window, string? output, int sampleY = 10)
    {
        Win32.GetWindowRect(window, out var area);
        var width = area.Right - area.Left; var height = area.Bottom - area.Top;
        var screen = GetDC(0); var dc = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = Win32.SelectObject(dc, bitmap);
        try
        {
            if (PrintWindow(window, dc, 2) == 0) throw new Exception("Cannot render native help popup.");
            var color = GetPixel(dc, 10, sampleY);
            if (output is not null)
            {
                Win32.SelectObject(dc, previous);
                var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
                var pixels = new byte[width * height * 4];
                fixed (byte* data = pixels)
                    if (GetDIBits(screen, bitmap, 0, (uint)height, data, ref info, 0) != height) throw new Exception("Cannot read help pixels.");
                using var writer = new BinaryWriter(File.Create(output));
                writer.Write((ushort)0x4D42); writer.Write(54 + pixels.Length); writer.Write(0); writer.Write(54);
                writer.Write(40); writer.Write(width); writer.Write(-height); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(0); writer.Write(pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                writer.Write(pixels);
            }
            return color;
        }
        finally { Win32.SelectObject(dc, previous); Win32.DeleteObject(bitmap); DeleteDC(dc); ReleaseDC(0, screen); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int Xppm, Yppm; public uint Used, Important; }
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint window, nint dc);
    [LibraryImport("user32.dll")] private static partial int PrintWindow(nint window, nint dc, uint flags);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")] private static partial int DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleBitmap(nint dc, int width, int height);
    [LibraryImport("gdi32.dll")] private static partial uint GetPixel(nint dc, int x, int y);
    [LibraryImport("gdi32.dll")] private static unsafe partial int GetDIBits(nint dc, nint bitmap, uint start, uint rows, byte* pixels, ref BitmapInfo info, uint usage);
}
