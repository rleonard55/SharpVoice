using System.Formats.Tar;
using System.Security.Cryptography;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;
using SharpVoice.Core;

namespace SharpVoice.Speech;

public sealed class WakeModelManager(HttpClient http, string? root = null)
{
    private const string Model = "sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01";
    private const string ArchiveHash = "f170013b4716e41b62b9bfd809687c207cef798ef9bc6534d524e17af9b6561a";
    private const int ArchiveSize = 17626723;
    private readonly string directory = root ?? Path.Combine(AppPaths.Models, "wake");
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<string> EnsureSpeechAsync(Action<WakePreparation> progress, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var target = Path.Combine(directory, "speech"); Directory.CreateDirectory(target);
            var legacy = Path.Combine(directory, Model, "silero_vad.onnx");
            var file = Path.Combine(target, "silero_vad.onnx");
            const string hash = "9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6";
            if (!File.Exists(file) && File.Exists(legacy) && await ModelManager.VerifyAsync(legacy, hash, ct)) File.Copy(legacy, file, true);
            await EnsureVoiceActivity(target, progress, ct).ConfigureAwait(false);
            progress(new("ready", 0, null, "Local Speech Detector Ready")); return target;
        }
        finally { gate.Release(); }
    }
    internal static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>
    {
        ["bpe.model"] = "c8a2a0129c4ab8e463164c142f82d25649661b122c8cd0b7aab5c9e80b90ad24",
        ["tokens.txt"] = "fd2ded4050a55d2b1578870ba8697d02371980217806b7558bd0a5cc60f3ba53",
        ["encoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx"] = "1e721676515bcd42a186979733981213c66c80db680e1cc582dfedf3be76e678",
        ["decoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx"] = "e40ff43297abe815e8898494c17e71bba2152d9d40fa3eb803f75d0f7533329a",
        ["joiner-epoch-12-avg-2-chunk-16-left-64.int8.onnx"] = "eae9da0c7e1e6c6a3f4cc42d167899c388f6c6701b94cb96320e4f55df79624c"
    };
    public async Task<string> EnsureAsync(Action<WakePreparation> progress, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        var target = Path.Combine(directory, Model); var temporary = target + "." + Guid.NewGuid().ToString("N"); var archive = temporary + ".download";
        try
        {
            Directory.CreateDirectory(directory);
            progress(new("verifying", 0, null, "Checking local wake model…"));
            if (await Valid(target, ct)) { await EnsureVoiceActivity(target, progress, ct); progress(new("ready", 0, null, "Wake model ready")); return target; }
            using var response = await http.GetAsync("https://github.com/k2-fsa/sherpa-onnx/releases/download/kws-models/" + Model + ".tar.bz2", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != ArchiveSize) throw new InvalidDataException("Wake model download has an unexpected size. Retry to download it again.");
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(archive))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > ArchiveSize) throw new InvalidDataException("Wake model download exceeded its expected size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    progress(new("downloading", total, ArchiveSize, "Downloading local wake model…"));
                }
                if (total != ArchiveSize) throw new InvalidDataException("Wake model download was incomplete. Retry to download it again.");
            }
            if (!await ModelManager.VerifyAsync(archive, ArchiveHash, ct)) throw new InvalidDataException("Wake model verification failed. Nothing was installed.");
            progress(new("verifying", ArchiveSize, ArchiveSize, "Preparing local wake model…"));
            Directory.CreateDirectory(temporary);
            using (var compressed = File.OpenRead(archive))
            using (var decompressed = BZip2Stream.Create(compressed, CompressionMode.Decompress, false))
            using (var tar = new TarReader(decompressed))
            {
                TarEntry? entry; long expanded = 0;
                while ((entry = await tar.GetNextEntryAsync(false, ct)) is not null)
                {
                    expanded += entry.Length;
                    if (expanded > 50 * 1024 * 1024 || entry.Name.Contains("..", StringComparison.Ordinal) || entry.Name.StartsWith('/') || entry.Name.Contains('\\')) throw new InvalidDataException("Unexpected wake archive contents.");
                    if (entry.EntryType == TarEntryType.Directory) continue;
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new InvalidDataException("Unsupported wake archive entry.");
                    var name = entry.Name.StartsWith(Model + "/", StringComparison.Ordinal) ? entry.Name[(Model.Length + 1)..] : "";
                    if (!Files.ContainsKey(name)) continue;
                    if (entry.Length > 8 * 1024 * 1024 || File.Exists(Path.Combine(temporary, name))) throw new InvalidDataException("Unexpected wake model entry size or duplicate.");
                    await using var output = File.Create(Path.Combine(temporary, name));
                    await entry.DataStream!.CopyToAsync(output, ct);
                }
            }
            if (!await Valid(temporary, ct)) throw new InvalidDataException("Wake model files failed verification.");
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(temporary, target);
            await EnsureVoiceActivity(target, progress, ct);
            progress(new("ready", ArchiveSize, ArchiveSize, "Wake model ready")); return target;
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
            gate.Release();
        }
    }
    private static async Task<bool> Valid(string directory, CancellationToken ct)
    {
        foreach (var file in Files)
        {
            var path = Path.Combine(directory, file.Key);
            if (!File.Exists(path) || !await ModelManager.VerifyAsync(path, file.Value, ct)) return false;
        }
        return true;
    }
    private async Task EnsureVoiceActivity(string target, Action<WakePreparation> progress, CancellationToken ct)
    {
        const string hash = "9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6";
        const int size = 643854;
        var file = Path.Combine(target, "silero_vad.onnx");
        if (File.Exists(file) && await ModelManager.VerifyAsync(file, hash, ct)) return;
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var response = await http.GetAsync("https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != size) throw new InvalidDataException("Speech detector download has an unexpected size. Retry Wake Phrases preparation.");
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[81920]; var total = 0; int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > size) throw new InvalidDataException("Speech detector download exceeded its expected size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    progress(new("downloading", total, size, "Downloading local speech-end detection…"));
                }
                if (total != size) throw new InvalidDataException("Speech detector download was incomplete.");
            }
            if (!await ModelManager.VerifyAsync(temporary, hash, ct)) throw new InvalidDataException("Speech detector verification failed. Nothing was installed.");
            ct.ThrowIfCancellationRequested(); File.Move(temporary, file, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
