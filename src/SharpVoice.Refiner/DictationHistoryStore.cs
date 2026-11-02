using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpVoice.Core;

namespace SharpVoice.Refiner;

/// <summary>Opt-in, atomic text-history storage encrypted for the current Windows user.</summary>
public sealed class DictationHistoryStore(string? path = null)
{
    private readonly string file = path ?? Path.Combine(AppPaths.Root, "history.bin");
    public DictationEntry[] Load()
    {
        if (!File.Exists(file)) return [];
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (new FileInfo(file).Length > 64 * 1024 * 1024) throw new InvalidDataException("Saved dictation history is too large.");
        var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser);
        try
        {
            var saved = JsonSerializer.Deserialize(plaintext, HistoryJsonContext.Default.DictationEntryArray) ?? [];
            var check = new DictationHistory(); check.Restore(saved); return check.Snapshot();
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    public void Save(DictationEntry[] entries)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var check = new DictationHistory(); check.Restore(entries);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(check.Snapshot(), HistoryJsonContext.Default.DictationEntryArray);
        var temporary = file + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            if (plaintext.Length > 63 * 1024 * 1024) throw new InvalidDataException("Saved history is too large. Clear older entries before saving.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            File.WriteAllBytes(temporary, ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser));
            File.Move(temporary, file, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Delete() { if (File.Exists(file)) File.Delete(file); }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(DictationEntry[]))]
internal partial class HistoryJsonContext : JsonSerializerContext;
