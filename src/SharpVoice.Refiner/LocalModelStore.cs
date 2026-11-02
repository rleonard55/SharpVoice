using System.Collections.Concurrent;
using System.Security.Cryptography;
using SharpVoice.Core;

namespace SharpVoice.Refiner;

/// <summary>Streams pinned weights to a unique temporary file, verifies, then installs atomically.</summary>
public sealed class LocalModelStore(HttpClient http)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (long Length, DateTime Stamp, string Hash)> Verified = new(StringComparer.OrdinalIgnoreCase);
    public bool IsPresent(string id)
    {
        var model = LocalAiModels.Find(id);
        if (model is null) return false;
        var file = new FileInfo(Path.Combine(AppPaths.Models, model.FileName));
        return file.Exists && file.Length == model.SizeBytes;
    }
    public Task<string> EnsureAsync(string id, Action<LocalModelProgress> progress, CancellationToken ct, bool allowDownload = true)
    {
        var model = LocalAiModels.Resolve(id);
        return EnsureAtPathAsync(model, Path.Combine(AppPaths.Models, model.FileName), progress, ct, allowDownload);
    }
    internal async Task<string> EnsureAtPathAsync(LocalAiModel model, string path, Action<LocalModelProgress> progress, CancellationToken ct, bool allowDownload = true)
    {
        path = Path.GetFullPath(path);
        var gate = Gates.GetOrAdd(path, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            progress(new(model.Id, LocalModelStage.Checking, 0, model.SizeBytes));
            if (await VerifyAsync(path, model, progress, ct))
            {
                progress(new(model.Id, LocalModelStage.Ready, model.SizeBytes, model.SizeBytes));
                return path;
            }
            if (!allowDownload) throw new InvalidOperationException("The local AI model is missing or invalid. Choose Download Model in AI & models to prepare it, then retry.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".download-" + Guid.NewGuid().ToString("N");
            try
            {
                using var response = await http.GetAsync(model.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long size && size != model.SizeBytes)
                    throw new InvalidDataException("The model download has an unexpected size. Retry the download.");
                await using (var source = await response.Content.ReadAsStreamAsync(ct))
                await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    var buffer = new byte[81920]; long total = 0; var last = -1;
                    progress(new(model.Id, LocalModelStage.Downloading, 0, model.SizeBytes));
                    int count;
                    while ((count = await source.ReadAsync(buffer, ct)) != 0)
                    {
                        if (count > model.SizeBytes - total) throw new InvalidDataException("The model download exceeded its expected size.");
                        await target.WriteAsync(buffer.AsMemory(0, count), ct);
                        total += count;
                        var update = new LocalModelProgress(model.Id, LocalModelStage.Downloading, total, model.SizeBytes);
                        if (update.Percent != last) { last = update.Percent; progress(update); }
                    }
                    if (total != model.SizeBytes) throw new InvalidDataException("The model download was incomplete. Retry the download.");
                }
                if (!await VerifyAsync(temporary, model, progress, ct)) throw new InvalidDataException("Local model verification failed (SHA-256). Retry the download.");
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
                Remember(path, model);
                progress(new(model.Id, LocalModelStage.Ready, model.SizeBytes, model.SizeBytes));
                return path;
            }
            finally
            {
                Verified.TryRemove(temporary, out _);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally { gate.Release(); }
    }
    private static void Remember(string path, LocalAiModel model)
    {
        var file = new FileInfo(path);
        Verified[path] = (file.Length, file.LastWriteTimeUtc, model.Sha256);
    }
    private static async Task<bool> VerifyAsync(string path, LocalAiModel model, Action<LocalModelProgress> progress, CancellationToken ct)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != model.SizeBytes) { Verified.TryRemove(path, out _); return false; }
        if (Verified.TryGetValue(path, out var known) && known.Length == file.Length && known.Stamp == file.LastWriteTimeUtc && known.Hash == model.Sha256) return true;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[81920]; long total = 0; var last = -1; int count;
        progress(new(model.Id, LocalModelStage.Verifying, 0, model.SizeBytes));
        while ((count = await source.ReadAsync(buffer, ct)) != 0)
        {
            hash.AppendData(buffer, 0, count); total += count;
            var update = new LocalModelProgress(model.Id, LocalModelStage.Verifying, total, model.SizeBytes);
            if (update.Percent != last) { last = update.Percent; progress(update); }
        }
        ct.ThrowIfCancellationRequested();
        var valid = total == model.SizeBytes && Convert.ToHexString(hash.GetHashAndReset()).Equals(model.Sha256, StringComparison.OrdinalIgnoreCase);
        if (valid) Remember(path, model); else Verified.TryRemove(path, out _);
        return valid;
    }
}
