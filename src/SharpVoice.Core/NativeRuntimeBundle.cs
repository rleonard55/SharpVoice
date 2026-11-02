using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SharpVoice.Core;

/// <summary>One-file distribution; Windows loads verified native DLLs from a per-user versioned cache.</summary>
public static class NativeRuntimeBundle
{
    private static readonly object gate = new();
    private static string? extracted;
    public static string? Ensure()
    {
        if (!OperatingSystem.IsWindows()) return null;
        lock (gate)
        {
            if (extracted is not null) return extracted;
            using var source = Assembly.GetEntryAssembly()?.GetManifestResourceStream("SharpVoice.NativeRuntime.zip");
            if (source is null) return null; // Library consumers can supply the standard NuGet runtime layout.
            var hash = Convert.ToHexString(SHA256.HashData(source));
            source.Position = 0;
            var root = Path.Combine(AppPaths.Root, "native", hash);
            Directory.CreateDirectory(root);
            using var archive = new ZipArchive(source, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var path = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid bundled native library path.");
                using var data = entry.Open();
                var expected = SHA256.HashData(data);
                if (File.Exists(path))
                {
                    using var existing = File.OpenRead(path);
                    if (SHA256.HashData(existing).AsSpan().SequenceEqual(expected)) continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var input = entry.Open())
                    using (var output = File.Create(temporary)) input.CopyTo(output);
                    File.Move(temporary, path, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            // Preload the designated app-local Microsoft redistributables before inference.
            // Explicit paths avoid relying on Visual Studio or a machine-wide VC install.
            foreach (var name in new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "vcomp140.dll" })
            {
                var dependency = Path.Combine(root, "cpp", name);
                if (File.Exists(dependency)) _ = NativeLibrary.Load(dependency); // Keep loaded for process lifetime.
            }
            return extracted = root;
        }
    }
}
