using System.Diagnostics;
using SharpVoice.Core;
using SharpVoice.UI;
using SharpVoice.Platform.Windows;

internal static class WindowsCueLatencySmoke
{
    public static async Task Run()
    {
        var preparation = Stopwatch.StartNew();
        using var cues = new SoundCues();
        if (!await cues.Ready.WaitAsync(TimeSpan.FromSeconds(10))) throw new Exception("No prepared audio output.");
        Console.WriteLine($"Prepared/primed output ready: {preparation.Elapsed.TotalMilliseconds:F2} ms (silent startup work)");
        var sample = 0;
        foreach (var cue in new[] { FeedbackCue.RecordingStart, FeedbackCue.RecordingStop, FeedbackCue.Warning, FeedbackCue.InsertionSuccess, FeedbackCue.RecordingStart })
        {
            if (sample++ == 4) { await Task.Delay(2500); MemoryTrimmer.Trim(); }
            var done = new TaskCompletionSource<FeedbackCue>(TaskCreationOptions.RunContinuationsAsynchronously);
            cues.Completed = played => done.TrySetResult(played);
            var watch = Stopwatch.StartNew();
            cues.Play(cue);
            var actual = await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (actual != cue) throw new Exception("Cue playback order changed.");
            Console.WriteLine($"{cue}: request to playback completion {watch.Elapsed.TotalMilliseconds:F2} ms");
            // Includes 130/170 ms of actual PCM, plus a generous device/scheduling allowance.
            if (watch.Elapsed.TotalMilliseconds > 330) throw new Exception("Cue still pays noticeable per-play startup overhead.");
            await Task.Delay(150);
        }
        var burst = new[] { FeedbackCue.RecordingStart, FeedbackCue.RecordingStop, FeedbackCue.Warning, FeedbackCue.InsertionSuccess };
        var played = new List<FeedbackCue>();
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cues.Completed = cue => { played.Add(cue); if (played.Count == burst.Length) drained.TrySetResult(); };
        foreach (var cue in burst) cues.Play(cue);
        await drained.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (!played.SequenceEqual(burst)) throw new Exception("Rapid cues were reordered or dropped.");
        cues.Play(FeedbackCue.RecordingStart);
        // Disposal with a pending/playing cue must release buffers and terminate promptly.
        var shutdown = Stopwatch.StartNew();
        cues.Dispose();
        if (shutdown.Elapsed > TimeSpan.FromSeconds(3)) throw new Exception("Sound shutdown hung during playback.");
        Console.WriteLine("PASS prepared audio cues play promptly in order and stop safely.");
    }
}
