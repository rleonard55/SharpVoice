using System.Diagnostics;
using System.Text.Json;

namespace SharpVoice.Refiner;

public sealed record ClaudeCodeStatus(bool Installed, bool SignedIn, string Message);
public sealed record ClaudeCodeProcessResult(int ExitCode, string Output);

public interface IClaudeCodeProcessRunner
{
    Task<ClaudeCodeProcessResult> RunAsync(ProcessStartInfo start, string? input, CancellationToken ct);
}

/// <summary>The official native CLI owns OAuth. SharpVoice never reads or stores Claude credentials.</summary>
public sealed class ClaudeCodeClient(IClaudeCodeProcessRunner? runner = null, Func<string?>? findExecutable = null)
{
    private readonly IClaudeCodeProcessRunner processRunner = runner ?? new ClaudeCodeProcessRunner();
    private readonly Func<string?> locate = findExecutable ?? FindExecutable;
    private string? verifiedExecutable;
    public static readonly Version MinimumVersion = new(2, 1, 293);

    public async Task<ClaudeCodeStatus> GetStatusAsync(CancellationToken ct)
    {
        if (locate() is null) return new(false, false, "Install native Claude Code, then sign in with your Claude subscription.");
        try
        {
            var executable = await RequireExecutableAsync(ct);
            var result = await processRunner.RunAsync(Start(executable, ["auth", "status"]), null, ct);
            using var document = JsonDocument.Parse(result.Output);
            var root = document.RootElement;
            var signedIn = result.ExitCode == 0 && root.TryGetProperty("loggedIn", out var loggedIn) && loggedIn.ValueKind == JsonValueKind.True
                && root.TryGetProperty("authMethod", out var method) && method.GetString() == "claude.ai";
            return new(true, signedIn, signedIn ? "Claude subscription signed in through Claude Code." : "Sign in through Claude Code with your Claude subscription.");
        }
        catch (InvalidOperationException ex) { return new(true, false, ex.Message); }
        catch (JsonException) { return new(true, false, "Could not read Claude Code status. Update Claude Code and retry."); }
    }

    public async Task SignInAsync(CancellationToken ct)
    {
        var executable = await RequireExecutableAsync(ct);
        var start = Start(executable, ["auth", "login"]);
        // Let Anthropic's own terminal/browser flow handle all credentials and prompts.
        start.CreateNoWindow = false;
        start.StandardInputEncoding = start.StandardOutputEncoding = start.StandardErrorEncoding = null;
        start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = false;
        var result = await processRunner.RunAsync(start, null, ct);
        if (result.ExitCode != 0) throw new InvalidOperationException("Claude Code sign-in did not complete. Nothing was changed in SharpVoice.");
    }

    public async Task SignOutAsync(CancellationToken ct)
    {
        var executable = await RequireExecutableAsync(ct);
        var result = await processRunner.RunAsync(Start(executable, ["auth", "logout"]), null, ct);
        if (result.ExitCode != 0) throw new InvalidOperationException("Claude Code could not sign out. Retry in Claude Code.");
    }

