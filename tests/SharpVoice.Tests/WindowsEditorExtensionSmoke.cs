using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static partial class WindowsEditorExtensionSmoke
{
    internal static unsafe void Run(string vscode, string extension, string report)
    {
        if (File.Exists(report)) File.Delete(report);
        var root = Path.Combine(Path.GetTempPath(), "sharpvoice-vscode-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var profile = Path.Combine(root, "profile"); Directory.CreateDirectory(Path.Combine(profile, "User"));
        File.WriteAllText(Path.Combine(profile, "User", "settings.json"), "{\"telemetry.telemetryLevel\":\"off\",\"update.mode\":\"none\",\"extensions.autoUpdate\":false,\"extensions.autoCheckUpdates\":false,\"workbench.startupEditor\":\"none\",\"security.workspace.trust.enabled\":false}");
        var desktopName = "SharpVoiceCodeTest" + Guid.NewGuid().ToString("N");
        var desktop = CreateDesktopW(desktopName, 0, 0, 0, 0x01FF, 0);
        if (desktop == 0) throw new Exception("Cannot create isolated editor test desktop.");
        nint desktopText = 0, command = 0, environment = 0; ProcessInfo process = default;
        using var output = new FileStream(report + ".startup.log", FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        var outputHandle = output.SafeFileHandle.DangerousGetHandle(); SetHandleInformation(outputHandle, 1, 1);
        try
        {
            var vars = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().ToDictionary(v => (string)v.Key, v => (string?)v.Value ?? "", StringComparer.OrdinalIgnoreCase);
            foreach (var key in new[] { "ELECTRON_RUN_AS_NODE", "VSCODE_IPC_HOOK_CLI", "VSCODE_CWD" }) vars.Remove(key);
            vars["LOCALAPPDATA"] = Path.Combine(root, "appdata"); Directory.CreateDirectory(vars["LOCALAPPDATA"]);
            vars["SHARPVOICE_EDITOR_TEST_REPORT"] = Path.GetFullPath(report);
            var block = string.Join('\0', vars.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase).Select(v => v.Key + "=" + v.Value)) + "\0\0";
            environment = Marshal.StringToHGlobalUni(block); desktopText = Marshal.StringToHGlobalUni("WinSta0\\" + desktopName);
            var args = $"\"{vscode}\" --new-window --disable-extension vscode.github-authentication --disable-extension vscode.github --disable-extension vscode.git --disable-extension github.copilot --disable-extension github.copilot-chat --disable-extension TypeScriptTeam.jsts-chat-features --skip-welcome --skip-release-notes --disable-workspace-trust --disable-gpu --no-sandbox --user-data-dir=\"{profile}\" --extensions-dir=\"{Path.Combine(root, "extensions")}\" --extensionDevelopmentPath=\"{extension}\" --extensionTestsPath=\"{Path.Combine(extension, "test", "host.cjs")}\"";
            command = Marshal.StringToHGlobalUni(args);
            var startup = new StartupInfo { Size = (uint)sizeof(StartupInfo), Desktop = desktopText, Flags = 0x100, Output = outputHandle, Error = outputHandle };
            if (CreateProcessW(0, command, 0, 0, 1, 0x400, environment, 0, ref startup, out process) == 0) throw new Exception("Isolated VS Code launch failed: " + Marshal.GetLastPInvokeError());
            if (WaitForSingleObject(process.Process, 180000) != 0)
            {
                try { using var child = Process.GetProcessById((int)process.Pid); child.Kill(entireProcessTree: true); } catch { }
                throw new Exception("Isolated VS Code test timed out.");
            }
            GetExitCodeProcess(process.Process, out var exit);
            var result = File.Exists(report) ? File.ReadAllText(report) : "FAIL no editor report";
            Console.Write(result);
            if (exit != 0 || result.Contains("FAIL", StringComparison.Ordinal) || !result.Contains("PASS refactor", StringComparison.Ordinal)) throw new Exception("VS Code integration fixture failed (exit " + exit + ").");
        }
        finally
        {
            if (process.Thread != 0) CloseHandle(process.Thread); if (process.Process != 0) CloseHandle(process.Process);
            if (desktopText != 0) Marshal.FreeHGlobal(desktopText); if (command != 0) Marshal.FreeHGlobal(command); if (environment != 0) Marshal.FreeHGlobal(environment);
            CloseDesktop(desktop);
            if (!File.Exists(report))
            {
                var diagnostics = report + ".diagnostics"; Directory.CreateDirectory(diagnostics);
                foreach (var log in Directory.EnumerateFiles(root, "*.log", SearchOption.AllDirectories))
                    try { File.Copy(log, Path.Combine(diagnostics, Path.GetFileName(log)), true); } catch { }
            }
            try { Directory.Delete(root, true); } catch { }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo
    {
        public uint Size; public nint Reserved, Desktop, Title;
        public uint X, Y, Width, Height, XChars, YChars, Fill, Flags; public ushort Show, ReservedSize;
        public nint ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public nint Process, Thread; public uint Pid, Tid; }
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)] private static partial nint CreateDesktopW(string name, nint device, nint mode, uint flags, uint access, nint security);
    [LibraryImport("user32.dll")] private static partial int CloseDesktop(nint desktop);
    [LibraryImport("kernel32.dll", SetLastError = true)] private static partial int CreateProcessW(nint app, nint command, nint processSecurity, nint threadSecurity, int inherit, uint flags, nint environment, nint directory, ref StartupInfo startup, out ProcessInfo process);
    [LibraryImport("kernel32.dll")] private static partial uint WaitForSingleObject(nint handle, uint milliseconds);
    [LibraryImport("kernel32.dll")] private static partial int GetExitCodeProcess(nint process, out uint exit);
    [LibraryImport("kernel32.dll")] private static partial int SetHandleInformation(nint handle, uint mask, uint flags);
    [LibraryImport("kernel32.dll")] private static partial int CloseHandle(nint handle);
}
