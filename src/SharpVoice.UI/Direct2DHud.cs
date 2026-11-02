using System.Runtime.InteropServices;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

/// <summary>Direct2D DC render target and DirectWrite text, with explicit COM lifetime for Native AOT.</summary>
public sealed unsafe partial class Direct2DHud : IDisposable
{
    private nint factory, target, brush, writeFactory, titleFormat, previewFormat, modelFormat, iconFormat;
    private bool unavailable;
    public bool Draw(nint dc, Win32.Rect bounds, ThemeColors theme, string status, string preview, ReadOnlySpan<float> levels, int index, string model = "", bool recording = false)
    {
        if (unavailable) return false;
        try
        {
            EnsureResources();
            Check(((delegate* unmanaged[Stdcall]<nint, nint, Win32.Rect*, int>)Slot(target, 57))(target, dc, &bounds));
            ((delegate* unmanaged[Stdcall]<nint, void>)Slot(target, 48))(target); // BeginDraw
            var background = Color.From(theme.Background);
            ((delegate* unmanaged[Stdcall]<nint, Color*, void>)Slot(target, 47))(target, &background);
            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            var pill = new RoundedRect { Rect = new(1, 1, width - 1, height - 1), RadiusX = height / 2f, RadiusY = height / 2f };
            SetColor(theme.Surface);
            ((delegate* unmanaged[Stdcall]<nint, RoundedRect*, nint, void>)Slot(target, 19))(target, &pill, brush);
            SetColor(theme.Border);
            ((delegate* unmanaged[Stdcall]<nint, RoundedRect*, nint, float, nint, void>)Slot(target, 18))(target, &pill, brush, 1, 0);
            var badge = new RoundedRect { Rect = new(18, height / 2f - 18, 54, height / 2f + 18), RadiusX = 18, RadiusY = 18 };
            SetColor(theme.Selection);
            ((delegate* unmanaged[Stdcall]<nint, RoundedRect*, nint, void>)Slot(target, 19))(target, &badge, brush);
            SetColor(recording ? (uint)0x005050EF : theme.Accent);
            Text("\uE720", iconFormat, new(27, height / 2f - 12, 51, height / 2f + 14));
            SetColor(theme.Text);
            Text(status, titleFormat, new(68, 14, 198, 36));
            SetColor(theme.Muted);
            Text(model, modelFormat, new(200, 17, width - 35, 34));
            SetColor(theme.Text);
            var live = recording && preview.StartsWith("Live Draft", StringComparison.Ordinal);
            Text(preview, previewFormat, new(68, 40, width - (live ? 66 : 94), 76));
            SetColor(theme.Accent);
            for (var i = 0; i < (live ? 5 : 9); i++)
            {
                var value = levels.Length == 0 ? 0 : levels[(index + i * Math.Max(1, levels.Length / 9)) % levels.Length];
                var barHeight = 4 + Math.Clamp(value, 0, 1) * (live ? 18 : 27);
                var x = width - (live ? 53 : 79) + i * 5;
                var bar = new RoundedRect { Rect = new(x, height / 2f - barHeight / 2, x + 3, height / 2f + barHeight / 2), RadiusX = 1.5f, RadiusY = 1.5f };
                ((delegate* unmanaged[Stdcall]<nint, RoundedRect*, nint, void>)Slot(target, 19))(target, &bar, brush);
            }
            var hr = ((delegate* unmanaged[Stdcall]<nint, ulong*, ulong*, int>)Slot(target, 49))(target, null, null);
            if (hr < 0) { ReleaseTarget(); return false; } // Recreate after device loss on the next paint.
            return true;
        }
        catch (COMException) { ReleaseAll(); unavailable = true; return false; }
        catch (DllNotFoundException) { ReleaseAll(); unavailable = true; return false; }
        catch (EntryPointNotFoundException) { ReleaseAll(); unavailable = true; return false; }
    }

