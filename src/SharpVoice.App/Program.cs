using System.Net;
using SharpVoice.Core;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.Refiner.Auth;
using SharpVoice.UI;
using Velopack;

namespace SharpVoice.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("SharpVoice runs on Windows 10/11 x64."); return 1; }
        VelopackApp.Build().SetAutoApplyOnStartup(false)
            .OnBeforeUninstallFastCallback(_ => SharpVoice.Platform.Windows.WindowsLoginStartup.RemoveOwnedRegistration(Environment.ProcessPath ?? ""))
            .Run();
        if (args is ["--release-info", var output]) return ReleaseDiagnostics.Write(output);
        if (args is ["--verify-bundled-model", var modelReport]) return ReleaseDiagnostics.VerifyBundledModel(modelReport);
        if (args is ["--diagnostics", var report]) return StartupDiagnostics.Run(report);
        using var singleInstance = new Mutex(true, @"Local\SharpVoice.Prototype", out var firstInstance);
        if (!firstInstance) { if (args is not ["--startup"]) Win32.MessageBoxW(0, "SharpVoice is already running. Look for its icon in the system tray.", "SharpVoice", 0x40); return 0; }
        try
        {
            var config = ConfigManager.Load();
            if (!File.Exists(AppPaths.Config)) ConfigManager.Save(config);
            using var updater = new GitHubReleaseUpdater();
            // RunDesktop returns only after audio, workers, auth refresh and HTTP have drained.
            if (RunDesktop(config, updater)) updater.ApplyAndRestart();
            return 0;
        }
        catch (Exception ex)
        {
            Win32.MessageBoxW(0, ex.Message + "\n\nConfiguration: " + AppPaths.Config, "SharpVoice could not start", 0x10);
            return 1;
        }
    }

    private static bool RunDesktop(AppConfig config, GitHubReleaseUpdater updater)
    {
        using var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true, PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            ConnectTimeout = TimeSpan.FromSeconds(20), AutomaticDecompression = DecompressionMethods.All
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SharpVoice/0.1.0");
        using var auth = new ChatGptAuthManager(http);
        string? warning = null;
        try { auth.Initialize(); }
        catch { warning = "Saved ChatGPT session could not be read. Sign out and sign in again."; }
        if (updater.Supported && config.StartOnLogin)
        {
            try { new SharpVoice.Platform.Windows.WindowsLoginStartup().Save(true, () => { }); }
            catch { warning ??= "Start on sign-in could not be refreshed. Review General settings."; }
        }
        using var desktop = new NativeDesktop(config);
        desktop.Initialize(showSettings: false);
        using var app = new SharpVoiceApp(config, desktop, http, auth);
        using var updates = new AppUpdateHost(config, desktop, app, updater);
        app.Start(warning);
        desktop.Run();
        return updates.RestartRequested;
    }
}
