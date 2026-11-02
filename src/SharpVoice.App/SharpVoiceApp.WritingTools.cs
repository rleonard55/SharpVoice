using System.Diagnostics;
using SharpVoice.Core;
using SharpVoice.Refiner;
using SharpVoice.Refiner.Auth;

namespace SharpVoice.App;

internal sealed partial class SharpVoiceApp
{
    private readonly DictationHistory history = new();
    private readonly DictationHistoryStore historyStore;
    private HistoryRefiner historyRefiner = null!;
    private HistoryWorkQueue historyTasks = new();
    private long historyRevision;
    private AppConfig recordingConfig = new();
    private string recordingModel = "";
    private readonly Stopwatch recordingClock = new();
    private SilenceStopPolicy silenceStop = new(0);
    private Timer? silenceTimer;

    private void InitializeWritingTools(HttpClient http, ChatGptAuthManager auth)
    {
        recordingConfig = config;
        historyRefiner = new(http, auth);
        silenceTimer = new Timer(_ => { var id = generation; desktop.Post(() => CheckSilence(id)); }, null, Timeout.Infinite, Timeout.Infinite);
        desktop.ReadHistory = history.Snapshot;
        desktop.DeleteHistoryEntry = id => { history.Remove(id); HistoryChanged(); };
        desktop.ClearHistoryRequested = () => { history.Clear(); HistoryChanged(); };
        desktop.HistoryRetryRequested = RetryHistory;
    }
    private void LoadHistory()
    {
        if (!config.PersistHistory) { PersistHistory(); return; }
        var revision = historyRevision;
        historyTasks = new HistoryWorkQueue(() =>
        {
            try
            {
                var entries = historyStore.Load();
                desktop.Post(() =>
                {
                    if (historyRevision != revision || !config.PersistHistory) return;
                    history.Restore(entries); desktop.NotifyHistoryChanged();
                });
            }
            catch { desktop.Post(() => desktop.SetStatus("Saved text history could not be read. New dictations will start a new saved history.", isError: true)); }
        });
        preloads.Add(historyTasks.Ready);
    }
    private void HistoryChanged()
    {
        historyRevision++;
        desktop.NotifyHistoryChanged(); PersistHistory();
    }
    private void PersistHistory()
    {
        var snapshot = history.Snapshot(); var enabled = config.PersistHistory;
        // Serialize writes/deletes so disabling persistence or clearing cannot be undone by an older write.
        historyTasks.Enqueue(() =>
        {
            try { if (enabled) historyStore.Save(snapshot); else historyStore.Delete(); }
            catch { desktop.Post(() => desktop.SetStatus("Text history could not be saved or removed. Check disk access; dictation is still available.", isError: true)); }
        });
    }
    private void AddHistory(DictationEntry entry)
    {
        try { history.Add(entry); HistoryChanged(); }
        catch { desktop.SetStatus("This dictation could not be added to recent history. The current text is still available.", isError: true); }
    }
    private void UpdateHistory(Guid id, string? result, string status, string error = "")
    {
        var entry = history.Find(id); if (entry is null) return;
        if (history.Update(id, result ?? entry.Result, recordingModel.Length > 512 ? recordingModel[..512] : recordingModel, status, error.Length > 4000 ? error[..4000] : error)) HistoryChanged();
    }
    private Task RetryHistory(HistoryRetryRequest request, CancellationToken ct)
    {
        if (phase != Phase.Idle || authBusy || editorSession.Busy) throw new InvalidOperationException("Finish the current operation before retrying history.");
        var entry = history.Find(request.EntryId) ?? throw new InvalidOperationException("This history entry was deleted.");
        var dictionary = config.Dictionary;
        var task = Task.Run(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            var response = await historyRefiner.RetryAsync(entry, request.Backend, request.Model, linked.Token);
            var result = !entry.AskAi && entry.SourceText is null ? WritingTools.Correct(response.Text, dictionary) : response.Text;
            linked.Token.ThrowIfCancellationRequested();
            desktop.Post(() =>
            {
                if (ct.IsCancellationRequested || lifetime.IsCancellationRequested || phase != Phase.Idle) return;
                if (!history.Update(entry.Id, result, response.Model, "Retried · Not Pasted", "")) return;
                HistoryChanged(); desktop.SetPreview(result, retain: true);
            });
        });
        preloads.RemoveAll(t => t.IsCompleted); preloads.Add(task); return task;
    }
    private void BeginSilenceWatch()
    {
        recordingClock.Restart();
        silenceStop = new(recordingConfig.SilenceStopSeconds);
        var interval = wakeSession is not null || recordingConfig.RecordingGesture == "toggle" && recordingConfig.SilenceStopSeconds > 0 ? 200 : Timeout.Infinite;
        silenceTimer?.Change(interval, interval);
    }
    private void CheckSilence(int id)
    {
        if (id != generation || phase != Phase.Recording) return;
        if (wakeSession is not null) { if (WakeSpeechEnded()) Released(); return; }
        if (recordingConfig.RecordingGesture != "toggle") return;
        if (silenceStop.Observe(recorder.LevelDb, recordingConfig.NoiseGateDb, recordingClock.Elapsed)) Released();
    }
    private void StopSilenceWatch() { silenceTimer?.Change(Timeout.Infinite, Timeout.Infinite); recordingClock.Stop(); }
    private string RecordingHint => wakeSession is not null ? $"Stops after {recordingConfig.WakeSilenceSeconds} seconds of silence · Escape cancels" : answerReply is not null ? "Click Stop Recording in the answer window when finished" : editorSession.Busy ? "Use Stop Recording in VS Code when finished" : recordingConfig.RecordingGesture == "toggle" ? "Press the shortcut again when finished" : "Release the keys when finished";
}
