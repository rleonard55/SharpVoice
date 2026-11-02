namespace SharpVoice.Core;

public sealed record AppUpdateView(string CurrentVersion, string Status, string? AvailableVersion = null,
    string? ReadyVersion = null, bool Busy = false, int Progress = 0, bool Supported = true);

public interface IReleaseUpdater
{
    bool Supported { get; }
    string CurrentVersion { get; }
    string? ReadyVersion { get; }
    Task<string?> CheckAsync(CancellationToken ct);
    Task DownloadAsync(string version, Action<int> progress, CancellationToken ct);
    void ApplyAndRestart();
}

/// <summary>Owns update operations; the host owns UI-thread activity checks and orderly shutdown.</summary>
public sealed class ReleaseUpdateCoordinator : IAsyncDisposable
{
    private readonly IReleaseUpdater updater;
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private Task work = Task.CompletedTask;
    private bool automatic, automaticOperation, disposed, restartRequested;
    private string action;
    private AppUpdateView view;
    public static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(60);
    public event Action<AppUpdateView>? Changed;

    public ReleaseUpdateCoordinator(IReleaseUpdater updater, bool automatic, string action)
    {
        ValidateAction(action);
        this.updater = updater; this.automatic = automatic; this.action = action;
        view = new(updater.CurrentVersion, updater.Supported ? "Ready to check GitHub Releases." : "Install the release package to receive updates; development builds are not updated.",
            ReadyVersion: updater.ReadyVersion, Supported: updater.Supported);
    }
    public AppUpdateView View { get { lock (gate) return view; } }
    public bool RestartRequested { get { lock (gate) return restartRequested; } }
    private static void ValidateAction(string action)
    {
        if (action is not ("notify" or "download" or "install")) throw new ArgumentException("Choose a supported automatic update action.");
    }
    public void SetPreferences(bool enabled, string whenFound)
    {
        ValidateAction(whenFound);
        CancellationTokenSource? cancel = null;
        lock (gate)
        {
            if (disposed) return;
            if (automaticOperation && (!enabled || whenFound != action)) cancel = operation;
            automatic = enabled; action = whenFound;
        }
        try { cancel?.Cancel(); } catch (ObjectDisposedException) { }
    }
    public Task CheckAsync(bool scheduled = false, CancellationToken ct = default) => Start(downloadOnly: false, scheduled, ct);
    public Task DownloadAsync(CancellationToken ct = default) => Start(downloadOnly: true, scheduled: false, ct);
    private Task Start(bool downloadOnly, bool scheduled, CancellationToken ct)
    {
        lock (gate)
        {
            if (disposed || restartRequested || view.Busy || !updater.Supported || (scheduled && !automatic)) return Task.CompletedTask;
            if (downloadOnly && (view.AvailableVersion is null || view.ReadyVersion == view.AvailableVersion)) return Task.CompletedTask;
            operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, ct);
            automaticOperation = scheduled;
            view = view with { Busy = true, Progress = 0, Status = downloadOnly ? "Downloading update…" : "Checking GitHub Releases…" };
            var owner = operation;
            work = Task.Run(() => Run(downloadOnly, scheduled, owner), CancellationToken.None);
            return work;
        }
    }
    private void Publish(AppUpdateView next)
    {
        lock (gate) { if (disposed) return; view = next; }
        Changed?.Invoke(next);
    }
    private async Task Run(bool downloadOnly, bool scheduled, CancellationTokenSource owner)
    {
        try
        {
            Changed?.Invoke(View);
            string? candidate;
            if (downloadOnly) candidate = View.AvailableVersion;
            else
            {
                candidate = await updater.CheckAsync(owner.Token).ConfigureAwait(false);
                owner.Token.ThrowIfCancellationRequested();
                Publish(View with { AvailableVersion = candidate, ReadyVersion = updater.ReadyVersion,
                    Status = candidate is null ? "No newer published release is available." : $"Version {candidate} is available." });
            }
            bool download;
            lock (gate) download = downloadOnly || (scheduled && automatic && action is "download" or "install");
            if (candidate is not null && download && updater.ReadyVersion != candidate)
            {
                owner.Token.ThrowIfCancellationRequested();
                Publish(View with { Status = "Downloading and verifying update…" });
                await updater.DownloadAsync(candidate, progress => Publish(View with { Progress = Math.Clamp(progress, 0, 100) }), owner.Token).ConfigureAwait(false);
                owner.Token.ThrowIfCancellationRequested();
                Publish(View with { ReadyVersion = updater.ReadyVersion, Progress = 100,
                    Status = "Update downloaded and verified. Restart clears memory-only conversations." });
            }
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        { Publish(View with { Status = "Update activity stopped. You can check again manually." }); }
        catch (Exception)
        { Publish(View with { Status = "Update service unavailable. Check your connection; private repositories and draft releases are not publicly accessible." }); }
        finally
        {
            lock (gate) { view = view with { Busy = false }; operation = null; automaticOperation = false; }
            owner.Dispose();
            if (!disposed) Changed?.Invoke(View);
        }
    }
    public bool TryRequestRestart(bool manual, TimeSpan quietTime, bool hostCanRestart)
    {
        lock (gate)
        {
            if (disposed || restartRequested || view.Busy || view.ReadyVersion is null || !hostCanRestart) return false;
            if (!manual && (!automatic || action != "install" || quietTime < IdleInterval)) return false;
            restartRequested = true;
            return true;
        }
    }
    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (gate) { if (disposed) return; disposed = true; pending = work; }
        lifetime.Cancel();
        await pending.ConfigureAwait(false);
        lifetime.Dispose();
    }
}