    public async Task<string> RefineAsync(string input, string instructions, string model, Action<string> preview, Action<string> modelResolved, CancellationToken ct)
    {
        var executable = await RequireExecutableAsync(ct);
        var status = await GetStatusAsync(ct);
        if (!status.SignedIn) throw new InvalidOperationException(status.Message + " Nothing was inserted.");
        var selectedModel = model == "auto" ? "haiku" : model;
        modelResolved(selectedModel);
        var result = await RunRequestAsync(executable, input, instructions, selectedModel, ct);
        ct.ThrowIfCancellationRequested();
        if (result.ExitCode != 0) throw new InvalidOperationException("Claude Code could not complete the request. Check its sign-in, model access and usage limits. Nothing was inserted.");
        string text;
        try
        {
            using var document = JsonDocument.Parse(result.Output);
            var root = document.RootElement;
            if (root.GetProperty("type").GetString() != "result" || root.GetProperty("subtype").GetString() != "success"
                || root.GetProperty("is_error").ValueKind != JsonValueKind.False
                || root.GetProperty("stop_reason").GetString() != "end_turn")
                throw new InvalidDataException("Claude Code did not return a complete answer. Nothing was inserted.");
            text = root.GetProperty("result").GetString() ?? "";
            if (text.Length > 200_000 || string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Claude Code returned empty or excessive text. Nothing was inserted.");
            if (root.TryGetProperty("modelUsage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                var models = usage.EnumerateObject().Select(x => x.Name).ToArray();
                if (models.Length is > 0 and <= 8 && models.All(x => x.Length <= 256)) modelResolved(string.Join(", ", models));
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new InvalidDataException("Claude Code returned an unsupported response. Update Claude Code and retry. Nothing was inserted."); }
        ct.ThrowIfCancellationRequested();
        preview(text);
        return text.Trim();
    }

    private async Task<ClaudeCodeProcessResult> RunRequestAsync(string executable, string input, string instructions, string model, CancellationToken ct)
    {
        var directory = Directory.CreateTempSubdirectory("SharpVoice-Claude-");
        try
        {
            // Keep long mode preferences out of the Windows command-line limit. Speech/context never goes into this file.
            var prompt = Path.Combine(directory.FullName, "instructions.txt");
            await File.WriteAllTextAsync(prompt, instructions, new System.Text.UTF8Encoding(false), ct);
            var start = Start(executable,
            [
                "--print", "--output-format", "json", "--input-format", "text", "--model", model,
                "--system-prompt-file", prompt, "--tools", "", "--disallowedTools", "mcp__*",
                "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}", "--setting-sources", "",
                "--settings", "{\"disableAllHooks\":true}", "--disable-slash-commands", "--no-chrome", "--no-session-persistence"
            ]);
            start.WorkingDirectory = directory.FullName;
            return await processRunner.RunAsync(start, input, ct);
        }
        finally
        {
            try { directory.Delete(recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<string> RequireExecutableAsync(CancellationToken ct)
    {
        var executable = locate() ?? throw new InvalidOperationException("Install native Claude Code and sign in with your Claude subscription to use this backend.");
        if (verifiedExecutable == executable) return executable;
        var result = await processRunner.RunAsync(Start(executable, ["--version"]), null, ct);
        var word = result.Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (result.ExitCode != 0 || !Version.TryParse(word, out var version) || version < MinimumVersion)
            throw new InvalidOperationException($"Update Claude Code to {MinimumVersion} or newer (run claude update), then retry.");
        verifiedExecutable = executable;
        return executable;
    }

    public static string? FindExecutable()
    {
        var native = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        if (File.Exists(native)) return native;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = directory.Trim(' ', '"');
            if (!Path.IsPathFullyQualified(path)) continue;
            var candidate = Path.Combine(path, "claude.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static ProcessStartInfo Start(string executable, string[] args)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetTempPath(),
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
            StandardInputEncoding = new System.Text.UTF8Encoding(false)
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        // This backend is explicitly subscription-based; inherited API keys/proxies must not silently change billing/provider.
        foreach (var key in start.Environment.Keys.Where(x => x.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase)
            || x.StartsWith("CLAUDE_CODE_USE_", StringComparison.OrdinalIgnoreCase) || x is "CLAUDE_CODE_OAUTH_TOKEN" or "CLAUDE_CODE_SIMPLE").ToArray()) start.Environment.Remove(key);
        start.Environment["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] = "1";
        start.Environment["CLAUDE_CODE_SKIP_PROMPT_HISTORY"] = "1";
        return start;
    }
}

/// <summary>Owns only its child process; drains both pipes concurrently and kills it on cancellation/failure.</summary>
public sealed class ClaudeCodeProcessRunner(string providerName = "Claude Code") : IClaudeCodeProcessRunner
{
    public async Task<ClaudeCodeProcessResult> RunAsync(ProcessStartInfo start, string? input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var child = new Process { StartInfo = start };
        try { if (!child.Start()) throw new InvalidOperationException($"Could not start {providerName}."); }
        catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException($"Could not start native {providerName}. Check its installation."); }
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var output = "";
        async Task ReadOutput()
        {
            try { output = await ReadBoundedAsync(child.StandardOutput, 2_000_000, abort.Token); }
            catch { abort.Cancel(); throw; }
        }
        async Task DrainError()
        {
            try { var buffer = new char[2048]; while (await child.StandardError.ReadAsync(buffer, abort.Token) != 0) { } }
            catch { abort.Cancel(); throw; }
        }
        async Task WriteInput()
        {
            try { if (input is not null) await child.StandardInput.WriteAsync(input.AsMemory(), abort.Token); child.StandardInput.Close(); }
            catch { abort.Cancel(); throw; }
        }
        var tasks = new List<Task> { child.WaitForExitAsync(abort.Token) };
        if (start.RedirectStandardOutput) tasks.Add(ReadOutput());
        if (start.RedirectStandardError) tasks.Add(DrainError());
        if (start.RedirectStandardInput) tasks.Add(WriteInput());
        try { await Task.WhenAll(tasks).WaitAsync(abort.Token); }
        catch
        {
            abort.Cancel();
            try { if (!child.HasExited) child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { }
            ct.ThrowIfCancellationRequested();
            var failed = tasks.FirstOrDefault(x => x.IsFaulted);
            if (failed?.Exception?.InnerException is InvalidDataException invalid) throw invalid;
            throw new IOException($"{providerName} stopped before returning a complete response. Nothing was inserted.");
        }
        return new(child.ExitCode, output);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken ct)
    {
        var text = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, ct)) != 0)
        {
            if (text.Length + count > limit) throw new InvalidDataException("Claude Code response exceeded the size limit. Nothing was inserted.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
