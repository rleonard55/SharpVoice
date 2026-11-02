using SharpVoice.Speech;

internal static class WakeDetectorChecks
{
    public static async Task Assets()
    {
        var root = Path.Combine(Path.GetTempPath(), "sharpvoice-wake-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var http = new HttpClient(new BadArchive());
            var manager = new WakeModelManager(http, root);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var ready = false;
                try { await manager.EnsureAsync(p => ready |= p.State == "ready", default); throw new Exception("Corrupt wake model accepted."); }
                catch (InvalidDataException) { }
                if (ready || Directory.GetFiles(root, "*.download", SearchOption.AllDirectories).Length != 0) throw new Exception("Failed wake download left ready/partial state.");
            }
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { await manager.EnsureAsync(_ => { }, cancel.Token); throw new Exception("Canceled wake download continued."); }
            catch (OperationCanceledException) { }
            using var wrongHash = new HttpClient(new BadArchive(17626723));
            try { await new WakeModelManager(wrongHash, root).EnsureAsync(_ => { }, default); throw new Exception("Correct-size corrupted wake model accepted."); }
            catch (InvalidDataException) { }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class BadArchive(int size = 3) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[size]) });
    }
    public static async Task Native(string model, string wav, string dictate, string ask, string expected = "any")
    {
        var bytes = File.ReadAllBytes(wav); var position = 12; byte[]? pcm = null; int rate = 0, channels = 0, bits = 0;
        while (position + 8 <= bytes.Length)
        {
            var name = System.Text.Encoding.ASCII.GetString(bytes, position, 4); var count = BitConverter.ToInt32(bytes, position + 4); position += 8;
            if (name == "fmt ") { channels = BitConverter.ToInt16(bytes, position + 2); rate = BitConverter.ToInt32(bytes, position + 4); bits = BitConverter.ToInt16(bytes, position + 14); }
            if (name == "data") pcm = bytes.AsSpan(position, count).ToArray();
            position += count + (count & 1);
        }
        if (rate != 16000 || channels != 1 || bits != 16 || pcm is null) throw new Exception("Fixture requires 16 kHz mono PCM16.");
        var process = System.Diagnostics.Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime; var watch = System.Diagnostics.Stopwatch.StartNew();
        await using var detector = new WakeKeywordDetector(model, dictate, ask);
        var hits = new System.Collections.Concurrent.ConcurrentQueue<SharpVoice.Core.WakeDetection>();
        detector.Detected += h => { hits.Enqueue(h); Console.WriteLine(h); };
        var audio = new float[pcm.Length / 2 + 16000];
        for (var i = 0; i < pcm.Length / 2; i++) audio[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
        for (var i = 0; i < audio.Length; i += 1600)
        {
            if (!detector.Enqueue(audio.AsSpan(i, Math.Min(1600, audio.Length - i)), i, 1)) throw new Exception("Fixture detector could not keep up with real-time input.");
            await Task.Delay(100);
        }
        await Task.Delay(500);
        if (expected == "none" ? !hits.IsEmpty : hits.IsEmpty) throw new Exception("Unexpected wake detection result: " + expected);
        if (expected == "dictation" && !hits.Any(x => x.Mode == SharpVoice.Core.VoiceMode.Dictation)) throw new Exception("Longer default phrase was not recognized as dictation.");
        if (expected == "ask" && (hits.Any(x => x.Mode == SharpVoice.Core.VoiceMode.Dictation) || !hits.Any(x => x.Mode == SharpVoice.Core.VoiceMode.AskAi))) throw new Exception("Short default phrase did not remain Ask AI.");
        if (detector.Activity is not { Epoch: 1 }) throw new Exception("Wake endpoint did not observe this capture epoch.");
        if (detector.Enqueue(new float[16001], audio.Length, 1) || detector.Activity is not null) throw new Exception("Overloaded wake worker retained stale silence activity.");
        Console.WriteLine("PASS WAKE expected=" + expected + " cpu_ms=" + (process.TotalProcessorTime - cpu).TotalMilliseconds.ToString("F0") + " elapsed_ms=" + watch.Elapsed.TotalMilliseconds.ToString("F0") + " private_mb=" + (process.PrivateMemorySize64 / 1048576));
    }
    public static async Task ModelFixture(string archive, string root)
    {
        using var http = new HttpClient(new LocalArchive(archive));
        var directory = await new WakeModelManager(http, root).EnsureAsync(p => Console.WriteLine(p.State), default);
        using var offline = new HttpClient(new NoDownloads());
        await new WakeModelManager(offline, root).EnsureAsync(_ => { }, default);
        var vad = Path.Combine(directory, "silero_vad.onnx");
        await File.WriteAllBytesAsync(vad, [0]);
        using var corrupt = new HttpClient(new BadArchive(643854)); var ready = false;
        try { await new WakeModelManager(corrupt, root).EnsureAsync(p => ready |= p.State == "ready", default); throw new Exception("Corrupt replacement speech model accepted."); }
        catch (InvalidDataException) { }
        if (ready || Directory.GetFiles(root, "*.download", SearchOption.AllDirectories).Length != 0) throw new Exception("Bad speech model left ready/partial state.");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await new WakeModelManager(http, root).EnsureAsync(_ => { }, canceled.Token); throw new Exception("Canceled speech model repair continued."); }
        catch (OperationCanceledException) { }
        await new WakeModelManager(http, root).EnsureAsync(_ => { }, default);
        await new WakeModelManager(offline, root).EnsureAsync(_ => { }, default);
        Console.WriteLine("PASS verified keyword/speech model installation, offline cache, corrupt replacement rejection and repair: " + directory);
    }
    private sealed class LocalArchive(string archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(request.RequestUri!.AbsolutePath.EndsWith("silero_vad.onnx", StringComparison.Ordinal) ? Path.Combine(Path.GetDirectoryName(archive)!, "silero_vad.onnx") : archive)) });
    }
    private sealed class NoDownloads : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new Exception("A verified cached model attempted a download.");
    }
    public static Task Tokenization()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Wake");
        var tokenizer = new WakeTokenizer(Path.Combine(root, "bpe.model"), Path.Combine(root, "tokens.txt"));
        foreach (var line in File.ReadAllLines(Path.Combine(root, "tokens-reference.tsv")))
        {
            var pair = line.Split('\t');
            if (tokenizer.Encode(pair[0]) != pair[1]) throw new Exception("Wake tokenization differs from upstream SentencePiece: " + pair[0]);
        }
        if (tokenizer.Encode("Hey SharpVoice") != tokenizer.Encode("hey sharp voice")) throw new Exception("Branded spoken words differ.");
        foreach (var invalid in new[] { "", "Hello 世界", "hey @ system" })
        {
            try { tokenizer.Encode(invalid); throw new Exception("Invalid phrase accepted: " + invalid); }
            catch (ArgumentException) { }
        }
        return Task.CompletedTask;
    }
}
