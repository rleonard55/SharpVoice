using System.Diagnostics;
using SharpVoice.Core;
using SharpVoice.Platform.Windows;
using SharpVoice.Platform.Windows.Native;
using SharpVoice.Refiner.Auth;
using SharpVoice.UI;

namespace SharpVoice.App;

internal static class StartupDiagnostics
{
    // Same desktop, configuration, auth storage and coordinator as the app, without
    // a second tray icon or competing global hook. Never records or inserts text.
    public static int Run(string output)
    {
        var process = Process.GetCurrentProcess();
        var entryMs = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
        var watch = Stopwatch.StartNew();
        var config = ConfigManager.Load();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        using var auth = new ChatGptAuthManager(http);
        auth.Initialize();
        using var desktop = new NativeDesktop(config);
        desktop.Initialize(showSettings: false, integrateShell: false);
        var desktopMs = watch.Elapsed.TotalMilliseconds;
        using var app = new SharpVoiceApp(config, desktop, http, auth);
        app.Start(null);
        var result = 0;
        var measure = Task.Run(async () =>
        {
            try
            {
                await app.Preparation;
                var preparedMs = watch.Elapsed.TotalMilliseconds;
                if (!app.IsMicrophoneReady) throw new InvalidOperationException("Microphone not ready.");
                process.Refresh();
                var before = process.WorkingSet64 / 1048576d;
                var cpu = process.TotalProcessorTime;
                var idle = Stopwatch.StartNew();
                await Task.Delay(5000);
                process.Refresh();
                var cpuPercent = (process.TotalProcessorTime - cpu).TotalMilliseconds / idle.Elapsed.TotalMilliseconds * 100;
                MemoryTrimmer.Trim();
                await Task.Delay(2000);
                process.Refresh();
                File.WriteAllLines(output, [
                    $"Process entry: {entryMs:F2} ms",
                    $"Configuration/auth/desktop initialized after entry: {desktopMs:F2} ms",
                    $"Microphone/model preparation after entry: {preparedMs:F2} ms",
                    $"Idle capture CPU (one-core percentage, 5 s sample): {cpuPercent:F3}%",
                    $"Working set before trim: {before:F2} MiB",
                    $"Working set 2 s after trim: {process.WorkingSet64 / 1048576d:F2} MiB",
                    $"Private committed memory after trim: {process.PrivateMemorySize64 / 1048576d:F2} MiB",
                    "Whisper weights were not loaded; no audio recorded or saved; shell integration disabled."
                ]);
            }
            catch (Exception ex) { result = 1; File.WriteAllText(output, "Diagnostics failed: " + ex.Message); }
            finally { desktop.Post(() => Win32.PostQuitMessage(0)); }
        });
        desktop.Run();
        measure.GetAwaiter().GetResult();
        return result;
    }
}
