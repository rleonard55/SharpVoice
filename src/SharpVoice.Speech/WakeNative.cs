using System.Runtime.InteropServices;
using SharpVoice.Core;

namespace SharpVoice.Speech;

// ABI pinned to upstream sherpa-onnx v1.12.32 c-api.h. All strings are UTF-8 owned by caller during creation.
internal static unsafe partial class WakeNative
{
    static WakeNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(WakeNative).Assembly, (name, _, _) =>
        {
            if (name != "sherpa-onnx-c-api") return 0;
            var root = NativeRuntimeBundle.Ensure();
            var directory = root is null ? Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native") : Path.Combine(root, "wake");
            var onnx = Path.Combine(directory, "onnxruntime.dll");
            if (File.Exists(onnx)) NativeLibrary.Load(onnx);
            return NativeLibrary.Load(Path.Combine(directory, "sherpa-onnx-c-api.dll"));
        });
    }
    [StructLayout(LayoutKind.Sequential)] internal struct ModelConfig
    {
        public nint Encoder, Decoder, Joiner, ParaEncoder, ParaDecoder, Zipformer, Tokens;
        public int Threads; public nint Provider; public int Debug;
        public nint ModelType, ModelingUnit, BpeVocab, TokensBuffer; public int TokensBufferSize;
        public nint Nemo, Tone;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Config
    {
        public int SampleRate, Features; public ModelConfig Model;
        public int Paths, TrailingBlanks; public float Score, Threshold;
        public nint KeywordsFile, KeywordsBuffer; public int KeywordsSize;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Result
    {
        public nint Keyword, Tokens, TokensArray; public int Count;
        public float* Timestamps; public float StartTime; public nint Json;
    }
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxCreateKeywordSpotter")] internal static partial nint Create(in Config config);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxDestroyKeywordSpotter")] internal static partial void Destroy(nint spotter);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxCreateKeywordStream")] internal static partial nint Stream(nint spotter);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxDestroyOnlineStream")] internal static partial void DestroyStream(nint stream);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxOnlineStreamAcceptWaveform")] internal static partial void Accept(nint stream, int sampleRate, float* samples, int count);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxIsKeywordStreamReady")] internal static partial int Ready(nint spotter, nint stream);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxDecodeKeywordStream")] internal static partial void Decode(nint spotter, nint stream);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxResetKeywordStream")] internal static partial void Reset(nint spotter, nint stream);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxGetKeywordResult")] internal static partial Result* Get(nint spotter, nint stream);
    [LibraryImport("sherpa-onnx-c-api", EntryPoint = "SherpaOnnxDestroyKeywordResult")] internal static partial void FreeResult(Result* result);
}
