using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private float meterLevel = -100, meterGate;
    private bool meterHasLevel, meterHasGate;

    private void RefreshInputMeter()
    {
        if (inputLabel == 0 || inputMeter == 0) return;
        var reading = ReadLevel?.Invoke();
        meterHasLevel = reading.HasValue && float.IsFinite(reading.Value);
        meterLevel = meterHasLevel ? Math.Clamp(reading!.Value, -100, 0) : -100;
        meterHasGate = fields.TryGetValue("NoiseGateDb", out var edit)
            && float.TryParse(Text(edit), CultureInfo.InvariantCulture, out meterGate) && meterGate is >= -90 and <= 0;
        string text;
        if (paused) text = "INPUT LEVEL   Microphone paused";
        else if (!meterHasLevel) text = "INPUT LEVEL · Waiting";
        else if (!meterHasGate) text = $"INPUT LEVEL   {meterLevel:F0} dBFS · Enter a valid gate (−90 to 0 dB)";
        else
        {
            var delta = meterLevel - meterGate;
            var distance = Math.Abs(delta).ToString(Math.Abs(delta) < 1 ? "F1" : "F0", CultureInfo.InvariantCulture);
            var comparison = delta == 0 ? "at gate" : $"{distance} dB {(delta > 0 ? "above" : "below")} gate";
            text = $"INPUT   {meterLevel:F0} dBFS · Gate {meterGate:F0} dB · {comparison}";
        }
        Win32.SetWindowTextW(inputLabel, text);
        // Keep the native progress value for accessibility while drawing the threshold overlay ourselves.
        Win32.SendMessageW(inputMeter, 0x402, (nuint)(paused ? 0 : (int)(meterLevel + 100)), 0);
        Win32.InvalidateRect(inputMeter, 0, 0);
    }

    private void PaintInputMeter(nint dc)
    {
        Win32.GetClientRect(inputMeter, out var area);
        Win32.FillRect(dc, in area, surfaceBrush);
        var track = area; track.Top += 6; track.Bottom -= 6;
        Win32.FillRect(dc, in track, theme.Dark ? inputBackground : selectionBrush);
        var width = Math.Max(0, track.Right - track.Left);
        int X(float db) => track.Left + (int)MathF.Round(Math.Clamp((db + 100) / 100, 0, 1) * width);
        if (meterHasLevel && !paused)
        {
            var fill = track; fill.Right = X(meterLevel);
            Win32.FillRect(dc, in fill, borderBrush);
            if (meterHasGate && meterLevel >= meterGate)
            {
                fill.Left = X(meterGate);
                if (fill.Right > fill.Left) Win32.FillRect(dc, in fill, accent);
            }
        }
        if (meterHasGate)
        {
            var marker = area; marker.Left = Math.Clamp(X(meterGate) - 1, 0, Math.Max(0, area.Right - 2)); marker.Right = marker.Left + 2;
            var brush = Win32.GetStockObject(18); // DC_BRUSH; reuse rather than allocate a brush on every timer tick.
            var oldColor = SetDCBrushColor(dc, theme.Text);
            Win32.FillRect(dc, in marker, brush);
            SetDCBrushColor(dc, oldColor);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint MeterProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var self = current;
        if (self is null) return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
        if (message == 0x14) return 1;
        if (message == 0x318) { self.PaintInputMeter((nint)wParam); return 0; }
        if (message == 0xF)
        {
            var dc = Win32.BeginPaint(hwnd, out var paint);
            try { self.PaintInputMeter(dc); } finally { Win32.EndPaint(hwnd, paint); }
            return 0;
        }
        if (message == 0x82) Win32.RemoveWindowSubclass(hwnd, &MeterProcedure, id);
        return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
    }
    [LibraryImport("gdi32.dll")] private static partial uint SetDCBrushColor(nint dc, uint color);
}
