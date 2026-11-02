using SharpVoice.Core;
using SharpVoice.Refiner;

internal static class DictationHistoryChecks
{
    public static Task Journal()
    {
        var journal = new DictationHistory();
        var first = Entry("first"); journal.Add(first);
        if (!journal.Update(first.Id, "after", "model", "Ready", "") || journal.Find(first.Id)?.Result != "after") throw new Exception("History update lost its entry.");
        journal.Remove(first.Id);
        if (journal.Update(first.Id, "late retry", "model", "Ready", "")) throw new Exception("Late retry resurrected a deleted entry.");
        for (var i = 0; i < 60; i++) journal.Add(Entry(i.ToString()));
        if (journal.Snapshot().Length != 50 || journal.Snapshot()[0].Transcript != "59") throw new Exception("History was not newest-first and bounded.");
        journal.Clear();
        if (journal.Snapshot().Length != 0) throw new Exception("Clear history failed.");
        return Task.CompletedTask;
    }
    public static async Task StartupOrdering()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<string>();
        var queue = new HistoryWorkQueue(() => { entered.SetResult(); release.Task.GetAwaiter().GetResult(); });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            queue.Enqueue(() => writes.Add("clear"));
            queue.Enqueue(() => writes.Add("new dictation"));
            await Task.Delay(100);
            if (queue.Ready.IsCompleted || queue.Completion.IsCompleted || writes.Count != 0)
                throw new Exception("History became ready or wrote before saved history finished loading.");
        }
        finally { release.TrySetResult(); }
        await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        if (!writes.SequenceEqual(new[] { "clear", "new dictation" })) throw new Exception("History writes overtook clear/load.");
        if (!new HistoryWorkQueue().Ready.IsCompletedSuccessfully) throw new Exception("Memory-only history blocked startup.");
    }
    public static Task EncryptedPersistence()
    {
        if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
        var path = Path.Combine(Path.GetTempPath(), "sharpvoice-history-" + Guid.NewGuid() + ".bin");
        try
        {
            var store = new DictationHistoryStore(path);
            store.Save([Entry("PRIVATE-DICTATION-CONTENT")]);
            if (System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("PRIVATE-DICTATION-CONTENT")) throw new Exception("History was saved in plaintext.");
            if (store.Load()[0].Transcript != "PRIVATE-DICTATION-CONTENT") throw new Exception("Encrypted history did not round-trip.");
            store.Delete(); if (File.Exists(path)) throw new Exception("Turning off persistence did not delete disk history.");
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }
    private static DictationEntry Entry(string text) => new(Guid.NewGuid(), DateTimeOffset.UtcNow, "notepad", text, text, "", null, false, new AppConfig(), "", "Transcribed", "");
}
