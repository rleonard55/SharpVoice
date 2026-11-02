namespace SharpVoice.Platform.Windows;

public sealed class AnswerSpeech(Func<string, string, Action<string>, CancellationToken, Task>? edge = null, Func<string, CancellationToken, Task>? windows = null)
{
    private readonly Func<string, string, Action<string>, CancellationToken, Task> edgeEngine = edge ?? EdgeAnswerSpeech.SpeakAsync;
    private readonly Func<string, CancellationToken, Task> windowsEngine = windows ?? WindowsAnswerSpeech.SpeakAsync;
    private readonly SemaphoreSlim playback = new(1, 1);
    public async Task SpeakAsync(string text, string voice, Action<string> status, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await playback.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var shortened = text.Length > 12_000;
            if (shortened) text = text[..(char.IsHighSurrogate(text[11_999]) ? 11_999 : 12_000)];
            var suffix = shortened ? " · First 12,000 characters" : "";
            var engine = "Edge Voice";
            try
            {
                status("Preparing Edge voice…" + suffix);
                await edgeEngine(text, voice, s => status(s + suffix), ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                engine = "Windows Voice (Edge Unavailable)";
                status("Reading with Windows voice · Edge unavailable" + suffix);
                await windowsEngine(text, ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            status("Finished Reading · " + engine + suffix);
        }
        finally { playback.Release(); }
    }
}
