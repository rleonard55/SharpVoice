using System.Diagnostics;
using SharpVoice.Audio;

namespace SharpVoice.Speech;

/// <summary>One owned local draft at a time. Disposal drains inference before releasing its snapshot.</summary>
public sealed class LiveTranscription : IAsyncDisposable
{
    private readonly CancellationTokenSource stop;
    private readonly Task work;
    private int disposed;
    public LiveTranscription(Func<AudioRecording?> snapshot, Func<ReadOnlyMemory<float>, CancellationToken, Task<string>> transcribe,
        Action<string, int> preview, CancellationToken ct, TimeSpan? cadence = null, Action? unavailable = null)
    {
        stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = cadence ?? TimeSpan.FromMilliseconds(900);
        if (delay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(cadence));
        var token = stop.Token;
        work = Task.Run(async () =>
        {
            var sequence = 0; var nextDelay = delay;
            try
            {
                while (true)
                {
                    await Task.Delay(nextDelay, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    using var audio = snapshot();
                    if (audio is null || !audio.HasSpeech || audio.Samples.Length < 8000) continue;
                    var clock = Stopwatch.StartNew();
                    var text = await transcribe(audio.Samples, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(text)) preview(text, ++sequence);
                    nextDelay = clock.Elapsed > TimeSpan.FromSeconds(3) ? TimeSpan.FromSeconds(3) : delay;
                    if (clock.Elapsed > TimeSpan.FromSeconds(8)) { unavailable?.Invoke(); return; }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { if (!token.IsCancellationRequested) unavailable?.Invoke(); }
        });
    }
    public async ValueTask DisposeAsync()
    {
        lock (stop) { if (disposed == 0) { disposed = 1; stop.Cancel(); } }
        await work.ConfigureAwait(false);
        lock (stop) { if (disposed == 1) { disposed = 2; stop.Dispose(); } }
    }
}
