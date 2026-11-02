using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    public Func<DictationEntry[]>? ReadHistory { get; set; }
    public Action<Guid>? DeleteHistoryEntry { get; set; }
    public Action? ClearHistoryRequested { get; set; }
    public Func<HistoryRetryRequest, CancellationToken, Task>? HistoryRetryRequested { get; set; }
    private DictationEntry[] historyRows = [];
    private Guid? selectedHistory;
    private CancellationTokenSource? historyRetryCancellation;
    private long historyRetryGeneration;
    private nint historyRetryButton;

    public void NotifyHistoryChanged()
    {
        if (toolsKind == "history" && toolsWindow != 0) RefreshHistoryList();
    }
    private void OpenHistory()
    {
        StartToolsWindow("history", "Recent Dictations");
        ToolEdit("Before", "Before · Selected Source or Original Transcript", 104, 158, multiline: true, readOnly: true);
        ToolEdit("After", "After · Result (Retries Are Never Pasted Automatically)", 302, 158, multiline: true, readOnly: true);
        ToolCombo("RetryBackend", "Retry Using Provider", 504, choices["Backend"].Select(c => c.Label).ToArray());
        ToolEdit("RetryModel", "Retry Model ID (Auto or an Exact ID from Your Account)", 568);
        ToolButton("Delete Entry", 4301, 24, 596, 112);
        ToolButton("Clear All", 4302, 148, 596, 112);
        historyRetryButton = ToolButton("Retry With Model", 4303, 288, 636, 230);
        ToolButton("Copy Result", 4304, 540, 636, 230);
        ToolButton("Close", 4205, 640, 692, 168);
        ToolControl("STATIC", config.PersistHistory ? "Text history is saved locally, encrypted for this Windows user." : "Text-only session history · Cleared when SharpVoice exits.", 0, 24, 650, 238, 65);
        selectedHistory = null; RefreshHistoryList(); FinishToolsWindow();
    }
    private void RefreshHistoryList()
    {
        historyRows = ReadHistory?.Invoke() ?? [];
        Win32.SendMessageW(toolsList, 0x184, 0, 0);
        foreach (var row in historyRows) AddToolListItem($"{row.CreatedAt.ToLocalTime():HH:mm} · {row.AppName} · {row.Transcript}");
        var index = selectedHistory is Guid id ? Array.FindIndex(historyRows, row => row.Id == id) : -1;
        if (index < 0 && historyRows.Length != 0) index = 0;
        Win32.SendMessageW(toolsList, 0x186, unchecked((nuint)(nint)index), 0);
        SelectHistory();
    }
    private void SelectHistory()
    {
        var index = (int)Win32.SendMessageW(toolsList, 0x188, 0, 0);
        var entry = index >= 0 && index < historyRows.Length ? historyRows[index] : null;
        var previous = selectedHistory;
        selectedHistory = entry?.Id;
        if (previous != selectedHistory && historyRetryCancellation is not null) CancelHistoryRetry();
        Win32.SetWindowTextW(toolFields["Before"], entry is null ? "" : !entry.AskAi && entry.SourceText is not null ? entry.SourceText : entry.Transcript);
        Win32.SetWindowTextW(toolFields["After"], entry?.Result ?? "");
        if (previous != selectedHistory || Text(toolFields["RetryModel"]).Length == 0)
        {
            Win32.SetWindowTextW(toolFields["RetryModel"], entry?.Settings.RefinementModel ?? "auto");
            var provider = entry?.Settings.Backend ?? config.Backend;
            Win32.SendMessageW(toolFields["RetryBackend"], 0x14E, (nuint)Math.Max(0, Array.FindIndex(choices["Backend"], c => c.Id == provider)), 0);
        }
        Win32.SetWindowTextW(toolsNotice, entry is null ? "No recent dictations yet. Dictate into another app to create an entry." : Short($"{entry.Status} · {entry.AppName} · {entry.Model}" + (entry.Error.Length == 0 ? "" : " · " + entry.Error), 190));
        RefreshHistoryRetryControls();
    }
    private void HandleHistoryCommand(int id)
    {
        switch (id)
        {
            case 4301:
                if (selectedHistory is Guid selected) { CancelHistoryRetry(); DeleteHistoryEntry?.Invoke(selected); RefreshHistoryList(); }
                break;
            case 4302: CancelHistoryRetry(); ClearHistoryRequested?.Invoke(); selectedHistory = null; RefreshHistoryList(); break;
            case 4303: RequestHistoryRetry(); break;
            case 4304:
                var entry = historyRows.FirstOrDefault(e => e.Id == selectedHistory);
                if (entry is not null) { TextInjector.SetClipboardText(entry.Result.Length == 0 ? entry.Transcript : entry.Result, toolsWindow); Win32.SetWindowTextW(toolsNotice, "Copied. Paste into the control you choose."); }
                break;
        }
    }
    private void CancelHistoryRetry()
    {
        ++historyRetryGeneration;
        historyRetryCancellation?.Cancel();
        RefreshHistoryRetryControls();
    }
    private void RefreshHistoryRetryControls()
    {
        if (toolsKind != "history" || toolsWindow == 0 || historyRetryButton == 0) return;
        var busy = historyRetryCancellation is not null;
        Win32.EnableWindow(Win32.GetDlgItem(toolsWindow, 4301), selectedHistory is null ? 0 : 1);
        Win32.EnableWindow(Win32.GetDlgItem(toolsWindow, 4304), selectedHistory is null ? 0 : 1);
        Win32.EnableWindow(Win32.GetDlgItem(toolsWindow, 4302), historyRows.Length == 0 ? 0 : 1);
        Win32.SetWindowTextW(historyRetryButton, busy ? historyRetryCancellation!.IsCancellationRequested ? "Canceling…" : "Cancel Retry" : "Retry With Model");
        Win32.EnableWindow(historyRetryButton, !active && selectedHistory is not null && HistoryRetryRequested is not null && historyRetryCancellation?.IsCancellationRequested != true ? 1 : 0);
        foreach (var key in new[] { "RetryBackend", "RetryModel" })
            if (toolFields.TryGetValue(key, out var control)) Win32.EnableWindow(control, busy ? 0 : 1);
    }
    private void RequestHistoryRetry()
    {
        if (active || toolsKind != "history" || toolsWindow == 0 || HistoryRetryRequested is null || selectedHistory is null) return;
        if (historyRetryCancellation is not null) { CancelHistoryRetry(); return; }
        var index = (int)Win32.SendMessageW(toolFields["RetryBackend"], 0x147, 0, 0);
        if (index < 0 || index >= choices["Backend"].Length) throw new ArgumentException("Choose a retry provider.");
        var model = Text(toolFields["RetryModel"]).Trim(); if (model.Length == 0) model = "auto";
        var owner = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        historyRetryCancellation = owner;
        var request = ++historyRetryGeneration;
        Win32.SetWindowTextW(toolsNotice, "Retrying retained text. Review the result here, then Copy Result to use it.");
        RefreshHistoryRetryControls();
        Task task;
        try { task = HistoryRetryRequested(new(selectedHistory.Value, choices["Backend"][index].Id, model), owner.Token); }
        catch (Exception ex) { task = Task.FromException(ex); }
        _ = CompleteHistoryRetryAsync(task, owner, request);
    }
    private async Task CompleteHistoryRetryAsync(Task task, CancellationTokenSource owner, long request)
    {
        string message = "Retry finished. Review the result and use Copy Result when ready.";
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { message = "Retry canceled or timed out. The previous result is kept."; }
        catch (Exception ex) { message = Short(ex.Message, 190); }
        Post(() =>
        {
            if (!ReferenceEquals(historyRetryCancellation, owner)) return;
            historyRetryCancellation = null; owner.Dispose();
            if (request == historyRetryGeneration && toolsKind == "history" && toolsWindow != 0)
            { RefreshHistoryList(); Win32.SetWindowTextW(toolsNotice, message); }
            RefreshHistoryRetryControls();
        });
    }
}
