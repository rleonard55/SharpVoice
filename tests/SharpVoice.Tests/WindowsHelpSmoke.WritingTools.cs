using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    private static unsafe void CheckWritingTools(NativeDesktop desktop, Action<ThemeColors> changeTheme, string? output)
    {
        static void Check(bool value, string why) { if (!value) throw new Exception(why); }
        var window = desktop.SettingsHandle;
        string Text(nint control)
        {
            var buffer = new char[4096];
            fixed (char* value = buffer) return new string(buffer, 0, Win32.GetWindowTextW(control, value, buffer.Length));
        }
        void Page(int page)
        {
            var tabs = Win32.GetDlgItem(window, 1014); Win32.SendMessageW(tabs, 0x130C, (nuint)page, 0);
            var notification = new Win32.Notification { Window = tabs, Id = 1014, Code = -551 };
            Win32.SendMessageW(window, 0x4E, 1014, (nint)(&notification));
        }
        void Open(int id) { Page(5); Win32.SendMessageW(window, 0x111, (nuint)id, Win32.GetDlgItem(window, id)); Check(desktop.ToolsWindow != 0, "Writing manager failed to open."); }
        void Command(int id) => Win32.SendMessageW(desktop.ToolsWindow, 0x111, (nuint)id, Win32.GetDlgItem(desktop.ToolsWindow, id));
        void Set(string key, string value) => Win32.SetWindowTextW(desktop.ToolField(key), value);
        nint List() => Win32.GetDlgItem(desktop.ToolsWindow, 4101);
        void Select(int index) { Win32.SendMessageW(List(), 0x186, (nuint)index, 0); Win32.SendMessageW(desktop.ToolsWindow, 0x111, (1u << 16) | 4101, List()); }
        void Render(string kind)
        {
            if (output is null) return;
            foreach (var palette in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
            { changeTheme(palette); Win32.SendMessageW(window, 0x1A, 0, 0); Pump(); Check(RenderHelp(desktop.ToolsWindow, output + "-" + kind + (palette.Dark ? "-dark.bmp" : "-light.bmp"), 60) == palette.Background, "Manager background did not render in the current theme."); }
        }
        var originalSave = desktop.ConfigChanged;
        var saves = 0; AppConfig? saved = null;
        desktop.ConfigChanged = config => { saves++; saved = config; };
        Open(114); Set("Trigger", "sharp voice"); Set("Value", "SharpVoice");
        Command(4203); Check(Win32.SendMessageW(List(), 0x18B, 0, 0) == 1, "Dictionary entry not added to draft.");
        var save = Win32.GetDlgItem(desktop.ToolsWindow, 4204);
        Win32.SendMessageW(save, 0x100, 13, 0); Win32.SendMessageW(save, 0x101, 13, 0);
        Check(saves == 1 && saved!.Dictionary.Single().Preferred == "SharpVoice" && desktop.ToolsWindow == 0, "Keyboard save failed or double-applied.");
        Open(114); Select(0); Check(Text(desktop.ToolField("Value")) == "SharpVoice", "Saved dictionary was not recalled.");
        Set("Value", "Discard this change"); Command(4205);
        Open(114); Select(0); Check(Text(desktop.ToolField("Value")) == "SharpVoice" && saves == 1, "Cancel changed preferences.");
        Command(4201); Set("Trigger", "SHARP VOICE"); Set("Value", "duplicate"); Command(4203);
        Check(Win32.SendMessageW(List(), 0x18B, 0, 0) == 1 && Text(Win32.GetDlgItem(desktop.ToolsWindow, 4100)).Contains("unique"), "Duplicate validation lost the draft or opened a modal error.");
        Select(0); Render("dictionary");
        var successfulSave = desktop.ConfigChanged;
        desktop.ConfigChanged = _ => throw new IOException("Fixture save failed.");
        Set("Value", "Uncommitted spelling"); Command(4204);
        Check(desktop.ToolsWindow != 0 && Text(Win32.GetDlgItem(desktop.ToolsWindow, 4100)).Contains("Fixture save failed"), "Save failure closed or discarded the editor.");
        desktop.ConfigChanged = successfulSave; Command(4205);
        Open(115); Set("Trigger", "insert signature"); Set("Value", "Regards,\r\nLeon"); Command(4204);
        Check(saves == 2 && saved!.Snippets.Single().Text.Contains("Leon"), "Save did not commit the current form.");
        Open(115); Select(0); Render("snippets"); Command(4205);
        Open(116); Set("Name", "Work Email"); Set("Process", "outlook.exe"); Set("Model", "haiku"); Set("Minimum", "0"); Set("Instructions", "Keep messages concise.");
        Win32.SendMessageW(desktop.ToolField("Backend"), 0x14E, 4, 0);
        Win32.SendMessageW(desktop.ToolField("Mode"), 0x14E, 3, 0);
        Render("profiles"); Command(4204);
        Check(saves == 3 && saved!.Profiles.Single().Backend == "claude_code" && saved.Profiles[0].Mode == "email", "Profile choices saved labels or wrong IDs.");
        var journal = new DictationHistory();
        var first = new DictationEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "notepad", "Please shorten this.", "Please shorten this.", "Original result.", "Original selected source.", false, new AppConfig(), "", "Inserted", "");
        journal.Add(first);
        desktop.ReadHistory = journal.Snapshot;
        desktop.DeleteHistoryEntry = journal.Remove; desktop.ClearHistoryRequested = journal.Clear;
        CancellationToken retryToken = default; HistoryRetryRequest? request = null;
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        desktop.HistoryRetryRequested = (input, ct) => { request = input; retryToken = ct; return pending.Task; };
        Open(117); Check(Text(desktop.ToolField("Before")) == first.SourceText, "History compare omitted selected source.");
        Win32.SendMessageW(desktop.ToolField("RetryBackend"), 0x14E, 1, 0); Set("RetryModel", "fixture-model");
        Command(4303); Check(request?.EntryId == first.Id && request.Backend == "chatgpt_oauth" && request.Model == "fixture-model", "History retry ignored draft provider/model.");
        Command(4303); Check(retryToken.IsCancellationRequested, "History retry was not cancellable.");
        pending.SetResult();
        for (var i = 0; i < 30 && Text(Win32.GetDlgItem(desktop.ToolsWindow, 4303)) != "Retry With Model"; i++) Pump();
        Check(Text(desktop.ToolField("After")) == "Original result.", "Canceled retry replaced the result.");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously); Command(4303);
        journal.Update(first.Id, "Short source.", "fixture-model", "Retried · Not Pasted", ""); pending.SetResult();
        for (var i = 0; i < 30 && Text(desktop.ToolField("After")) != "Short source."; i++) Pump();
        Check(Text(desktop.ToolField("After")) == "Short source.", "History retry did not refresh comparison.");
        Render("history");
        var second = first with { Id = Guid.NewGuid(), AppName = "code", Transcript = "Another dictation.", SourceText = null };
        journal.Add(second); desktop.NotifyHistoryChanged();
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously); Command(4303); Select(0);
        Check(retryToken.IsCancellationRequested, "Changing history selection did not cancel the old retry.");
        pending.SetResult(); Pump(); Select(1);
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously); Command(4303); desktop.SetActive(true);
        Check(retryToken.IsCancellationRequested, "Dictation start did not cancel the history retry.");
        pending.SetResult(); Pump(); desktop.SetActive(false);
        Command(4301); Check(journal.Snapshot().Length == 1 && journal.Find(first.Id) is null, "Delete history failed.");
        journal.Add(first); desktop.NotifyHistoryChanged(); Command(4302); Check(journal.Snapshot().Length == 0, "Clear history failed.");
        journal.Add(first); desktop.NotifyHistoryChanged(); pending = new(TaskCreationOptions.RunContinuationsAsynchronously); Command(4303);
        Win32.SendMessageW(desktop.ToolsWindow, 0x10, 0, 0);
        Check(retryToken.IsCancellationRequested, "Closing history did not cancel the retry."); pending.SetResult(); Pump();
        Check(desktop.ToolsWindow == 0 && saves == 3, "History unexpectedly saved settings.");
        Page(5); Win32.SendMessageW(Win32.GetDlgItem(window, 1029), 0x14E, 1, 0);
        Page(3); Win32.SendMessageW(Win32.GetDlgItem(window, 1027), 0x14E, 1, 0);
        Win32.SetWindowTextW(Win32.GetDlgItem(window, 1028), "3");
        Win32.SendMessageW(window, 0x111, 101, Win32.GetDlgItem(window, 101));
        Check(saves == 4 && saved!.RecordingGesture == "toggle" && saved.SilenceStopSeconds == 3 && saved.PersistHistory, "New recording/history choices did not persist through Apply.");
        Win32.ShowWindow(window, 4);
        desktop.ConfigChanged = originalSave;
        Console.WriteLine("PASS native writing-rule CRUD, draft cancel/validation, keyboard save, profiles, history comparison/retry/cancel/delete and both themes.");
    }
}
