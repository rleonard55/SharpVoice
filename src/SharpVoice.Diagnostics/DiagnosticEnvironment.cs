using System.Diagnostics;
using SharpVoice.Audio;
using SharpVoice.Speech;

namespace SharpVoice.Diagnostics;

internal sealed class DiagnosticEnvironment
{
    public Func<bool> MainAppRunning { get; init; } = () =>
    {
        var processes = Process.GetProcessesByName("SharpVoice");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    };
    public Func<DiagnosticSettings, string, WasapiAudioRecorder> CreateRecorder { get; init; }
        = (s, id) => new(s.NoiseGateDb, 30, id, s.KeepReady, diagnostics: true);
    public Func<HttpClient, Action<SharpVoice.Core.WakePreparation>, CancellationToken, Task<string>> PrepareModels { get; init; }
        = (http, progress, ct) => new WakeModelManager(http).EnsureSpeechAsync(progress, ct);
    public Func<HttpClient, string, CancellationToken, Task<string>> PrepareSpeechModel { get; init; }
        = (http, model, ct) => new ModelManager(http).EnsureAsync(model, _ => { }, ct);
}
