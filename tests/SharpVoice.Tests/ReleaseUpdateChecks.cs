using System.Text.Json;
using SharpVoice.Core;
using SharpVoice.App;
using SharpVoice.Platform.Windows;

internal static class ReleaseUpdateChecks
{
    public static Task Preferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SharpVoiceUpdates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "config.json");
            ConfigManager.Save(new AppConfig(), path);
            using var original = JsonDocument.Parse(File.ReadAllText(path));
            string Fixture(bool? enabled, string? action) => ConnectedJson.Build(writer =>
            {
                writer.WriteStartObject();
                foreach (var field in original.RootElement.EnumerateObject())
                    if (field.Name is not ("automatic_updates" or "update_action")) { writer.WritePropertyName(field.Name); field.Value.WriteTo(writer); }
                if (enabled is bool value) writer.WriteBoolean("automatic_updates", value);
                if (action is not null) writer.WriteString("update_action", action);
                writer.WriteEndObject();
            });
            using (var saved = JsonDocument.Parse(File.ReadAllText(path)))
            {
                if (!saved.RootElement.GetProperty("automatic_updates").GetBoolean()) throw new Exception("Automatic updates must default On.");
                if (saved.RootElement.GetProperty("update_action").GetString() != "install") throw new Exception("Automatic action must default Install When Idle.");
            }
            File.WriteAllText(path, Fixture(null, null));
            var legacy = ConfigManager.Load(path);
            if (!legacy.AutomaticUpdates || legacy.UpdateAction != "install") throw new Exception("Missing update preferences must migrate to On and Install When Idle.");
            File.WriteAllText(path, Fixture(false, "notify"));
            var config = ConfigManager.Load(path);
            ConfigManager.Save(config, path);
            using var roundTrip = JsonDocument.Parse(File.ReadAllText(path));
            if (roundTrip.RootElement.GetProperty("automatic_updates").GetBoolean() || roundTrip.RootElement.GetProperty("update_action").GetString() != "notify")
                throw new Exception("Disabling automatic updates must preserve its independent Notify action.");
            File.WriteAllText(path, Fixture(true, "unsupported"));
            try { ConfigManager.Load(path); throw new Exception("Unknown update action accepted."); }
            catch (ArgumentException) { }
        }
        finally { Directory.Delete(directory, true); }
        return Task.CompletedTask;
    }

    public static async Task Policy()
    {
        foreach (var enabled in new[] { false, true })
        foreach (var action in new[] { "notify", "download", "install" })
        {
            var backend = new FakeUpdater();
            await using var updates = new ReleaseUpdateCoordinator(backend, enabled, action);
            await updates.CheckAsync(scheduled: true);
            Check(backend.Checks == (enabled ? 1 : 0), "Scheduled checks ignored enablement.");
            Check(backend.Downloads == (enabled && action != "notify" ? 1 : 0), "Wrong automatic download policy.");
            Check(!updates.TryRequestRestart(false, TimeSpan.FromHours(1), false), "Busy host accepted automatic restart.");
            Check(!updates.TryRequestRestart(false, TimeSpan.FromSeconds(59), true), "Restart did not wait for 60 quiet seconds.");
            var expected = enabled && action == "install";
            Check(updates.TryRequestRestart(false, TimeSpan.FromSeconds(60), true) == expected, "Automatic restart policy changed.");
            Check(!updates.TryRequestRestart(false, TimeSpan.FromSeconds(60), true), "A second restart was requested.");
            Check(backend.Applies == 0, "Coordinator must let the host dispose audio before applying.");
        }
        var manualBackend = new FakeUpdater();
        await using (var updates = new ReleaseUpdateCoordinator(manualBackend, false, "notify"))
        {
            await updates.CheckAsync();
            Check(manualBackend.Checks == 1 && manualBackend.Downloads == 0, "Manual Check must only check even when automatic updates are Off.");
            await updates.DownloadAsync();
            Check(updates.View.ReadyVersion == "0.2.0", "Manual download unavailable when automatic updates are Off.");
            Check(!updates.TryRequestRestart(true, TimeSpan.Zero, false), "Manual restart interrupted work.");
            Check(updates.TryRequestRestart(true, TimeSpan.Zero, true), "Explicit install should remain available when Off.");
        }
        var pending = new FakeUpdater { ReadyVersion = "0.2.0" };
        await using (var updates = new ReleaseUpdateCoordinator(pending, true, "install"))
        {
            updates.SetPreferences(false, "install");
            Check(!updates.TryRequestRestart(false, TimeSpan.FromHours(1), true), "Turning Off did not block an earlier downloaded update.");
            updates.SetPreferences(true, "download");
            Check(!updates.TryRequestRestart(false, TimeSpan.FromHours(1), true), "Download-only restarted a pending update.");
        }
        var unavailable = new FakeUpdater { Fail = true };
        await using (var updates = new ReleaseUpdateCoordinator(unavailable, true, "install"))
        {
            await updates.CheckAsync();
            Check(!updates.View.Busy && updates.View.Status.Contains("unavailable") && !updates.View.Status.Contains("private diagnostic detail"), "Errors must be recoverable and sanitized.");
        }
    }

    public static async Task Cancellation()
    {
        foreach (var change in new[] { "off", "notify" })
        {
            var backend = new FakeUpdater { WaitForCancel = true };
            await using var updates = new ReleaseUpdateCoordinator(backend, true, "install");
            var work = updates.CheckAsync(scheduled: true);
            await backend.Downloading.Task.WaitAsync(TimeSpan.FromSeconds(5));
            updates.SetPreferences(change != "off", change == "notify" ? "notify" : "install");
            await work.WaitAsync(TimeSpan.FromSeconds(5));
            Check(backend.Canceled && !updates.View.Busy && updates.View.ReadyVersion is null, "Automatic work was not canceled/drained.");
            Check(!updates.TryRequestRestart(false, TimeSpan.FromHours(1), true), "Canceled download requested restart.");
        }
    }

    public static Task WakeEligibility()
    {
        Check(!SharpVoiceApp.WakeAllowsUpdate(false, false, false, null), "Wake preparation must drain before restart.");
        Check(!SharpVoiceApp.WakeAllowsUpdate(true, true, false, TimeSpan.Zero), "Speech before wake activation was considered idle.");
        Check(!SharpVoiceApp.WakeAllowsUpdate(true, true, true, TimeSpan.FromSeconds(5)), "Prefix inference was considered idle.");
        Check(!SharpVoiceApp.WakeAllowsUpdate(false, true, true, null), "Canceled prefix inference must drain even after listening pauses.");
        Check(!SharpVoiceApp.WakeAllowsUpdate(true, true, false, null), "Unknown wake activity was considered idle.");
        Check(SharpVoiceApp.WakeAllowsUpdate(true, true, false, TimeSpan.FromSeconds(3)), "Silent wake listening should permit the subsequent 60-second idle interval.");
        Check(SharpVoiceApp.WakeAllowsUpdate(false, true, false, null), "Paused/off microphone should permit idle updates.");
        return Task.CompletedTask;
    }

    public static Task StableStartup()
    {
        var root = Path.Combine(Path.GetTempPath(), "SharpVoiceLauncher-" + Guid.NewGuid().ToString("N"));
        var current = Path.Combine(root, "current"); Directory.CreateDirectory(current);
        var app = Path.Combine(current, "SharpVoice.exe"); var launcher = Path.Combine(root, "SharpVoice.exe");
        try
        {
            File.WriteAllText(app, "fixture");
            Check(WindowsLoginStartup.StableLauncher(app) == app, "Development current folder was mistaken for an installation.");
            File.WriteAllText(Path.Combine(current, "sq.version"), "fixture");
            File.WriteAllText(Path.Combine(root, "Update.exe"), "fixture");
            Check(WindowsLoginStartup.StableLauncher(app) == app, "Missing execution stub should preserve the actual executable.");
            File.WriteAllText(launcher, "fixture");
            Check(WindowsLoginStartup.StableLauncher(app) == launcher, "Installed startup must use the stable root launcher.");
            Check(WindowsLoginStartup.CommandFor(launcher) == "\"" + launcher + "\" --startup", "Stable launcher lost safe path quoting or startup arguments.");
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class FakeUpdater : IReleaseUpdater
    {
        public bool Supported => true;
        public string CurrentVersion => "0.1.0";
        public string? ReadyVersion { get; set; }
        public int Checks, Downloads, Applies;
        public bool WaitForCancel, Canceled, Fail;
        public TaskCompletionSource Downloading = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string?> CheckAsync(CancellationToken ct)
        {
            Checks++; ct.ThrowIfCancellationRequested();
            if (Fail) throw new IOException("private diagnostic detail");
            return Task.FromResult<string?>("0.2.0");
        }
        public async Task DownloadAsync(string version, Action<int> progress, CancellationToken ct)
        {
            Downloads++; Downloading.TrySetResult();
            if (WaitForCancel)
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { Canceled = true; throw; }
            }
            ct.ThrowIfCancellationRequested(); progress(50); ReadyVersion = version;
        }
        public void ApplyAndRestart() => Applies++;
    }
}
