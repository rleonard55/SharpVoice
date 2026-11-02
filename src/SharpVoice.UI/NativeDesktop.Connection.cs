using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private const string ConnectionGuidance = "Test the AI provider and model currently shown, without applying or saving settings. Sends only a fixed public test request; no microphone, selected text or clipboard is used. Remote tests use a small amount of account allowance. Local AI requires its model already downloaded. Cancel Test, changing provider/model, leaving this tab or starting dictation cancels the test. Timeout: 60 seconds.";
    public Func<string, string, CancellationToken, Task<ConnectionTestResult>>? ConnectionTestRequested { get; set; }
    private nint connectionTestButton, connectionStatus;
    private CancellationTokenSource? connectionCancellation;
    private long connectionGeneration;
    private string connectionBackend = "", connectionModel = "", connectionMessage = "";
    private bool connectionCancelling;
    private int connectionResultState;

    private void RefreshConnectionTestControls()
    {
        if (connectionTestButton == 0) return;
        var backend = SelectedChoice("Backend");
        var authBusy = IsAuthenticationBusy?.Invoke() == true;
        if ((connectionBackend.Length != 0 && (connectionBackend != backend || connectionModel != SelectedModelId())) || authBusy || active)
            CancelConnectionTestCore(clearResult: true);
        Win32.ShowWindow(connectionTestButton, Win32.SendMessageW(tabControl, 0x130B, 0, 0) == 1 && backend != "none" ? 5 : 0);
        Win32.EnableWindow(connectionTestButton, !active && !authBusy && !connectionCancelling && localDownloadCancellation is null && ConnectionTestRequested is not null ? 1 : 0);
        Win32.SetWindowTextW(connectionTestButton, connectionCancellation is null ? "Test Connection" : connectionCancelling ? "Canceling Test…" : "Cancel Test");
        Win32.SetWindowTextW(connectionStatus, connectionMessage);
        Win32.InvalidateRect(connectionStatus, 0, 1);
        RefreshLocalDownloadControls();
    }
    private void CancelConnectionTestCore(bool clearResult)
    {
        ++connectionGeneration;
        if (connectionCancellation is not null && !connectionCancelling)
        {
            connectionCancelling = true;
            connectionCancellation.Cancel();
        }
        if (clearResult)
        {
            connectionBackend = connectionModel = connectionMessage = "";
            connectionResultState = 0;
            UpdateHelp("ConnectionTest", ConnectionGuidance);
        }
    }
    private void CancelConnectionTest()
    {
        CancelConnectionTestCore(clearResult: true);
        RefreshConnectionTestControls();
    }
    private void RequestConnectionTest()
    {
        if (connectionTestButton == 0 || Win32.IsWindowVisible(connectionTestButton) == 0 || active || localDownloadCancellation is not null || IsAuthenticationBusy?.Invoke() == true || ConnectionTestRequested is null) return;
        if (connectionCancellation is not null)
        {
            if (!connectionCancelling)
            {
                CancelConnectionTestCore(clearResult: false);
                connectionMessage = "Test Canceled";
                connectionResultState = 0;
                RefreshConnectionTestControls();
            }
            return;
        }
        var backend = SelectedChoice("Backend");
        var model = SelectedModelId();
        if (backend == "none") return;
        connectionBackend = backend; connectionModel = model;
        connectionMessage = "Testing Connection…"; connectionResultState = 0;
        var request = ++connectionGeneration;
        var owner = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        connectionCancellation = owner;
        RefreshConnectionTestControls();
        Task<ConnectionTestResult> task;
        try { task = ConnectionTestRequested(backend, model, owner.Token); }
        catch (Exception ex) { task = Task.FromException<ConnectionTestResult>(ex); }
        _ = CompleteConnectionTestAsync(task, owner, request);
    }
    private async Task CompleteConnectionTestAsync(Task<ConnectionTestResult> task, CancellationTokenSource owner, long request)
    {
        ConnectionTestResult? result = null;
        string? failure = null;
        try { result = await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { failure = "The test timed out. Check your network or provider, then retry."; }
        catch (Exception ex)
        {
            failure = ex is InvalidOperationException or InvalidDataException ? ex.Message
                : ex is ArgumentException ? "Choose a valid AI provider and model, then retry."
                : "Could not reach the AI provider. Check your network and sign-in, then retry.";
            failure = failure.Replace("chatgpt_oauth", "ChatGPT", StringComparison.Ordinal).Replace(" The transcript is retained in the preview.", "", StringComparison.Ordinal)
                .Replace(" The raw transcript is retained; nothing was inserted.", "", StringComparison.Ordinal);
        }
        Post(() =>
        {
            if (!ReferenceEquals(connectionCancellation, owner)) return;
            var canceled = owner.IsCancellationRequested;
            connectionCancellation = null; connectionCancelling = false;
            owner.Dispose();
            if (request == connectionGeneration)
            {
                if (result is not null && !canceled)
                {
                    connectionResultState = 1;
                    connectionMessage = $"✓ Connected · {Short(result.Model, 48)} · {result.Elapsed.TotalSeconds:F1} s";
                    UpdateHelp("ConnectionTest", $"Connection succeeded. Responding model: {result.Model}. Time: {result.Elapsed.TotalSeconds:F1} seconds.\n\n" + ConnectionGuidance);
                }
                else
                {
                    connectionResultState = 2;
                    var detail = failure ?? "The test timed out. Check the provider and retry.";
                    connectionMessage = "Connection Failed · " + Short(detail.Replace('\r', ' ').Replace('\n', ' '), 48);
                    UpdateHelp("ConnectionTest", Short(detail, 1000) + "\n\n" + ConnectionGuidance);
                }
            }
            RefreshConnectionTestControls();
        });
    }
    internal bool HandleConnectionInput(in Win32.Msg message)
    {
        if (message.Hwnd != connectionTestButton || message.Message is not (0x100 or 0x101) || message.WParam is not (13 or 32)) return false;
        if (message.Message == 0x100 && ((long)message.LParam & (1L << 30)) == 0) RequestConnectionTest();
        return true;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint ConnectionProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        var self = current;
        if (self is not null)
        {
            var input = new Win32.Msg { Hwnd = hwnd, Message = message, WParam = wParam, LParam = lParam };
            if (self.HandleConnectionInput(input)) return 0;
            if (message == 0x87 && wParam is 13 or 32) return Win32.DefSubclassProc(hwnd, message, wParam, lParam) | 4;
        }
        if (message == 0x82) Win32.RemoveWindowSubclass(hwnd, &ConnectionProcedure, id);
        return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
    }
    private uint ConnectionStatusColor => connectionResultState switch
    {
        1 => theme.Dark ? 0xA7DB8Cu : 0x337313u,
        2 => theme.Dark ? 0xA3A3FFu : 0x1E26B3u,
        _ => theme.Muted
    };
}
