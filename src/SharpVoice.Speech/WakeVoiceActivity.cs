using System.Runtime.InteropServices;

namespace SharpVoice.Speech;

public sealed record WakeSpeechActivity(long Epoch, long Sample, long LastSpeechSample)
{
    public TimeSpan Silence => TimeSpan.FromSeconds(Math.Max(0, Sample - LastSpeechSample) / 16000d);
}

// Used only by the keyword worker. Speech ending must not depend on the user's
// transcription noise gate: a permissive gate also lets continuous room noise through.
internal interface IWakeSpeechDetector : IDisposable
{
    void Reset(long first);
    WakeSpeechActivity Process(ReadOnlySpan<float> samples, long first, long epoch);
}

internal sealed class WakeVoiceActivity : IWakeSpeechDetector
{
    private nint detector;
    private long lastSpeech;
    private int buffered;
    public WakeVoiceActivity(string model)
    {
        var path = Marshal.StringToCoTaskMemUTF8(model);
        var cpu = Marshal.StringToCoTaskMemUTF8("cpu");
        try
        {
            var config = new WakeNative.VadConfig
            {
                Silero = new() { Model = path, Threshold = .35f, MinSilence = .1f, MinSpeech = .05f, Window = 512, MaxSpeech = 30 },
                SampleRate = 16000, Threads = 1, Provider = cpu
            };
            detector = WakeNative.CreateVad(in config, 32);
            if (detector == 0) throw new InvalidOperationException("Could not load local speech-end detection. Retry Wake Phrases preparation.");
        }
        finally { Marshal.FreeCoTaskMem(path); Marshal.FreeCoTaskMem(cpu); }
    }
    public void Reset(long first)
    {
        WakeNative.ResetVad(detector); lastSpeech = first; buffered = 0;
    }
    public unsafe WakeSpeechActivity Process(ReadOnlySpan<float> samples, long first, long epoch)
    {
        // Bound each observation even when WASAPI delivers a batch of packets.
        for (var offset = 0; offset < samples.Length; offset += 512)
        {
            var count = Math.Min(512, samples.Length - offset);
            fixed (float* input = samples.Slice(offset, count)) WakeNative.AcceptVad(detector, input, count);
            if (WakeNative.VadDetected(detector) != 0) lastSpeech = first + offset + count;
            buffered += count;
            // The upstream detector also buffers/copies the current segment.
            // Reset that storage periodically so a long request cannot overrun
            // the native ring. Preserve lastSpeech across this brief model warmup:
            // resetting the silence clock would prevent 8–10 second auto-stops.
            if (buffered >= 16000 * 8) { WakeNative.ResetVad(detector); buffered = 0; }
            WakeNative.ClearVad(detector); // We need activity, not retained speech segments.
        }
        return new(epoch, first + samples.Length, lastSpeech);
    }
    public void Dispose()
    {
        if (detector == 0) return;
        WakeNative.DestroyVad(detector); detector = 0;
    }
}

internal static unsafe partial class WakeNative
{
    // sherpa-onnx v1.12.32 c-api.h: Silero and Ten share this field layout.
    [StructLayout(LayoutKind.Sequential)] internal struct VadModelConfig
    {
        public nint Model;
        public float Threshold, MinSilence, MinSpeech;
        public int Window;
        public float MaxSpeech;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct VadConfig
    {
        public VadModelConfig Silero;
        public int SampleRate, Threads;
        public nint Provider;
        public int Debug;
        public VadModelConfig Ten;
    }
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxCreateVoiceActivityDetector")] internal static partial nint CreateVad(in VadConfig config, float bufferSeconds);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxDestroyVoiceActivityDetector")] internal static partial void DestroyVad(nint detector);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxVoiceActivityDetectorAcceptWaveform")] internal static partial void AcceptVad(nint detector, float* samples, int count);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxVoiceActivityDetectorDetected")] internal static partial int VadDetected(nint detector);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxVoiceActivityDetectorClear")] internal static partial void ClearVad(nint detector);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxVoiceActivityDetectorReset")] internal static partial void ResetVad(nint detector);
}
