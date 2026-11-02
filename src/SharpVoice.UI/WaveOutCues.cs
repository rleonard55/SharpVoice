using System.Runtime.InteropServices;
using SharpVoice.Core;

namespace SharpVoice.UI;

/// <summary>One open PCM output device; all cue buffers stay prepared between plays.</summary>
internal sealed unsafe partial class WaveOutCues : IDisposable
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort Tag, Channels;
        public uint SamplesPerSecond, BytesPerSecond;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public nint Data;
        public uint BufferLength, BytesRecorded;
        public nuint User;
        public uint Flags, Loops;
        public nint Next;
        public nuint Reserved;
    }

    private readonly ManualResetEvent completed = new(false);
    private readonly WaitHandle[] waits;
    private readonly Dictionary<FeedbackCue, nint> buffers = [];
    private nint device;

    public WaveOutCues(CancellationToken stop)
    {
        waits = [stop.WaitHandle, completed];
        try
        {
            var format = new WaveFormat { Tag = 1, Channels = 1, SamplesPerSecond = 44100, BytesPerSecond = 88200, BlockAlign = 2, BitsPerSample = 16 };
            Check(waveOutOpen(out device, uint.MaxValue, in format, (nuint)completed.SafeWaitHandle.DangerousGetHandle(), 0, 0x50000)); // CALLBACK_EVENT
            foreach (var (cue, name) in new[] { (FeedbackCue.RecordingStart, "recording-start"), (FeedbackCue.RecordingStop, "recording-stop"), (FeedbackCue.Warning, "warning"), (FeedbackCue.InsertionSuccess, "insertion-success"), (FeedbackCue.None, "recording-start") })
            {
                var wav = FeedbackAssets.Read(name + ".wav");
                // These generated embedded assets have a canonical 44-byte PCM WAV header.
                if (wav.Length < 44 || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wav.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8)
                    || BitConverter.ToUInt32(wav, 16) != 16 || BitConverter.ToUInt16(wav, 20) != 1 || BitConverter.ToUInt16(wav, 22) != 1
                    || BitConverter.ToUInt32(wav, 24) != 44100 || BitConverter.ToUInt16(wav, 34) != 16 || !wav.AsSpan(36, 4).SequenceEqual("data"u8)
                    || BitConverter.ToUInt32(wav, 40) != wav.Length - 44)
                    throw new InvalidDataException("Invalid embedded cue format.");
                var header = (WaveHeader*)NativeMemory.AllocZeroed((nuint)sizeof(WaveHeader));
                buffers.Add(cue, (nint)header);
                header->BufferLength = (uint)(wav.Length - 44);
                header->Data = Marshal.AllocHGlobal((int)header->BufferLength);
                Marshal.Copy(wav, 44, header->Data, (int)header->BufferLength);
                if (cue == FeedbackCue.None) NativeMemory.Clear((void*)header->Data, header->BufferLength);
                Check(waveOutPrepareHeader(device, header, (uint)sizeof(WaveHeader)));
            }
            Play(FeedbackCue.None); // Prime the driver with silence before signaling Ready; opening alone can defer startup until first write.
        }
        catch (Exception ex)
        {
            Dispose();
            if (ResourcesRetained) throw new CueResourcesRetainedException(ex);
            throw;
        }
    }

    public bool Play(FeedbackCue cue)
    {
        var header = (WaveHeader*)buffers[cue];
        completed.Reset();
        Check(waveOutWrite(device, header, (uint)sizeof(WaveHeader)));
        while ((Volatile.Read(ref header->Flags) & 1) == 0) // WHDR_DONE; open notifications can also signal the event.
        {
            var signal = WaitHandle.WaitAny(waits, TimeSpan.FromSeconds(2));
            if (signal == 0) return false;
            if (signal == WaitHandle.WaitTimeout) throw new TimeoutException("Cue output stopped responding.");
            completed.Reset();
        }
        return true;
    }

    public void Dispose()
    {
        if (device != 0)
        {
            var released = WaveOutCleanup.TryRelease(buffers.Values.ToArray(),
                () => waveOutReset(device),
                buffer =>
                {
                    var header = (WaveHeader*)buffer;
                    return (header->Flags & 2) == 0 || waveOutUnprepareHeader(device, header, (uint)sizeof(WaveHeader)) == 0;
                },
                buffer =>
                {
                    var header = (WaveHeader*)buffer;
                    Marshal.FreeHGlobal(header->Data);
                    NativeMemory.Free(header);
                },
                () => waveOutClose(device) == 0,
                () => completed.Dispose());
            ResourcesRetained = !released;
            // A broken driver can still reference a buffer/event after failed reset/unprepare/close.
            // Keep those native resources alive until process exit rather than risking use-after-free.
            if (!released) completed.SafeWaitHandle.SetHandleAsInvalid();
        }
        buffers.Clear();
        device = 0;
        completed.Dispose();
    }

    internal bool ResourcesRetained { get; private set; }

    private static void Check(uint result) { if (result != 0) throw new InvalidOperationException($"Cue output failed ({result})."); }
    [LibraryImport("winmm.dll")] private static partial uint waveOutOpen(out nint device, uint id, in WaveFormat format, nuint callback, nuint instance, uint flags);
    [LibraryImport("winmm.dll")] private static partial uint waveOutPrepareHeader(nint device, WaveHeader* header, uint size);
    [LibraryImport("winmm.dll")] private static partial uint waveOutWrite(nint device, WaveHeader* header, uint size);
    [LibraryImport("winmm.dll")] private static partial uint waveOutReset(nint device);
    [LibraryImport("winmm.dll")] private static partial uint waveOutUnprepareHeader(nint device, WaveHeader* header, uint size);
    [LibraryImport("winmm.dll")] private static partial uint waveOutClose(nint device);
}
