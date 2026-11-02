using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpVoice.Audio;
using SharpVoice.Speech;

namespace SharpVoice.Diagnostics;

public sealed record DiagnosticSettings(string AskPhrase, string DictationPhrase, float NoiseGateDb, int SilenceSeconds, string DeviceSelection, bool ClipsEnabled, bool KeepReady = false, string SpeechModel = "tiny.en-q5_1");
public sealed record DiagnosticEntry(long Sequence, DateTimeOffset Utc, long ElapsedMs, string Code, string Detail, CaptureDiagnostics? Capture = null, WakeDetectorDiagnostics? Detector = null);
public sealed record DiagnosticSummary(int Schema, string Application, string Runtime, string OperatingSystem, int LogicalProcessors,
    DiagnosticSettings Settings, long EventsWritten, long EventsDropped, int AudioClips, string DetectorProfile);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(DiagnosticEntry))]
[JsonSerializable(typeof(DiagnosticSummary))]
internal partial class ReportJsonContext : JsonSerializerContext;

public sealed class DiagnosticJournal : IDisposable
{
    private readonly object gate = new();
    private readonly StreamWriter writer;
    private readonly DiagnosticSettings settings;
    private readonly List<string> clips = [];
    private readonly Queue<string> recent = new();
    private readonly int maxEvents;
    private readonly long started = Environment.TickCount64;
    private long written, dropped;
    private bool disposed;
    public string DirectoryPath { get; }
    public DiagnosticJournal(string root, DiagnosticSettings settings, int maxEvents = 10000)
    {
        if (maxEvents < 1) throw new ArgumentOutOfRangeException(nameof(maxEvents));
        this.settings = settings; this.maxEvents = maxEvents;
        DirectoryPath = Path.Combine(root, "session-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(DirectoryPath);
        writer = new(File.Open(Path.Combine(DirectoryPath, "events.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        SaveSummary();
    }
    public void Record(string code, string detail, CaptureDiagnostics? capture = null, WakeDetectorDiagnostics? detector = null)
    {
        lock (gate)
        {
            if (disposed) return;
            // Callers supply technical events only. Redact machine-specific paths
            // defensively; full configuration/exception objects are never accepted.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 0) detail = detail.Replace(home, "[user]", StringComparison.OrdinalIgnoreCase);
            detail = detail.Length > 500 ? detail[..500] : detail;
            var line = $"{(Environment.TickCount64 - started) / 1000d,6:F1}s  {code}: {detail}";
            recent.Enqueue(line); while (recent.Count > 70) recent.Dequeue();
            if (written >= maxEvents) { dropped++; return; }
            var entry = new DiagnosticEntry(++written, DateTimeOffset.UtcNow, Environment.TickCount64 - started, code, detail, capture, detector);
            writer.WriteLine(JsonSerializer.Serialize(entry, ReportJsonContext.Default.DiagnosticEntry));
            if (written % 10 == 0) SaveSummary();
        }
    }
    public string RecentText { get { lock (gate) return string.Join("\r\n", recent); } }
    public void AddClip(int attempt, ReadOnlySpan<float> samples)
    {
        lock (gate)
        {
            if (!settings.ClipsEnabled) throw new InvalidOperationException("Audio capture was not enabled for this session.");
            ObjectDisposedException.ThrowIf(disposed, this);
            if (clips.Count >= 20 || samples.IsEmpty) return;
            var name = $"attempt-{attempt:D3}.wav";
            if (clips.Contains(name)) return;
            using var output = new BinaryWriter(File.Create(Path.Combine(DirectoryPath, name)));
            var count = Math.Min(samples.Length, 20 * WasapiAudioRecorder.SampleRate);
            output.Write("RIFF"u8); output.Write(36 + count * 2); output.Write("WAVEfmt "u8); output.Write(16);
            output.Write((short)1); output.Write((short)1); output.Write(16000); output.Write(32000); output.Write((short)2); output.Write((short)16);
            output.Write("data"u8); output.Write(count * 2);
            for (var i = 0; i < count; i++) output.Write((short)Math.Clamp(samples[i] * 32767, short.MinValue, short.MaxValue));
            clips.Add(name); SaveSummary();
        }
    }
    private void SaveSummary()
    {
        var summary = new DiagnosticSummary(1, "SharpVoice Wake Diagnostics", RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription, Environment.ProcessorCount, settings, written, dropped, clips.Count,
            "Speech-triggered Whisper using selected speech_model; Silero speech activity threshold 0.35; exact wake-prefix matching; no keyword spotter; confidence unavailable");
        var file = Path.Combine(DirectoryPath, "summary.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(summary, ReportJsonContext.Default.DiagnosticSummary));
        File.Move(file + ".tmp", file, true);
    }
    public string Export()
    {
        lock (gate)
        {
            if (!disposed) writer.Flush();
            SaveSummary();
            File.WriteAllText(Path.Combine(DirectoryPath, "README.txt"), "SharpVoice wake diagnostics\r\nRead summary.json for the settings and event counters; events.jsonl is a technical timeline.\r\nNo account credentials, full configuration, transcripts or user paths are collected.\r\n" +
                (settings.ClipsEnabled ? "Audio was explicitly enabled; attempt WAV files contain microphone audio. Review them before sharing.\r\n" : "Audio capture was OFF. This report contains no recordings.\r\n") +
                "Only the standalone capture/detector/recording lifecycle is tested. No AI requests, text insertion, global hotkeys or readout are run.\r\n");
            var exportRoot = Path.Combine(Path.GetDirectoryName(DirectoryPath)!, "exports"); Directory.CreateDirectory(exportRoot);
            var target = Path.Combine(exportRoot, Path.GetFileName(DirectoryPath) + "-" + Guid.NewGuid().ToString("N")[..6] + ".zip");
            using var archive = ZipFile.Open(target, ZipArchiveMode.Create);
            foreach (var name in new[] { "README.txt", "summary.json", "events.jsonl" }.Concat(clips))
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                using var input = new FileStream(Path.Combine(DirectoryPath, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var output = entry.Open(); input.CopyTo(output);
            }
            return target;
        }
    }
    public void Dispose() { lock (gate) { if (disposed) return; try { SaveSummary(); } finally { writer.Dispose(); disposed = true; } } }
}
