using System.Collections.Concurrent;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

/// <summary>Prepares output at startup and serializes cues on a dedicated audio thread.</summary>
internal sealed class SoundCues : IDisposable
{
    private readonly BlockingCollection<FeedbackCue> queue = new(8);
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread playback;
    private int disposed;
    internal Task<bool> Ready => ready.Task;
    internal Action<FeedbackCue>? Completed { get; set; }

    public SoundCues()
    {
        playback = new Thread(Playback) { IsBackground = true, Name = "SharpVoice cues" };
        playback.Start();
    }

    public void Play(FeedbackCue cue)
    {
        if (cue == FeedbackCue.None || Volatile.Read(ref disposed) != 0) return;
        try { queue.TryAdd(cue); }
        catch (InvalidOperationException) when (Volatile.Read(ref disposed) != 0) { }
    }

    private void Playback()
    {
        WaveOutCues? output = null;
        var outputBlocked = false;
        try
        {
            output = CueOutputRecovery.TryOpen(() => new WaveOutCues(lifetime.Token), ref outputBlocked);
            ready.TrySetResult(output is not null);
            foreach (var cue in queue.GetConsumingEnumerable(lifetime.Token))
            {
                lifetime.Token.ThrowIfCancellationRequested();
                try
                {
                    output ??= CueOutputRecovery.TryOpen(() => new WaveOutCues(lifetime.Token), ref outputBlocked);
                    if (output is null) { if (cue == FeedbackCue.Warning) Win32.MessageBeep(0x30); continue; }
                    if (output.Play(cue)) Completed?.Invoke(cue);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
                catch
                {
                    output?.Dispose();
                    outputBlocked = output?.ResourcesRetained == true;
                    output = null; // Retry the output device on the next cue.
                    if (cue == FeedbackCue.Warning) Win32.MessageBeep(0x30);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { output?.Dispose(); ready.TrySetResult(false); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        queue.CompleteAdding();
        playback.Join();
        queue.Dispose();
        lifetime.Dispose();
    }
}