    private void EnsureResources()
    {
        if (factory == 0)
        {
            var iid = new Guid("06152247-6F50-465A-9245-118BFD3B6007");
            Check(D2D1CreateFactory(0, in iid, 0, out factory));
            iid = new Guid("B859EE5A-D838-4B5B-A2E8-1ADC7D93DB48");
            Check(DWriteCreateFactory(0, in iid, out writeFactory));
            titleFormat = CreateTextFormat(16, weight: 600);
            previewFormat = CreateTextFormat(13);
            Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(previewFormat, 5))(previewFormat, 0)); // Wrap preview into two lines.
            Check(((delegate* unmanaged[Stdcall]<nint, int, float, float, int>)Slot(previewFormat, 10))(previewFormat, 1, 18, 14));
            modelFormat = CreateTextFormat(10);
            Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(modelFormat, 3))(modelFormat, 1)); // Trailing/right aligned.
            iconFormat = CreateTextFormat(20, "Segoe MDL2 Assets");
        }
        if (target != 0) return;
        // B8G8R8A8_UNORM / IGNORE is required for the layered window's GDI-compatible DC.
        var properties = new RenderTargetProperties { PixelFormat = 87, AlphaMode = 3, DpiX = 96, DpiY = 96 };
        nint created;
        Check(((delegate* unmanaged[Stdcall]<nint, RenderTargetProperties*, nint*, int>)Slot(factory, 16))(factory, &properties, &created));
        target = created;
        var color = Color.From(0);
        Check(((delegate* unmanaged[Stdcall]<nint, Color*, nint, nint*, int>)Slot(target, 8))(target, &color, 0, &created));
        brush = created;
    }
    private nint CreateTextFormat(float size, string family = "Segoe UI", int weight = 400)
    {
        nint format;
        fixed (char* name = family)
        fixed (char* locale = "en-us")
            Check(((delegate* unmanaged[Stdcall]<nint, char*, nint, int, int, int, float, char*, nint*, int>)Slot(writeFactory, 15))
                (writeFactory, name, 0, weight, 0, 5, size, locale, &format));
        Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(format, 5))(format, 1)); // No wrapping inside the pill.
        return format;
    }
    private void Text(string text, nint format, Rect rect)
    {
        fixed (char* value = text)
            ((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, Rect*, nint, uint, int, void>)Slot(target, 27))
                (target, value, (uint)text.Length, format, &rect, brush, 2, 0); // Clip to text bounds.
    }
    private void SetColor(uint value)
    {
        var color = Color.From(value);
        ((delegate* unmanaged[Stdcall]<nint, Color*, void>)Slot(brush, 8))(brush, &color);
    }
    private static nint Slot(nint obj, int index) => (*(nint**)obj)[index];
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static void Release(ref nint obj) { if (obj != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 2))(obj); obj = 0; }
    private void ReleaseTarget() { Release(ref brush); Release(ref target); }
    private void ReleaseAll() { ReleaseTarget(); Release(ref titleFormat); Release(ref previewFormat); Release(ref modelFormat); Release(ref iconFormat); Release(ref writeFactory); Release(ref factory); }
    public void Dispose() => ReleaseAll();

    [StructLayout(LayoutKind.Sequential)] private struct Color
    {
        public float R, G, B, A;
        public static Color From(uint bgr) => new() { R = (bgr & 255) / 255f, G = ((bgr >> 8) & 255) / 255f, B = ((bgr >> 16) & 255) / 255f, A = 1 };
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect(float left, float top, float right, float bottom)
    { public float Left = left, Top = top, Right = right, Bottom = bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct RoundedRect { public Rect Rect; public float RadiusX, RadiusY; }
    [StructLayout(LayoutKind.Sequential)] private struct RenderTargetProperties
    { public int Type, PixelFormat, AlphaMode; public float DpiX, DpiY; public int Usage, MinimumLevel; }
    [LibraryImport("d2d1.dll")] private static partial int D2D1CreateFactory(int type, in Guid iid, nint options, out nint factory);
    [LibraryImport("dwrite.dll")] private static partial int DWriteCreateFactory(int type, in Guid iid, out nint factory);
}
