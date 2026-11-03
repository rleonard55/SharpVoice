using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    private AppUpdateView updateView = new("Development", "Updates are starting…", Supported: false);
    public Action? UpdateCheckRequested { get; set; }
    public Action? UpdateDownloadRequested { get; set; }
    public Action? UpdateInstallRequested { get; set; }
    public Action<bool, string>? UpdatePreferencesChanged { get; set; }
    public bool UpdateRestartBlocked => active || hudMoving || SetupOpen || connectionCancellation is not null || localDownloadCancellation is not null
        || new[] { settingsWindow, toolsWindow, answerWindow, connectedWindow }.Any(window => window != 0 && Win32.IsWindowVisible(window) != 0);
    public void ExitForUpdate() => Win32.PostQuitMessage(0);
    public void QuiesceForUpdate()
    {
        hook?.Dispose(); hook = null;
        VoicePressed = null; Pressed = null; Released = null;
    }
    public unsafe void NotifyUpdate(string version)
    {
        var text = "SharpVoice " + version + " is available. Open General → Updates for details.";
        fixed (char* info = tray.Info) { new Span<char>(info, 256).Clear(); text.AsSpan(0, Math.Min(255, text.Length)).CopyTo(new Span<char>(info, 256)); }
        fixed (char* title = tray.InfoTitle) { new Span<char>(title, 64).Clear(); "SharpVoice Update".AsSpan().CopyTo(new Span<char>(title, 64)); }
        tray.InfoFlags = 1;
        var flags = tray.Flags; tray.Flags = 0x10;
        Win32.Shell_NotifyIconW(1, ref tray); tray.Flags = flags;
    }
    public void SetUpdateView(AppUpdateView view)
    {
        updateView = view;
        var button = Win32.GetDlgItem(settingsWindow, 122);
        if (button != 0) Win32.SetWindowTextW(button, view.ReadyVersion is not null ? "Updates · Ready" : view.AvailableVersion is not null ? "Updates · Available" : "Updates");
        if (toolsKind != "updates") return;
        Win32.SetWindowTextW(toolsNotice, "Installed Version: " + view.CurrentVersion);
        Win32.SetWindowTextW(toolFields["UpdateStatus"], view.Status + (view.Busy && view.Progress > 0 ? $" ({view.Progress}%)" : "")
            + (view.ReadyVersion is null ? "" : "\r\nReady to Install: " + view.ReadyVersion));
        Win32.EnableWindow(Win32.GetDlgItem(toolsWindow, 4702), view.Supported && !view.Busy ? 1 : 0);
        Win32.EnableWindow(Win32.GetDlgItem(toolsWindow, 4703), view.Supported && !view.Busy && view.AvailableVersion is not null && view.ReadyVersion != view.AvailableVersion ? 1 : 0);
        Win32.EnableWindow(Win32.GetDlgItem(toolsWindow, 4704), !view.Busy && view.ReadyVersion is not null ? 1 : 0);
    }
    private void OpenUpdates()
    {
        StartToolsWindow("updates", "SharpVoice · Updates");
        Win32.ShowWindow(toolsList, 0);
        ToolCombo("AutomaticUpdates", "Automatic Updates", 104, ["On", "Off"]);
        Win32.SendMessageW(toolFields["AutomaticUpdates"], 0x14E, config.AutomaticUpdates ? 0u : 1u, 0);
        ToolCombo("UpdateAction", "When an Update Is Found", 190,
            ["Notify Only", "Download Automatically (Install Manually)", "Install When Idle (Automatic Restart)"]);
        Win32.SendMessageW(toolFields["UpdateAction"], 0x14E, (nuint)Array.IndexOf(new[] { "notify", "download", "install" }, config.UpdateAction), 0);
        toolFields["UpdateStatus"] = ToolControl("EDIT", "", 0x00A11844, 40, 298, 760, 108);
        ToolButton("Update Details", 4705, 40, 420, 210);
        toolFields["UpdateSummary"] = ToolControl("STATIC", "Updates from public GitHub Releases · rleonard55/SharpVoice\r\n\r\nAutomatic restart waits for idle. Restart clears memory-only conversations.", 0, 40, 474, 760, 112);
        toolFields["UpdateDetails"] = ToolControl("EDIT", "Automatic checks run after startup and every six hours. Install When Idle waits for 60 quiet seconds and closed settings/review windows, including wake speech and recognition. Dictation, AI requests and spoken answers are never interrupted. Restart clears conversations kept only in memory.\r\n\r\nOff disables scheduled checks and automatic installation. Manual actions still work. Private repositories and draft releases are not visible to this public updater. Preferences, credentials and downloaded models are preserved through updates.", 0x00A11844, 40, 470, 760, 156);
        Win32.ShowWindow(toolFields["UpdateDetails"], 0);
        ToolButton("Check for Updates", 4702, 40, 646, 230);
        ToolButton("Download Update", 4703, 294, 646, 230);
        ToolButton("Install and Restart", 4704, 548, 646, 250);
        ToolButton("Save Changes", 4701, 40, 692, 180);
        ToolButton("Close", 4205, 640, 692, 158);
        SetUpdateView(updateView);
        FinishToolsWindow();
    }
    private void SaveUpdates()
    {
        var enabled = Win32.SendMessageW(toolFields["AutomaticUpdates"], 0x147, 0, 0);
        var selected = (int)Win32.SendMessageW(toolFields["UpdateAction"], 0x147, 0, 0);
        if (enabled is not (0 or 1) || selected is < 0 or > 2) throw new ArgumentException("Choose update enablement and its action.");
        var next = config with { AutomaticUpdates = enabled == 0, UpdateAction = new[] { "notify", "download", "install" }[selected] };
        next.Validate(); ConfigChanged?.Invoke(next); config = next;
        UpdatePreferencesChanged?.Invoke(next.AutomaticUpdates, next.UpdateAction);
        CloseTools();
    }
    private void ToggleUpdateDetails()
    {
        var details = toolFields["UpdateDetails"];
        var showing = Win32.IsWindowVisible(details) != 0;
        Win32.ShowWindow(details, showing ? 0 : 5);
        Win32.ShowWindow(toolFields["UpdateSummary"], showing ? 5 : 0);
        Win32.SetWindowTextW(Win32.GetDlgItem(toolsWindow, 4705), showing ? "Update Details" : "Hide Details");
    }
}
