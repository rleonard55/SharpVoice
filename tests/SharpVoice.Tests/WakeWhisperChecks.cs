using SharpVoice.Core;
using SharpVoice.Speech;

internal static class WakeWhisperChecks
{
    public static async Task NativeClips(string vad, string model, string directory)
    {
        using var speech = new WhisperEngine();
        var config = new AppConfig { AskWakePhrase = "Hey Chat", DictationWakePhrase = "Hey Chat Dictate" };
        await speech.PreloadAsync(model, default);
        var failures = new List<string>();
        foreach (var file in Directory.GetFiles(directory, "attempt-???.wav").Order())
        {
            var number = int.Parse(Path.GetFileNameWithoutExtension(file)[8..]);
            var hits = new System.Collections.Concurrent.ConcurrentQueue<WakeDetection>();
            await using var detector = new WakeWhisperDetector(vad, config, async (audio, ct) =>
            {
                var text = await speech.TranscribeAsync(model, audio, "en", _ => { }, ct, WakePhrases.VocabularyPrompt(config));
                Console.WriteLine($"CHECK clip={number} seconds={audio.Length / 16000d:F1} prefix={WakePhrases.TryDetect(config, text, out var mode)} mode={mode}");
                return text;
            }, diagnostics: true);
            detector.Detected += h => hits.Enqueue(h);
            var bytes = File.ReadAllBytes(file); var size = BitConverter.ToInt32(bytes, 40);
            var samples = new float[size / 2 + 16000 * 3];
            for (var i = 0; i < size / 2; i++) samples[i] = BitConverter.ToInt16(bytes, 44 + i * 2) / 32768f;
            for (var i = 0; i < samples.Length; i += 1600)
            {
                if (!detector.Enqueue(samples.AsSpan(i, Math.Min(1600, samples.Length - i)), i, 1)) throw new Exception("Replay overflowed.");
                await Task.Delay(100);
                if (number < 100 && !hits.IsEmpty) break;
            }
            await Task.Delay(1000);
            var expected = number <= 4 ? VoiceMode.AskAi : VoiceMode.Dictation;
            var passed = number >= 100 ? hits.IsEmpty : hits.Any(h => h.Mode == expected);
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} whisper wake clip={number} expected={(number >= 100 ? "none" : expected)} hits={string.Join(',', hits.Select(h => h.Mode))} checks={detector.Diagnostics?.Checks}");
            if (!passed) failures.Add(number.ToString());
        }
        if (failures.Count > 0) throw new Exception("Whisper wake replay failed clips: " + string.Join(',', failures));
    }
    public static Task Prefix()
    {
        var config = new AppConfig { AskWakePhrase = "Hey Chat", DictationWakePhrase = "Hey Chat Dictate" };
        if (!WakePhrases.TryDetect(config, "Hey, chat! Dictate: bring the folder.", out var mode) || mode != VoiceMode.Dictation)
            throw new Exception("Longer punctuation-separated wake phrase lost precedence.");
        if (!WakePhrases.TryDetect(config, "Hey chat, what is two plus two?", out mode) || mode != VoiceMode.AskAi)
            throw new Exception("Ask wake phrase was not detected.");
        foreach (var text in new[] { "Say hey chat", "Hey chatter", "[BLANK_AUDIO]", "What is two plus two?" })
            if (WakePhrases.TryDetect(config, text, out _)) throw new Exception("Non-prefix or non-speech text activated wake listening.");
        return Task.CompletedTask;
    }
    public static Task InitialMode()
    {
        var config = new AppConfig { AskWakePhrase = "Hey Chat", DictationWakePhrase = "Hey Chat Dictate" };
        var session = new WakeSession(new(VoiceMode.Dictation, 0, 1), config);
        if (session.Resolve("Hey Chat, bring the folder").Mode != VoiceMode.Dictation)
            throw new Exception("Final shortened transcription downgraded a detected Dictation to Ask AI.");
        return Task.CompletedTask;
    }
    public static async Task PrefixWindows()
    {
        var windows = new List<int>();
        await using var detector = Create((audio, _) => { lock (windows) windows.Add(audio.Length); return Task.FromResult("Unrelated speech"); });
        Feed(detector, 0, 64000, true);
        await Until(() => { lock (windows) return windows.Count >= 3; });
        lock (windows) if (!windows.Take(3).SequenceEqual(new[] { 16000, 24000, 32000 }))
            throw new Exception("Whisper skipped early prefix windows when microphone audio advanced during inference.");
    }
    public static async Task LongPause()
    {
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = 0;
        await using var detector = new WakeWhisperDetector(new AppConfig { WakeSilenceSeconds = 5 }, (_, ct) =>
        { ct.Register(() => Interlocked.Exchange(ref canceled, 1)); began.TrySetResult(); return result.Task; }, new Activity(), true, TimeSpan.FromMilliseconds(10));
        try
        {
            Feed(detector, 0, 24000, true); await began.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Feed(detector, 24000, 32000, false); await Until(() => detector.Activity?.Sample == 56000);
            Feed(detector, 56000, 1600, true); await Until(() => detector.Activity?.Sample == 57600);
            if (canceled != 0) throw new Exception("A pause shorter than the selected silence duration canceled wake recognition.");
        }
        finally { result.TrySetResult("Unrelated speech"); }
    }
    public static async Task Silence()
    {
        var calls = 0;
        await using var detector = Create((_, _) => { Interlocked.Increment(ref calls); return Task.FromResult("Hey Chat"); });
        Feed(detector, 0, 16000 * 3, false); await Until(() => detector.Diagnostics?.Processed == 30);
        await Task.Delay(80);
        if (calls != 0) throw new Exception("Silence invoked Whisper.");
        var hits = 0; detector.Detected += _ => Interlocked.Increment(ref hits);
        Feed(detector, 48000, 40000, true); await Until(() => hits == 1);
        await Task.Delay(80);
        if (hits != 1) throw new Exception("One utterance activated repeatedly.");
    }
    public static async Task SlowInference()
    {
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var detector = Create(async (_, ct) => { began.TrySetResult(); try { await Task.Delay(Timeout.Infinite, ct); } finally { canceled.TrySetResult(); } return "Hey Chat"; });
        Feed(detector, 0, 24000, true); await began.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Feed(detector, 24000, 32000, false); await Until(() => detector.Activity?.Sample == 56000);
        if (detector.Activity!.Silence.TotalSeconds < 1.9) throw new Exception("Whisper blocked independent recording-end activity.");
        detector.SetRecognitionEnabled(false); await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
    public static async Task Stale()
    {
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var detector = Create((_, _) => { began.TrySetResult(); return result.Task; });
        var hits = 0; detector.Detected += _ => Interlocked.Increment(ref hits);
        Feed(detector, 0, 24000, true); await began.Task.WaitAsync(TimeSpan.FromSeconds(3));
        detector.Reset(2); result.SetResult("Hey Chat, dictate hello");
        await Task.Delay(100);
        if (hits != 0) throw new Exception("Old-epoch Whisper result activated the new capture.");
        detector.SetRecognitionEnabled(false); Feed(detector, 0, 24000, true, 2);
        await Until(() => detector.Activity?.Epoch == 2);
        if (hits != 0) throw new Exception("Suspended recognition activated while tracking speech.");
    }
    public static async Task SharedPrefix()
    {
        var checkedShort = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var detector = Create((audio, _) =>
        {
            checkedShort.TrySetResult();
            return Task.FromResult(audio.Length < 32000 ? "Hey Chat" : "Hey Chat Dictate");
        });
        var hits = new System.Collections.Concurrent.ConcurrentQueue<WakeDetection>();
        detector.Detected += h => hits.Enqueue(h);
        Feed(detector, 0, 24000, true); await checkedShort.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(40);
        if (!hits.IsEmpty) throw new Exception("Short prefix activated before the longer phrase could be heard.");
        Feed(detector, 24000, 16000, true); await Until(() => !hits.IsEmpty);
        if (!hits.TryPeek(out var hit) || hit.Mode != VoiceMode.Dictation) throw new Exception("Longer Whisper phrase was not selected.");
    }
    private static WakeWhisperDetector Create(Func<ReadOnlyMemory<float>, CancellationToken, Task<string>> infer)
        => new(new AppConfig { AskWakePhrase = "Hey Chat", DictationWakePhrase = "Hey Chat Dictate" }, infer, new Activity(), true, TimeSpan.FromMilliseconds(10));
    private static void Feed(WakeWhisperDetector detector, long first, int count, bool speech, long epoch = 1)
    {
        var samples = new float[1600]; if (speech) Array.Fill(samples, .2f);
        for (var i = 0; i < count; i += samples.Length)
            if (!detector.Enqueue(samples.AsSpan(0, Math.Min(samples.Length, count - i)), first + i, epoch)) throw new Exception("Diagnostic fixture overflowed.");
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
    private sealed class Activity : IWakeSpeechDetector
    {
        private long last;
        public void Reset(long first) => last = first;
        public WakeSpeechActivity Process(ReadOnlySpan<float> samples, long first, long epoch)
        {
            foreach (var sample in samples) if (sample > .1f) { last = first + samples.Length; break; }
            return new(epoch, first + samples.Length, last);
        }
        public void Dispose() { }
    }
}
