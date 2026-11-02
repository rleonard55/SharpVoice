using Microsoft.Win32;
using SharpVoice.Platform.Windows;

internal static class WindowsLoginStartupChecks
{
    public static Task Run()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var path = @"Software\SharpVoice.Tests\LoginStartup-" + Guid.NewGuid().ToString("N");
        var executable = Environment.ProcessPath!;
        var startup = new WindowsLoginStartup(executable, path, "SharpVoice");
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
            key.SetValue("OtherApp", "untouched");
            var saves = 0;
            startup.Save(true, () => saves++);
            Check((string?)key.GetValue("SharpVoice") == "\"" + executable + "\" --startup", "Startup command must quote the executable and request a tray launch.");
            Check(key.GetValueKind("SharpVoice") == RegistryValueKind.String, "Startup command must be a literal string.");
            startup.Save(true, () => saves++);
            startup.Save(false, () => saves++);
            Check(key.GetValue("SharpVoice") is null && saves == 3, "Disable must remove only the app's value and save the preference.");
            Check((string?)key.GetValue("OtherApp") == "untouched", "Other startup values must be preserved.");

            var failed = false;
            try { startup.Save(true, () => throw new IOException("simulated save error")); }
            catch (IOException) { failed = true; }
            Check(failed && key.GetValue("SharpVoice") is null, "Failed save must undo newly enabled startup.");

            const string oldCommand = @"%USERPROFILE%\Old App\SharpVoice.exe";
            key.SetValue("SharpVoice", oldCommand, RegistryValueKind.ExpandString);
            failed = false;
            try { startup.Save(true, () => throw new IOException("simulated save error")); }
            catch (IOException) { failed = true; }
            Check(failed && (string?)key.GetValue("SharpVoice", null, RegistryValueOptions.DoNotExpandEnvironmentNames) == oldCommand
                && key.GetValueKind("SharpVoice") == RegistryValueKind.ExpandString, "Rollback must preserve the exact previous value and kind.");
            failed = false;
            try { startup.Save(false, () => throw new IOException("simulated save error")); }
            catch (IOException) { failed = true; }
            Check(failed && key.GetValue("SharpVoice") is not null, "Failed disable must restore the previous entry.");

            Check(WindowsLoginStartup.CommandFor(@"C:\My Apps\SharpVoice.exe") == "\"C:\\My Apps\\SharpVoice.exe\" --startup", "Spaced paths must be quoted.");
            foreach (var invalid in new[] { "relative.exe", @"C:\dotnet.exe", @"C:\app.dll", "C:\\bad\"name.exe", "C:\\bad\nname.exe", "C:\\" + new string('x', 260) + ".exe" })
            {
                failed = false;
                try { WindowsLoginStartup.CommandFor(invalid); }
                catch (InvalidOperationException) { failed = true; }
                Check(failed, "Unsupported startup command was accepted.");
            }
            var missing = new WindowsLoginStartup(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe"), path, "SharpVoice");
            failed = false;
            try { missing.Save(true, () => throw new Exception("Missing executable must not save preferences.")); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && key.GetValueKind("SharpVoice") == RegistryValueKind.ExpandString, "Missing executable must not mutate registration.");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
        return Task.CompletedTask;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
