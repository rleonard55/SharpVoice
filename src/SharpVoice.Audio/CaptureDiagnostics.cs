namespace SharpVoice.Audio;

public sealed record CaptureDiagnostics(long Packets, long Frames, long Discontinuities, double LastPacketAgeMs,
    long Epoch, bool Running, float? LevelDb, uint? DeviceRate, ulong? DevicePosition, uint? Flags, long SampleCursor);

internal sealed record NativePacket(uint DeviceRate, ulong Position, uint Flags);

public sealed partial class WasapiAudioRecorder
{
    private readonly bool diagnostics;
    private long diagnosticPackets, diagnosticFrames, diagnosticGaps, diagnosticLastPacket;
    private WasapiCaptureSession? diagnosticCapture;
    public CaptureDiagnostics? Diagnostics
    {
        get
        {
            if (!diagnostics) return null;
            lock (gate)
            {
                var packet = diagnosticCapture?.LastPacket;
                return new(diagnosticPackets, diagnosticFrames, diagnosticGaps,
                    diagnosticLastPacket == 0 ? -1 : Environment.TickCount64 - diagnosticLastPacket,
                    captureEpoch, captureRunning, float.IsFinite(levelDb) ? levelDb : null,
                    packet?.DeviceRate, packet?.Position, packet?.Flags, sampleCursor);
            }
        }
    }
}
