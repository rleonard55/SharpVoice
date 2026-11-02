using Microsoft.Win32;

namespace SharpVoice.Platform.Windows;

/// <summary>Manages only this app's per-user Run value, on explicit Settings Apply.</summary>
public sealed class WindowsLoginStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string executable, keyPath, valueName;
    public WindowsLoginStartup() : this(StableLauncher(Environment.ProcessPath ?? ""), RunKey, "SharpVoice") { }
    internal WindowsLoginStartup(string executable, string keyPath, string valueName)
        => (this.executable, this.keyPath, this.valueName) = (executable, keyPath, valueName);

    internal static string StableLauncher(string executable)
    {
        var current = Path.GetDirectoryName(executable);
        if (current is null || !Path.GetFileName(current).Equals("current", StringComparison.OrdinalIgnoreCase)) return executable;
        var root = Path.GetDirectoryName(current);
        if (root is null) return executable;
        var launcher = Path.Combine(root, Path.GetFileName(executable));
        return File.Exists(Path.Combine(current, "sq.version")) && File.Exists(Path.Combine(root, "Update.exe")) && File.Exists(launcher) ? launcher : executable;
    }

    public static void RemoveOwnedRegistration(string executable)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(executable)) return;
        var current = Path.GetDirectoryName(executable);
        if (current is null || !File.Exists(Path.Combine(current, "sq.version"))) return;
        var stable = StableLauncher(executable);
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        var value = key?.GetValue("SharpVoice") as string;
        if (value == CommandFor(executable) || value == CommandFor(stable)) key?.DeleteValue("SharpVoice", throwOnMissingValue: false);
    }

    internal static string CommandFor(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || executable.Any(x => x == '"' || char.IsControl(x))
            || Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Start on sign-in requires the SharpVoice executable. Launch SharpVoice.exe, then enable this option.");
        var command = "\"" + executable + "\" --startup";
        if (command.Length > 260) throw new InvalidOperationException("The SharpVoice path is too long for Windows startup. Move it to a shorter permanent location, then enable this option.");
        return command;
    }

    public void Save(bool enabled, Action savePreferences)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows login startup requires Windows.");
        var command = enabled ? CommandFor(executable) : null;
        if (enabled && !File.Exists(executable)) throw new InvalidOperationException("The SharpVoice executable could not be found. Start on sign-in was not changed.");
        object? previous;
        RegistryValueKind kind;
        using (var read = Registry.CurrentUser.OpenSubKey(keyPath))
        {
            previous = read?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            kind = previous is null ? RegistryValueKind.Unknown : read!.GetValueKind(valueName);
        }
        if ((!enabled && previous is null) || (kind == RegistryValueKind.String && previous is string value && value == command))
        {
            savePreferences();
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        if (enabled) key.SetValue(valueName, command!, RegistryValueKind.String);
        else key.DeleteValue(valueName, throwOnMissingValue: false);
        try { savePreferences(); }
        catch (Exception saveError)
        {
            try
            {
                if (previous is null) key.DeleteValue(valueName, throwOnMissingValue: false);
                else key.SetValue(valueName, previous, kind);
            }
            catch (Exception restoreError)
            {
                throw new AggregateException("Settings could not be saved and the previous startup registration could not be restored. Check Windows Startup Apps.", saveError, restoreError);
            }
            throw;
        }
    }
}
