using SharpVoice.Audio;
using SharpVoice.Core;
using SharpVoice.Platform.Windows;

namespace SharpVoice.App;

/// <summary>Host boundaries kept outside voice coordination, allowing isolated capture and insertion hosts.</summary>
internal sealed class VoiceEnvironment
{
    public Func<AppConfig, WasapiAudioRecorder>? CreateRecorder { get; init; }
    public Func<WindowTarget> CaptureTarget { get; init; } = WindowTarget.Capture;
    public Func<WindowTarget, CancellationToken, Task<SelectionSnapshot?>> CaptureSelection { get; init; } = SelectionSnapshot.CaptureAsync;
    public Func<string, WindowTarget, string, int, CancellationToken, SelectionSnapshot?, Task> Insert { get; init; } = TextInjector.InsertAsync;
    public string? HistoryPath { get; init; }
    public string? ModelDirectory { get; init; }
    public string? WakeModelRoot { get; init; }
    public bool EditorIntegration { get; init; } = true;
}
