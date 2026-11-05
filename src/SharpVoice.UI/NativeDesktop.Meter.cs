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
        if (paused) text = "Input · Microphone paused";
        else if (!meterHasLevel) text = "Input level · Waiting";
        else if (!meterHasGate) text = $"Input {meterLevel:F0} dBFS · Enter a valid gate (−90 to 0 dB)";
        else
        {
            var delta = meterLevel - meterGate;
            var distance = Math.Abs(delta).ToString(Math.Abs(delta) < 1 ? "F1" : "F0", CultureInfo.InvariantCulture);
            var comparison = delta == 0 ? "at gate" : $"{distance} dB {(delta > 0 ? "above" : "below")} gate";
            text = $"Input {meterLevel:F0} dBFS · {comparison}";
        }
        Win32.SetWindowTextW(inputLabel, text);
        Win32.InvalidateRect(inputMeter, 0, 0);
    }

    private unsafe void BuildAudioInput()
    {
        inputLabel = Control("STATIC", "Input level · Waiting", 0, 54, 351, 607, 24, 1018);
        inputMeter = gateSlider = Control("msctls_trackbar32", "Noise gate threshold", 0x10000 | 0x10 | 0x40, 54, 380, 607, 30, 1015);
        Win32.SendMessageW(gateSlider, 0x407, 0, -90); Win32.SendMessageW(gateSlider, 0x408, 1, 0);
        Win32.SendMessageW(gateSlider, 0x41B, 0, 24);
        Win32.SendMessageW(gateSlider, 0x405, 1, (nint)(int)MathF.Round(config.NoiseGateDb));
        if (Win32.SetWindowSubclass(gateSlider, &MeterInputProcedure, 4, 0) == 0)
            throw new InvalidOperationException("Could not initialize the combined gate input.");
        Control("STATIC", "Noise gate", 0, 54, 425, 98, 25);
        fields["NoiseGateDb"] = Control("EDIT", config.NoiseGateDb.ToString(CultureInfo.InvariantCulture), 0x10080, 160, 420, 64, 28, 1008);
        Win32.SendMessageW(fields["NoiseGateDb"], 0xD3, 3, (5 << 16) | 5);
        Control("STATIC", "dB", 0, 232, 425, 30, 25);
        AddHelp("NoiseGateDb", "Noise Gate", 1008, 421, 270);
        fields["LiveTranscription"] = Control("BUTTON", "Live Transcript", 0x10003, 430, 419, 204, 30, 1040);
        Win32.SendMessageW(fields["LiveTranscription"], 0xF1, config.LiveTranscription ? 1u : 0u, 0);
        if (Win32.SetWindowSubclass(fields["LiveTranscription"], &TranscriptToggleProcedure, 3, 0) == 0)
            throw new InvalidOperationException("Could not style Live Transcript.");
        AddHelp("LiveTranscription", "Live Transcript", 1040, 421);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputCustomDraw
    {
        public Win32.Notification Header; public uint Stage; public nint Dc;
        public Win32.Rect Rect; public nuint Item; public uint State; public nint Data;
    }
    private unsafe bool DrawInputMeter(nint notification, out nint result)
    {
        result = 0;
        var draw = (InputCustomDraw*)notification;
        if (draw->Header.Window != inputMeter || inputMeter == 0 || draw->Header.Code != -12) return false;
        if (draw->Stage == 1)
        {
            Win32.GetClientRect(inputMeter, out var area); Win32.FillRect(draw->Dc, in area, surfaceBrush);
            result = 0x20; return true; // CDRF_NOTIFYITEMDRAW
        }
        if (draw->Stage != 0x10001) return false;
        if (draw->Item == 3) PaintInputMeter(draw->Dc); // TBCD_CHANNEL
        if (draw->Item == 2)
        {
            var thumb = new Win32.Rect(); Win32.SendMessageW(inputMeter, 0x419, 0, (nint)(&thumb));
            var center = meterHasGate ? MeterPosition(meterGate) : (thumb.Left + thumb.Right) / 2;
            thumb.Left = center - 6; thumb.Right = center + 6; thumb.Top = 2; thumb.Bottom = 28;
            var brush = Win32.GetStockObject(18); var previous = SetDCBrushColor(draw->Dc, theme.Text);
            try
            {
                Rounded(draw->Dc, thumb, brush, 5);
                thumb.Left += 2; thumb.Right -= 2; thumb.Top += 2; thumb.Bottom -= 2;
                SetDCBrushColor(draw->Dc, meterHasGate ? theme.Indigo : theme.Warning); Rounded(draw->Dc, thumb, brush, 3);
            }
            finally { SetDCBrushColor(draw->Dc, previous); }
        }
        result = 4; return true; // CDRF_SKIPDEFAULT; no separate ticks.
    }
    private unsafe int MeterPosition(float db)
    {
        var channel = new Win32.Rect(); Win32.SendMessageW(inputMeter, 0x41A, 0, (nint)(&channel));
        return channel.Left + (int)MathF.Round(Math.Clamp((db + 90) / 90, 0, 1) * (channel.Right - channel.Left));
    }
    private unsafe void PaintInputMeter(nint dc)
    {
        var channel = new Win32.Rect(); Win32.SendMessageW(inputMeter, 0x41A, 0, (nint)(&channel));
        var track = channel; track.Top = 11; track.Bottom = 19;
        Win32.FillRect(dc, in track, theme.Dark ? inputBackground : selectionBrush);
        if (!meterHasLevel || paused) return;
        var fill = track; fill.Right = track.Left + (int)MathF.Round(Math.Clamp((meterLevel + 90) / 90, 0, 1) * (track.Right - track.Left));
        Win32.FillRect(dc, in fill, borderBrush);
        if (meterHasGate && meterLevel >= meterGate)
        {
            fill.Left = MeterPosition(meterGate);
            if (fill.Right > fill.Left) Win32.FillRect(dc, in fill, successBrush);
        }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint MeterInputProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var self = current;
        if (self is not null && message == 0x201 && self.meterHasGate)
        {
            var x = (short)((long)lParam & 0xFFFF); var y = (short)(((long)lParam >> 16) & 0xFFFF);
            if (Math.Abs(x - self.MeterPosition(self.meterGate)) <= 6 && y is >= 2 and <= 28)
            {
                // Typed fractions are drawn exactly between native whole-dB steps.
                // Forward clicks on the visible handle into the native thumb hit box.
                var thumb = new Win32.Rect(); Win32.SendMessageW(hwnd, 0x419, 0, (nint)(&thumb));
                lParam = (((thumb.Top + thumb.Bottom) / 2) << 16) | ((thumb.Left + thumb.Right) / 2);
            }
        }
        var result = Win32.DefSubclassProc(hwnd, message, wParam, lParam);
        if (message == 0x82) Win32.RemoveWindowSubclass(hwnd, &MeterInputProcedure, id);
        return result;
    }
    private void PaintTranscriptToggle(nint dc, nint control)
    {
        Win32.GetClientRect(control, out var area); Win32.FillRect(dc, in area, surfaceBrush);
        var on = Win32.SendMessageW(control, 0xF0, 0, 0) == 1;
        var brush = Win32.GetStockObject(18); var previous = SetDCBrushColor(dc, on ? theme.Indigo : theme.Border);
        try
        {
            var pill = new Win32.Rect { Left = 0, Top = 6, Right = 32, Bottom = 24 }; Rounded(dc, pill, brush, 18);
            var dot = new Win32.Rect { Left = on ? 17 : 3, Top = 9, Right = on ? 29 : 15, Bottom = 21 };
            SetDCBrushColor(dc, theme.HeaderText); Rounded(dc, dot, brush, 12);
        }
        finally { SetDCBrushColor(dc, previous); }
        var label = area; label.Left = 42; DrawLabel(dc, "Live Transcript", label, theme.Text);
        if (ControlHasFocus(control)) Win32.DrawFocusRect(dc, in area);
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint TranscriptToggleProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var self = current; if (self is null) return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
        if (message == 0x14) return 1;
        if (message == 0x318) { self.PaintTranscriptToggle((nint)wParam, hwnd); return 0; }
        if (message == 0xF)
        {
            var dc = Win32.BeginPaint(hwnd, out var paint);
            try { self.PaintTranscriptToggle(dc, hwnd); } finally { Win32.EndPaint(hwnd, paint); } return 0;
        }
        var result = Win32.DefSubclassProc(hwnd, message, wParam, lParam);
        if (message is 7 or 8 or 0xF1 or 0x202 or 0x101 or 0xA) Win32.InvalidateRect(hwnd, 0, 0);
        if (message == 0x82) Win32.RemoveWindowSubclass(hwnd, &TranscriptToggleProcedure, id);
        return result;
    }
    [LibraryImport("gdi32.dll")] private static partial uint SetDCBrushColor(nint dc, uint color);
}
