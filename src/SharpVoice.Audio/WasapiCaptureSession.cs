using System.Buffers;
using System.Runtime.InteropServices;

namespace SharpVoice.Audio;

internal delegate void AudioSamplesHandler(Span<float> samples, bool discontinuity = false);
internal interface IAudioCaptureSession : IDisposable
{
    void Start();
    void Drain(AudioSamplesHandler consume);
}

// Every session owns and releases its native device/COM resources on the capture thread.
internal sealed partial class WasapiCaptureSession : IAudioCaptureSession
{
    private const int SampleRate = WasapiAudioRecorder.SampleRate;
    private nint enumerator, device, client, capture;
    private float[] converted = ArrayPool<float>.Shared.Rent(SampleRate);
    private bool comInitialized, disposed;
    private CaptureContinuity continuity = new();
    internal bool DiagnosticsEnabled { get; set; }
    private uint deviceRate;
    private NativePacket? lastPacket;
    internal NativePacket? LastPacket => Volatile.Read(ref lastPacket);

    public unsafe WasapiCaptureSession(AutoResetEvent ready, string deviceId)
    {
        try
        {
            fixed (nint* devicePointer = &device, clientPointer = &client, capturePointer = &capture)
            {
            Check(CoInitializeEx(0, 0));
            comInitialized = true;
            var clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
            var iid = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
            Check(CoCreateInstance(in clsid, 0, 1, in iid, out enumerator));
            if (string.IsNullOrEmpty(deviceId))
                Check(((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)Slot(enumerator, 4))(enumerator, 1, 0, devicePointer));
            else
                fixed (char* endpoint = deviceId)
                    Check(((delegate* unmanaged[Stdcall]<nint, char*, nint*, int>)Slot(enumerator, 5))(enumerator, endpoint, devicePointer));
            iid = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)Slot(device, 3))(device, &iid, 1, 0, clientPointer));
            WaveFormat* mix;
            Check(((delegate* unmanaged[Stdcall]<nint, WaveFormat**, int>)Slot(client, 8))(client, &mix));
            try { deviceRate = mix->SamplesPerSecond; continuity = new(deviceRate, SampleRate); }
            finally { Marshal.FreeCoTaskMem((nint)mix); }
            var format = new WaveFormat { Tag = 1, Channels = 1, SamplesPerSecond = SampleRate, BytesPerSecond = SampleRate * 2, BlockAlign = 2, BitsPerSample = 16 };
            // Windows audio engine resamples the default microphone to mono 16 kHz PCM.
            Check(((delegate* unmanaged[Stdcall]<nint, int, uint, long, long, WaveFormat*, nint, int>)Slot(client, 3))
                (client, 0, 0x88040000, 1_000_000, 0, &format, 0));
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(client, 13))(client, ready.SafeWaitHandle.DangerousGetHandle()));
            iid = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(client, 14))(client, &iid, capturePointer));
            }
        }
        catch { Dispose(); throw; }
    }

    public unsafe void Start() => Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(client, 10))(client));
    public unsafe void Drain(AudioSamplesHandler consume)
    {
        uint packet;
        Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(capture, 5))(capture, &packet));
        while (packet != 0)
        {
            byte* data;
            uint frames, flags;
            ulong devicePosition;
            Check(((delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, ulong*, ulong*, int>)Slot(capture, 3))
                (capture, &data, &frames, &flags, &devicePosition, null));
            try
            {
                if (DiagnosticsEnabled) Volatile.Write(ref lastPacket, new(deviceRate, devicePosition, flags));
                if (frames > converted.Length)
                {
                    ArrayPool<float>.Shared.Return(converted, clearArray: true);
                    converted = ArrayPool<float>.Shared.Rent(checked((int)frames));
                }
                var samples = converted.AsSpan(0, (int)frames);
                if ((flags & 2) != 0) samples.Clear();
                else for (var i = 0; i < samples.Length; i++) samples[i] = ((short*)data)[i] / 32768f;
                if (frames != 0) consume(samples, continuity.Observe(devicePosition, frames, flags));
            }
            finally { Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(capture, 4))(capture, frames)); }
            Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(capture, 5))(capture, &packet));
        }
    }

    private static unsafe nint Slot(nint obj, int index) => (*(nint**)obj)[index];
    private static unsafe void Release(nint obj) { if (obj != 0) _ = ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 2))(obj); }
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort Tag, Channels;
        public uint SamplesPerSecond, BytesPerSecond;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }

    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(nint reserved, uint flags);
    [LibraryImport("ole32.dll")] private static partial void CoUninitialize();
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    public unsafe void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (client != 0) _ = ((delegate* unmanaged[Stdcall]<nint, int>)Slot(client, 11))(client);
        ArrayPool<float>.Shared.Return(converted, clearArray: true);
        Release(capture); Release(client); Release(device); Release(enumerator);
        if (comInitialized) CoUninitialize();
    }
}
