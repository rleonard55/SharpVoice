using System.IO.Compression;
using System.Security.Cryptography;

namespace SharpVoice.Platform.Windows;

/// <summary>Verified app-private speech runtime; no user Python, PATH or package installation.</summary>
internal static class EdgeSpeechBundle
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    internal static string Ensure(string root, CancellationToken ct)
    {
        using var source = typeof(EdgeSpeechBundle).Assembly.GetManifestResourceStream("SharpVoice.EdgeSpeech.zip")
            ?? throw new InvalidOperationException("The bundled Edge speech runtime is missing.");
        return Prepare(source, root, ct);
    }
    internal static string Prepare(Stream source, string root, CancellationToken ct)
    {
        gate.Wait(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var hash = Convert.ToHexString(SHA256.HashData(source)); source.Position = 0;
            var directory = Path.GetFullPath(Path.Combine(root, hash));
            var prefix = directory + Path.DirectorySeparatorChar;
            using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > 1000) throw new InvalidDataException("Speech bundle contains too many files.");
            long total = 0;
            var entries = new List<(ZipArchiveEntry Entry, string Path)>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.Name.Length == 0) continue;
                if (entry.Length > 32_000_000 || (total += entry.Length) > 128_000_000 || entry.FullName.Contains(':') || entry.FullName.Contains('\\'))
                    throw new InvalidDataException("Invalid speech bundle entry.");
                var path = Path.GetFullPath(Path.Combine(directory, entry.FullName));
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !paths.Add(path))
                    throw new InvalidDataException("Invalid speech bundle path.");
                entries.Add((entry, path));
            }
            var executable = Path.Combine(directory, "python.exe");
            if (!paths.Contains(executable)) throw new InvalidDataException("Speech bundle contains no interpreter.");
            Directory.CreateDirectory(root); RejectLink(root);
            Directory.CreateDirectory(directory); RejectLink(directory);
            foreach (var (entry, path) in entries)
            {
                ct.ThrowIfCancellationRequested();
                var parent = Path.GetDirectoryName(path)!;
                // Reject cache redirections before creating or opening any bundle file.
                var relative = Path.GetRelativePath(directory, parent);
                var current = directory;
                foreach (var part in relative.Split(Path.DirectorySeparatorChar))
                {
                    if (part == ".") continue;
                    current = Path.Combine(current, part); RejectLink(current); Directory.CreateDirectory(current);
                }
                RejectLink(path);
                byte[] expected;
                using (var data = entry.Open()) expected = SHA256.HashData(data);
                if (File.Exists(path))
                {
                    using var existing = File.OpenRead(path);
                    if (SHA256.HashData(existing).AsSpan().SequenceEqual(expected)) continue;
                }
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var input = entry.Open())
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                    ct.ThrowIfCancellationRequested();
                    File.Move(temporary, path, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            ct.ThrowIfCancellationRequested(); return executable;
        }
        finally { gate.Release(); }
    }
    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Speech cache cannot contain redirected files.");
    }
}
