using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private const string LocalDownloadGuidance = "Choose Local AI and a model, then Download Model to prepare it before dictating. Qwen 0.5B is faster and smaller; 1.5B uses more memory and CPU. Downloads show transfer and SHA-256 verification progress. Cancel, changing the model/provider or exiting SharpVoice stops the download. Downloads continue in the background when you switch tabs, close Settings or start dictating. Partial files are removed; retry starts a new download. Existing valid downloads are verified and reused. Downloading does not save or activate these choices: use Apply Settings to keep them. Auto selects Qwen 0.5B.";
    public Func<string, Action<LocalModelProgress>, CancellationToken, Task>? LocalModelDownloadRequested { get; set; }
    public Func<string, bool>? IsLocalModelPresent { get; set; }
    private nint localDownloadButton, localDownloadBar;
    private CancellationTokenSource? localDownloadCancellation;
    private long localDownloadGeneration;
    private string localSelection = "", localMessage = "";
    private bool localDownloadCancelling;
    private int localDownloadPercent;

    private void CancelLocalDownloadCore(bool clear)
    {
        ++localDownloadGeneration;
        if (localDownloadCancellation is not null && !localDownloadCancelling)
        { localDownloadCancelling = true; localDownloadCancellation.Cancel(); }
        if (clear) { localSelection = localMessage = ""; localDownloadPercent = 0; UpdateHelp("LocalDownload", LocalDownloadGuidance); }
    }
    private void CancelLocalDownload()
    {
        CancelLocalDownloadCore(clear: true);
        RefreshConnectionTestControls();
    }
    private void RefreshLocalDownloadControls()
    {
        if (localDownloadButton == 0) return;
        var backend = SelectedChoice("Backend"); var selected = SelectedModelId();
        if (backend != "embedded" || (localSelection.Length != 0 && localSelection != selected)) CancelLocalDownloadCore(clear: true);
        var local = backend == "embedded";
        var onPage = Win32.SendMessageW(tabControl, 0x130B, 0, 0) == 1;
        var model = LocalAiModels.Find(selected);
        Win32.ShowWindow(localDownloadButton, local && onPage ? 5 : 0);
        Win32.ShowWindow(localDownloadBar, local && onPage ? 5 : 0);
        if (helpEntries.TryGetValue(2113, out var help)) Win32.ShowWindow(help.Button, local && onPage ? 5 : 0);
        Win32.EnableWindow(localDownloadButton, local && model is not null && !active && !localDownloadCancelling && connectionCancellation is null && LocalModelDownloadRequested is not null ? 1 : 0);
        Win32.SetWindowTextW(localDownloadButton, localDownloadCancellation is null ? "Download Model" : localDownloadCancelling ? "Canceling…" : "Cancel Download");
        if (!local) return;
        var text = model is null ? "Choose a supported local model." : localMessage.Length != 0 ? localMessage
            : (IsLocalModelPresent?.Invoke(selected) == true ? "Downloaded" : "Not Downloaded") + $" · {model.SizeBytes / 1_000_000d:F0} MB";
        Win32.SetWindowTextW(modelHint, text);
        Win32.SetWindowTextW(localDownloadBar, text);
        Win32.SendMessageW(localDownloadBar, 0x402, (nuint)localDownloadPercent, 0);
    }
    private void RequestLocalDownload()
    {
        if (Win32.IsWindowVisible(localDownloadButton) == 0 || active || connectionCancellation is not null || LocalModelDownloadRequested is null) return;
        if (localDownloadCancellation is not null)
        {
            if (!localDownloadCancelling) { CancelLocalDownloadCore(clear: false); localMessage = "Download Canceled"; RefreshConnectionTestControls(); }
            return;
        }
        var selected = SelectedModelId();
        var model = LocalAiModels.Find(selected);
        if (SelectedChoice("Backend") != "embedded" || model is null) return;
        localSelection = selected; localMessage = "Checking Download…"; localDownloadPercent = 0;
        var owner = new CancellationTokenSource(); localDownloadCancellation = owner;
        var request = ++localDownloadGeneration;
        RefreshConnectionTestControls();
        Task task;
        try
        {
            task = LocalModelDownloadRequested(selected, progress => Post(() =>
            {
                if (request != localDownloadGeneration || !ReferenceEquals(localDownloadCancellation, owner) || progress.ModelId != model.Id) return;
                localDownloadPercent = progress.Percent;
                localMessage = progress.Stage switch
                {
                    LocalModelStage.Downloading => $"Downloading · {progress.Percent}% · {progress.Bytes / 1_000_000d:F1} / {progress.TotalBytes / 1_000_000d:F1} MB",
                    LocalModelStage.Verifying => $"Verifying SHA-256 · {progress.Percent}%",
                    LocalModelStage.Ready => "Ready · " + model.Label,
                    _ => "Checking Download…"
                };
                RefreshLocalDownloadControls();
            }), owner.Token);
        }
        catch (Exception ex) { task = Task.FromException(ex); }
        _ = CompleteLocalDownloadAsync(task, owner, request, model);
    }
    private async Task CompleteLocalDownloadAsync(Task task, CancellationTokenSource owner, long request, LocalAiModel model)
    {
        string? error = null;
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { error = "Download Canceled"; }
        catch (Exception ex) { error = ex is InvalidDataException or IOException ? ex.Message : "Download failed. Check your network and disk space, then retry."; }
        Post(() =>
        {
            if (!ReferenceEquals(localDownloadCancellation, owner)) return;
            var canceled = owner.IsCancellationRequested;
            localDownloadCancellation = null; localDownloadCancelling = false; owner.Dispose();
            if (request == localDownloadGeneration)
            {
                localMessage = error is null && !canceled ? "Ready · " + model.Label : Short(error ?? "Download Canceled", 78);
                if (error is null && !canceled) localDownloadPercent = 100;
                UpdateHelp("LocalDownload", (error is null ? "Model ready: " + model.Id : Short(error, 800)) + "\n\n" + LocalDownloadGuidance);
            }
            RefreshConnectionTestControls();
        });
    }
    internal bool HandleLocalDownloadInput(in Win32.Msg message)
    {
        if (message.Hwnd != localDownloadButton || message.Message is not (0x100 or 0x101) || message.WParam is not (13 or 32)) return false;
        if (message.Message == 0x100 && ((long)message.LParam & (1L << 30)) == 0) RequestLocalDownload();
        return true;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint LocalDownloadProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (current is { } self)
        {
            var input = new Win32.Msg { Hwnd = hwnd, Message = message, WParam = wParam, LParam = lParam };
            if (self.HandleLocalDownloadInput(input)) return 0;
            if (message == 0x87 && wParam is 13 or 32) return Win32.DefSubclassProc(hwnd, message, wParam, lParam) | 4;
        }
        if (message == 0x82) Win32.RemoveWindowSubclass(hwnd, &LocalDownloadProcedure, id);
        return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
    }
}
