using System.Runtime.InteropServices;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;

namespace SharpVoice.UI;

public sealed partial class NativeDesktop
{
    [LibraryImport("user32.dll", EntryPoint = "SetFocus")] private static partial nint FocusUpdateAction(nint control);
    private readonly HashSet<nint> updateCardControls = [];
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
        Win32.EnableWindow(Win32.GetDlgItem(toolsWindow, 4704), view.Supported && !view.Busy && (view.AvailableVersion is not null || view.ReadyVersion is not null) ? 1 : 0);
    }
    private void OpenUpdates()
    {
        StartToolsWindow("updates", "Updates");
        updateCardControls.Clear();
        SetWindowPos(toolsWindow, 0, 0, 0, 720, 670, 0x16);
        SetWindowPos(toolsNotice, 0, 24, 58, 656, 42, 0x14);
        Win32.ShowWindow(toolsList, 0);
        toolFields["UpdateStatus"] = ToolControl("STATIC", "", 0, 40, 128, 624, 66);
        updateCardControls.Add(toolFields["UpdateStatus"]);
        updateCardControls.Add(ToolButton("Check Now", 4702, 40, 220, 150));
        updateCardControls.Add(ToolButton("Install And Restart", 4704, 204, 220, 238));
        updateCardControls.Add(ToolButton("Download", 4703, 456, 220, 208));
        PolicyChoice("AutomaticUpdates", "Automatic Updates", 310, ["On", "Off"]);
        Win32.SendMessageW(toolFields["AutomaticUpdates"], 0x14E, config.AutomaticUpdates ? 0u : 1u, 0);
        PolicyChoice("UpdateAction", "When Available", 368,
            ["Notify Only", "Download Automatically", "Install When Idle"]);
        Win32.SendMessageW(toolFields["UpdateAction"], 0x14E, (nuint)Array.IndexOf(new[] { "notify", "download", "install" }, config.UpdateAction), 0);
        ToolButton("Update Details", 4705, 40, 442, 210);
        toolFields["UpdateSummary"] = ToolControl("STATIC", "Updates from GitHub · Installed IDE plugins update with SharpVoice.\r\nRestart waits for active work and clears memory-only conversations.", 0, 40, 490, 624, 76);
        toolFields["UpdateDetails"] = ToolControl("EDIT", "Automatic checks run after startup and every six hours. Install When Idle waits for 60 quiet seconds and closed settings/review windows. Dictation, AI requests, spoken answers and plugin updates finish before restart.\r\n\r\nCheck Now finds an update. Install And Restart downloads and verifies it if needed. Off disables automatic updates; manual actions still work. Installed IDE plugins update with SharpVoice; reload open editors to activate them. Preferences, credentials and downloaded models are preserved. Restart clears memory-only conversations.", 0x00A11844, 40, 484, 624, 88);
        Win32.ShowWindow(toolFields["UpdateDetails"], 0);
        ToolButton("Close", 4205, 40, 594, 158);
        ToolButton("Save Changes", 4701, 484, 594, 180);
        void PolicyChoice(string key, string label, int y, string[] options)
        {
            ToolCombo(key, label, y, options);
            var caption = toolControls[^2];
            SetWindowPos(caption, 0, 40, y + 5, 215, 24, 0x14);
            SetWindowPos(toolFields[key], 0, 270, y, 394, 240, 0x14);
            updateCardControls.Add(caption);
        }
        SetUpdateView(updateView);
        FinishToolsWindow();
        FocusUpdateAction(Win32.GetDlgItem(toolsWindow, 4702));
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
