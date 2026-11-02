using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static unsafe partial class WindowsSettingsSnapshot
{
    public static void Run(string output, string? style = null, int page = 0, float? levelDb = null, float? gateDb = null, string? backend = null)
    {
        var palette = style == "light" ? ThemeColors.Light : style is "dark" or "switch" ? ThemeColors.DarkTheme : ThemeColors.ReadSystem();
        using var desktop = new NativeDesktop(new AppConfig(), () => palette);
        desktop.Initialize(showSettings: false, integrateShell: false);
        var window = desktop.SettingsHandle;
        if (backend == "gemini_antigravity")
        {
            var provider = Win32.GetDlgItem(window, 1002);
            Win32.SendMessageW(provider, 0x14E, 4, 0);
            Win32.SendMessageW(window, 0x111, (1u << 16) | 1002, provider);
            desktop.SetGoogleStatus("Google account is managed by Antigravity.");
        }
        if (backend == "claude_code")
        {
            var provider = Win32.GetDlgItem(window, 1002);
            Win32.SendMessageW(provider, 0x14E, 3, 0);
            Win32.SendMessageW(window, 0x111, (1u << 16) | 1002, provider);
            desktop.SetClaudeStatus("Update Claude Code to 2.1.293 or newer (run claude update), then retry.");
        }
        if (levelDb.HasValue) desktop.ReadLevel = () => levelDb.Value;
        if (gateDb.HasValue) Win32.SetWindowTextW(Win32.GetDlgItem(window, 1008), gateDb.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (levelDb.HasValue || gateDb.HasValue) Win32.SendMessageW(window, 0x113, 3, 0);
        var tabs = Win32.GetDlgItem(window, 1014);
        Win32.SendMessageW(tabs, 0x130C, (nuint)page, 0);
        var selection = new Win32.Notification { Window = tabs, Id = 1014, Code = -551 };
        Win32.SendMessageW(window, 0x4E, 1014, (nint)(&selection));
        SetWindowPos(window, 0, -32000, -32000, 0, 0, 0x15); // no resize, no z-order/focus change
        Win32.ShowWindow(window, 4);
        var screen = GetDC(0);
        var dc = CreateCompatibleDC(screen);
        var rect = new Win32.Rect();
        GetWindowRect(window, &rect);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = Win32.SelectObject(dc, bitmap);
        try
        {
            void Render()
            {
                // A newly shown offscreen window can return a black first frame before composition finishes.
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    UpdateWindow(window);
                    PrintWindow(window, dc, 2);
                    if (GetPixel(dc, 16, 50) == palette.Background) return;
                    Thread.Sleep(20);
                }
                throw new Exception($"Settings background did not match its theme: expected {palette.Background:X6}, got {GetPixel(dc, 16, 50):X6}.");
            }
            Render();
            if (levelDb.HasValue && gateDb.HasValue && page == 0)
            {
                var meterRect = new Win32.Rect();
                GetWindowRect(Win32.GetDlgItem(window, 1016), &meterRect);
                var left = meterRect.Left - rect.Left;
                var top = meterRect.Top - rect.Top;
                var meterWidth = meterRect.Right - meterRect.Left;
                var gateX = (int)MathF.Round(Math.Clamp((gateDb.Value + 100) / 100, 0, 1) * meterWidth);
                var levelX = (int)MathF.Round(Math.Clamp((levelDb.Value + 100) / 100, 0, 1) * meterWidth);
                if (GetPixel(dc, left + Math.Clamp(gateX - 1, 0, meterWidth - 2), top + 2) != palette.Text)
                    throw new Exception("Gate marker was not rendered at the threshold.");
                if (levelDb.Value > gateDb.Value && levelX - gateX > 2)
                {
                    if (GetPixel(dc, left + (gateX + levelX) / 2, top + 11) != palette.Accent)
                        throw new Exception("Above-gate audio was not highlighted.");
                }
                else if (levelX > 2 && GetPixel(dc, left + levelX / 2, top + 11) != palette.Border)
                    throw new Exception("Below-gate audio was not muted.");
            }
            if (style == "switch")
            {
                palette = ThemeColors.Light;
                Win32.SendMessageW(window, 0x1A, 0, 0);
                Render();
            }
            Win32.SelectObject(dc, previous);
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
            var pixels = new byte[width * height * 4];
            fixed (byte* data = pixels)
                if (GetDIBits(screen, bitmap, 0, (uint)height, data, ref info, 0) != height) throw new Exception("Could not extract rendered pixels.");
            using var writer = new BinaryWriter(File.Create(output));
            writer.Write((ushort)0x4D42); writer.Write(54 + pixels.Length); writer.Write(0); writer.Write(54);
            writer.Write(40); writer.Write(width); writer.Write(-height); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(0); writer.Write(pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write(pixels);
        }
        finally
        {
            Win32.SelectObject(dc, previous); Win32.DeleteObject(bitmap); DeleteDC(dc); ReleaseDC(0, screen);
            Win32.ShowWindow(window, 0);
        }
        desktop.Post(() => Win32.PostQuitMessage(0)); desktop.Run();
        Console.WriteLine("PASS rendered OS-themed settings snapshot without taking desktop focus.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int Xppm, Yppm; public uint Used, Important; }
    [LibraryImport("user32.dll")] private static partial int SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint window, nint dc);
    [LibraryImport("user32.dll")] private static partial int GetWindowRect(nint window, Win32.Rect* rect);
    [LibraryImport("user32.dll")] private static partial int PrintWindow(nint window, nint dc, uint flags);
    [LibraryImport("user32.dll")] private static partial int UpdateWindow(nint window);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")] private static partial int DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")] private static partial uint GetPixel(nint dc, int x, int y);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleBitmap(nint dc, int width, int height);
    [LibraryImport("gdi32.dll")] private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint rows, byte* pixels, ref BitmapInfo info, uint usage);
}
