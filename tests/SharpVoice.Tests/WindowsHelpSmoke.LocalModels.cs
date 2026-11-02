using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.UI;

internal static partial class WindowsHelpSmoke
{
    private static unsafe void CheckLocalDownloads(NativeDesktop desktop, Action<ThemeColors> theme, string? output)
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var window = desktop.SettingsHandle;
        nint Item(int id) => Win32.GetDlgItem(window, id);
        string Text(int id)
        {
            var text = new char[2048];
            fixed (char* p = text) return new string(text, 0, Win32.GetWindowTextW(Item(id), p, text.Length));
        }
        void Page(int page)
        {
            var tabs = Item(1014); Win32.SendMessageW(tabs, 0x130C, (nuint)page, 0);
            var note = new Win32.Notification { Window = tabs, Id = 1014, Code = -551 };
            Win32.SendMessageW(window, 0x4E, 1014, (nint)(&note));
        }
        void Provider(int index) { Win32.SendMessageW(Item(1002), 0x14E, (nuint)index, 0); Win32.SendMessageW(window, 0x111, (1u << 16) | 1002, Item(1002)); }
        void Model(string id) { Win32.SetWindowTextW(Item(1003), id); Win32.SendMessageW(window, 0x111, (5u << 16) | 1003, Item(1003)); }
        void Click(int id = 113) => Win32.SendMessageW(window, 0x111, (nuint)id, Item(id));
        void Wait(Func<bool> condition) { for (var i = 0; i < 30 && !condition(); i++) Pump(); Check(condition(), "Local download UI did not finish."); }
        Page(1); Provider(2);
        var model = LocalAiModels.All[1]; Model(model.Id);
        var calls = 0; string selected = ""; CancellationToken token = default; Action<LocalModelProgress>? progress = null;
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        desktop.IsLocalModelPresent = _ => false;
        desktop.LocalModelDownloadRequested = (id, report, ct) => { calls++; selected = id; progress = report; token = ct; return pending.Task; };
        desktop.RefreshAuthentication();
        Check(Win32.IsWindowVisible(Item(113)) != 0 && Win32.IsWindowVisible(Item(1026)) != 0, "Local AI has no download controls.");
        Check(Text(1017).Contains("1117"), "Local selection did not show model size.");
        Check((Win32.SendMessageW(Item(113), 0x87, 13, 0) & 4) != 0, "Download Enter could apply settings.");
        Win32.SendMessageW(Item(113), 0x100, 13, 0); Win32.SendMessageW(Item(113), 0x101, 13, 0);
        Check(calls == 1 && selected == model.Id, "Download used an applied/default model instead of the selected larger model.");
        var connectionCalls = 0;
        desktop.ConnectionTestRequested = (_, _, _) => { connectionCalls++; return Task.FromResult(new ConnectionTestResult("test", TimeSpan.Zero)); };
        Click(112); Check(connectionCalls == 0, "Connection test raced a download.");
        progress!(new(model.Id, LocalModelStage.Downloading, model.SizeBytes / 2, model.SizeBytes)); Pump();
        Check(Text(1017).Contains("50%") && Win32.SendMessageW(Item(1026), 0x408, 0, 0) == 50, "Byte progress and native bar disagree.");
        Page(0); Check(!token.IsCancellationRequested, "Navigation canceled a background download."); Page(1);
        Win32.SendMessageW(window, 0x10, 0, 0); Check(!token.IsCancellationRequested, "Closing Settings canceled a background download.");
        Win32.ShowWindow(window, 4);
        desktop.SetActive(true); Check(!token.IsCancellationRequested, "Dictation canceled the background download."); desktop.SetActive(false);
        progress(new(model.Id, LocalModelStage.Verifying, model.SizeBytes / 2, model.SizeBytes)); Pump();
        Check(Text(1017).Contains("Verifying SHA-256"), "Verification did not have distinct progress.");
        foreach (var palette in new[] { ThemeColors.Light, ThemeColors.DarkTheme })
        {
            theme(palette); Win32.SendMessageW(window, 0x1A, 0, 0);
            if (output is not null) RenderHelp(window, output + (palette.Dark ? "-download-dark.bmp" : "-download-light.bmp"));
        }
        Click(); Check(token.IsCancellationRequested, "Cancel Download did not cancel the worker.");
        var oldProgress = progress; Model(LocalAiModels.Default.Id); oldProgress(new(model.Id, LocalModelStage.Ready, model.SizeBytes, model.SizeBytes));
        pending.SetResult(); Wait(() => Text(113) == "Download Model");
        Check(!Text(1017).Contains("Ready"), "Old model completion overwrote the new selection.");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click(); Provider(1); Check(token.IsCancellationRequested, "Changing provider did not cancel download.");
        pending.SetResult(); Wait(() => Text(113) == "Download Model");
        Check(Win32.IsWindowVisible(Item(113)) == 0 && Win32.IsWindowVisible(Item(1026)) == 0, "Local download controls leaked into remote providers.");
        Provider(2); Model(LocalAiModels.Default.Id);
        desktop.LocalModelDownloadRequested = (_, _, _) => Task.FromException(new InvalidDataException("Fixture SHA-256 mismatch"));
        desktop.RefreshAuthentication(); Click(); Wait(() => Text(1017).Contains("SHA-256 mismatch"));
        desktop.LocalModelDownloadRequested = (id, report, ct) => { report(new(LocalAiModels.Resolve(id).Id, LocalModelStage.Ready, 1, 1)); return Task.CompletedTask; };
        Click(); Wait(() => Text(1017).Contains("Ready"));
        Check(Win32.SendMessageW(Item(1026), 0x408, 0, 0) == 100, "Ready download did not complete the native bar.");
        Provider(1);
        Console.WriteLine("PASS native local selection, byte/verification progress, background lifetime, cancellation, stale results and both themes.");
    }
}
