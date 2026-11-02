using System.Text;
using Whisper.net;
using Whisper.net.LibraryLoader;
using SharpVoice.Core;

namespace SharpVoice.Speech;

public sealed class WhisperEngine : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private WhisperFactory? factory;
    private string? loadedPath;
    private DateTime lastUse = DateTime.UtcNow;
    private readonly Timer sleepTimer;

    public WhisperEngine() => sleepTimer = new Timer(_ => DeepSleep(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    // Called from a worker on key-down so model loading overlaps the user's speech.
    public async Task PreloadAsync(string path, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { Load(path); }
        finally { lastUse = DateTime.UtcNow; gate.Release(); }
    }

    private void Load(string path)
    {
        if (factory is not null && loadedPath == path) return;
        factory?.Dispose();
        factory = null;
        loadedPath = null;
        var native = NativeRuntimeBundle.Ensure();
        if (native is not null) RuntimeOptions.LibraryPath = Path.Combine(native, "whisper", "SharpVoice.exe");
        factory = WhisperFactory.FromPath(path);
        loadedPath = path;
    }

    public async Task<string> TranscribeAsync(string path, ReadOnlyMemory<float> samples, string language, Action<string> preview, CancellationToken ct, string vocabulary = "", bool fast = false)
    {
        await gate.WaitAsync(ct);
        try
        {
            Load(path);
            var builder = factory!.CreateBuilder().WithLanguage(language);
            var context = WhisperSpeedPlan.AudioContext(samples.Length, fast);
            if (context != 0) builder.WithAudioContextSize(context).WithThreads(WhisperSpeedPlan.Threads);
            if (vocabulary.Length != 0) builder.WithPrompt(vocabulary);
            await using var processor = builder.Build();
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, ct))
            {
                text.Append(segment.Text);
                var recognized = TranscriptionText.Clean(text.ToString());
                if (recognized.Length > 0) preview(recognized);
            }
            return TranscriptionText.Clean(text.ToString());
        }
        finally { lastUse = DateTime.UtcNow; gate.Release(); }
    }

    private void DeepSleep()
    {
        if (!gate.Wait(0)) return;
        try
        {
            if (DateTime.UtcNow - lastUse < TimeSpan.FromMinutes(10)) return;
            factory?.Dispose(); factory = null; loadedPath = null;
        }
        finally { gate.Release(); }
    }

    public void Dispose()
    {
        sleepTimer.Dispose();
        gate.Wait();
        try { factory?.Dispose(); factory = null; }
        finally { gate.Release(); }
    }
}
