using System.Security.Cryptography;
using SharpVoice.Core;

namespace SharpVoice.Speech;

public sealed class ModelManager(HttpClient http)
{
    // SHA-256 values from upstream Hugging Face LFS metadata, not the model card's SHA-1 column.
    public static string HashFor(string model) => model switch
    {
        "tiny.en" => "921e4cf8686fdd993dcd081a5da5b6c365bfde1162e72b08d75ac75289920b1f",
        "base.en" => "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002",
        "small.en" => "c6138d6d58ecc8322097e0f987c32f1be8bb0a18532a3f88f734d1bbf9c41e5d",
        "tiny.en-q5_1" => "c77c5766f1cef09b6b7d47f21b546cbddd4157886b3b5d6d4f709e91e66c7c2b",
        "base.en-q5_1" => "4baf70dd0d7c4247ba2b81fafd9c01005ac77c2f9ef064e00dcf195d0e2fdd2f",
        "small.en-q5_1" => "bfdff4894dcb76bbf647d56263ea2a96645423f1669176f4844a1bf8e478ad30",
        _ => throw new ArgumentException("Unknown speech model.", nameof(model))
    };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<string> verified = [];

    public async Task<string> EnsureAsync(string model, Action<string> status, CancellationToken ct)
    {
        var expected = HashFor(model);
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(AppPaths.Models);
            var path = Path.Combine(AppPaths.Models, $"ggml-{model}.bin");
            if (verified.Contains(path) && File.Exists(path)) return path;
            status($"Verifying {model} model…");
            if (File.Exists(path) && await VerifyAsync(path, expected, ct)) { verified.Add(path); return path; }
            var temporary = path + ".download";
            try
            {
                using var response = await http.GetAsync($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{model}.bin", HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using (var source = await response.Content.ReadAsStreamAsync(ct))
                await using (var target = File.Create(temporary))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int lastPercent = -1;
                    int count;
                    while ((count = await source.ReadAsync(buffer, ct)) != 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, count), ct);
                        total += count;
                        var percent = response.Content.Headers.ContentLength is > 0 ? (int)(total * 100 / response.Content.Headers.ContentLength.Value) : -1;
                        if (percent != lastPercent || lastPercent == -1) { status($"Downloading {model}: {total / 1048576} MB ({percent}%)"); lastPercent = percent; }
                    }
                }
                if (!await VerifyAsync(temporary, expected, ct)) throw new InvalidDataException("Whisper model SHA-256 mismatch. Download was not installed; try again.");
                File.Move(temporary, path, true);
                verified.Add(path);
                return path;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

    public static async Task<bool> VerifyAsync(string path, string expected, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
