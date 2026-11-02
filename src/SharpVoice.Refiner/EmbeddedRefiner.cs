using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using LLama.Native;
using SharpVoice.Core;

namespace SharpVoice.Refiner;

/// <summary>Optional CPU-only Qwen inference. Weights/context are released after each request.</summary>
public sealed class EmbeddedRefiner(HttpClient http)
{
    public const string ModelName = "qwen2.5-0.5b-instruct-q4_k_m";
    public const string ModelHash = "74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db";
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly LocalModelStore models = new(http);

    public async Task<string> RefineAsync(string transcript, string instructions, Action<string> preview, CancellationToken ct, bool allowDownload = true, string model = "auto")
    {
        await gate.WaitAsync(ct);
        try
        {
            var selected = LocalAiModels.Resolve(model);
            var path = await models.EnsureAsync(selected.Id, p => preview($"{p.Stage} {selected.Label}: {p.Percent}%"), ct, allowDownload);
            ct.ThrowIfCancellationRequested();
            var native = NativeRuntimeBundle.Ensure();
            if (native is not null && !NativeLibraryConfig.LLama.LibraryHasLoaded)
                NativeLibraryConfig.All.WithLibrary(Path.Combine(native, "llama", "llama.dll"), Path.Combine(native, "llama", "mtmd.dll"))
                    .WithCuda(false).WithVulkan(false).WithAutoFallback(false);
            // ModelParams itself queries llama_max_devices; configure the loader first.
            var parameters = new ModelParams(path) { ContextSize = 16384, GpuLayerCount = 0 };
            using var weights = LLamaWeights.LoadFromFile(parameters);
            ct.ThrowIfCancellationRequested();
            var executor = new StatelessExecutor(weights, parameters) { ApplyTemplate = true, SystemMessage = instructions };
            using var sampling = new DefaultSamplingPipeline { Temperature = 0.1f };
            var options = new InferenceParams { MaxTokens = 1024, SamplingPipeline = sampling, OverflowStrategy = ContextOverflowStrategy.ThrowException };
            var result = new StringBuilder();
            var tokens = 0;
            await foreach (var part in executor.InferAsync(transcript, options, ct))
            {
                ct.ThrowIfCancellationRequested();
                tokens++;
                result.Append(part);
                preview(result.ToString());
            }
            ct.ThrowIfCancellationRequested();
            if (tokens >= options.MaxTokens) throw new InvalidDataException("Offline refinement reached its output limit. Nothing was inserted; the transcript is retained.");
            if (string.IsNullOrWhiteSpace(result.ToString())) throw new InvalidDataException("Offline refinement returned no text.");
            return result.ToString().Trim();
        }
        finally { gate.Release(); }
    }

    internal Task<string> EnsureModelAsync(Action<string> status, CancellationToken ct, bool allowDownload = true, string? testPath = null)
    {
        return testPath is null ? models.EnsureAsync("auto", p => status($"{p.Stage}: {p.Percent}%"), ct, allowDownload)
            : models.EnsureAtPathAsync(LocalAiModels.Default, testPath, p => status($"{p.Stage}: {p.Percent}%"), ct, allowDownload);
    }
}
