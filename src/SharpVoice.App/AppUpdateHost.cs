using SharpVoice.Core;
using SharpVoice.UI;

namespace SharpVoice.App;

/// <summary>Checks restart eligibility on the message thread, then lets Main dispose the app before applying.</summary>
internal sealed class AppUpdateHost : IDisposable
{
    private readonly NativeDesktop desktop;
    private readonly SharpVoiceApp app;
    private readonly ReleaseUpdateCoordinator updates;
    private readonly Timer timer;
    private bool automatic, manualRestartPending;
    private long nextCheck = Environment.TickCount64 + 30_000, lastBusy = Environment.TickCount64, readyAt = Environment.TickCount64;
    private string? observedReady, notified;
    private volatile bool disposed;
    private int queued;
    internal bool RestartRequested => updates.RestartRequested;

    internal AppUpdateHost(AppConfig config, NativeDesktop desktop, SharpVoiceApp app, IReleaseUpdater backend)
    {
        this.desktop = desktop; this.app = app; automatic = config.AutomaticUpdates;
        updates = new(backend, config.AutomaticUpdates, config.UpdateAction);
        updates.Changed += view => desktop.Post(() => { if (!disposed) Render(view); });
        desktop.UpdateCheckRequested = () => { _ = updates.CheckAsync(); Render(updates.View); };
        desktop.UpdateDownloadRequested = () => { _ = updates.DownloadAsync(); Render(updates.View); };
        desktop.UpdateInstallRequested = () => { if (!manualRestartPending) { _ = InstallAndRestartAsync(); Render(updates.View); } };
        desktop.UpdatePreferencesChanged = (enabled, action) =>
        {
            automatic = enabled; updates.SetPreferences(enabled, action);
            nextCheck = Environment.TickCount64 + 30_000; lastBusy = Environment.TickCount64;
            Render(updates.View);
        };
        Render(updates.View);
        timer = new Timer(_ =>
        {
            if (disposed || Interlocked.Exchange(ref queued, 1) != 0) return;
            desktop.Post(() => { Interlocked.Exchange(ref queued, 0); if (!disposed) Tick(); });
        }, null, 1000, 1000);
    }
    private async Task InstallAndRestartAsync()
    {
        if (!await updates.PrepareInstallAsync().ConfigureAwait(false)) return;
        desktop.Post(() =>
        {
            if (disposed) return;
            manualRestartPending = true;
            RequestRestart(manual: true, TimeSpan.Zero);
        });
    }
    private void Render(AppUpdateView view)
    {
        desktop.SetUpdateView(view);
        if (view.ReadyVersion != observedReady) { observedReady = view.ReadyVersion; readyAt = Environment.TickCount64; }
    }
    private void Tick()
    {
        var now = Environment.TickCount64;
        if (app.UpdateIdle && (updates.View.ReadyVersion ?? updates.View.AvailableVersion) is { } version && version != notified)
        { notified = version; desktop.NotifyUpdate(version); }
        if (!app.UpdateIdle || desktop.UpdateRestartBlocked) lastBusy = now;
        if (!manualRestartPending && automatic && now >= nextCheck && !updates.View.Busy)
        {
            nextCheck = now + (long)TimeSpan.FromHours(6).TotalMilliseconds;
            _ = updates.CheckAsync(scheduled: true);
        }
        var quiet = TimeSpan.FromMilliseconds(Math.Max(0, now - Math.Max(Math.Max(lastBusy, readyAt), app.LastUpdateActivity)));
        RequestRestart(manual: manualRestartPending, quiet);
    }
    private void RequestRestart(bool manual, TimeSpan quiet)
    {
        var canRestart = app.UpdateIdle && (manual || !desktop.UpdateRestartBlocked);
        if (!updates.TryRequestRestart(manual, quiet, canRestart))
        {
            if (manual) desktop.SetUpdateView(updates.View with { Status = "Update ready. Waiting for active dictation, AI work or spoken answers to finish before restarting." });
            return;
        }
        manualRestartPending = false;
        app.QuiesceForUpdate();
        desktop.ExitForUpdate();
    }
    public void Dispose()
    {
        disposed = true; timer.Dispose();
        desktop.UpdateCheckRequested = desktop.UpdateDownloadRequested = desktop.UpdateInstallRequested = null;
        desktop.UpdatePreferencesChanged = null;
        updates.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
