namespace SharpVoice.Core;

public sealed record LocalAiModel(string Id, string Label, long SizeBytes, string Sha256, string DownloadUrl)
{
    public string FileName => Id + ".gguf";
}

public static class LocalAiModels
{
    public static LocalAiModel Default => All[0];
    public static IReadOnlyList<LocalAiModel> All { get; } = Array.AsReadOnly(new[]
    {
        new LocalAiModel("qwen2.5-0.5b-instruct-q4_k_m", "Qwen 2.5 0.5B (Fastest, 491 MB)", 491400032,
            "74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db",
            "https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf"),
        new LocalAiModel("qwen2.5-1.5b-instruct-q4_k_m", "Qwen 2.5 1.5B (Larger, 1.12 GB)", 1117320736,
            "6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e",
            "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf")
    });
    public static LocalAiModel? Find(string id) => id == "auto" ? Default : All.FirstOrDefault(x => x.Id == id);
    public static LocalAiModel Resolve(string id) => Find(id) ?? throw new ArgumentException("Choose a supported local AI model from the list.");
}

public enum LocalModelStage { Checking, Downloading, Verifying, Ready }
public sealed record LocalModelProgress(string ModelId, LocalModelStage Stage, long Bytes, long TotalBytes)
{
    public int Percent => TotalBytes <= 0 ? 0 : (int)Math.Clamp(Bytes * 100 / TotalBytes, 0, 100);
}
