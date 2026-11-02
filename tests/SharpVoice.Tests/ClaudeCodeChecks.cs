using System.Diagnostics;
using System.Text.Json;
using SharpVoice.Core;
using SharpVoice.Refiner;

internal static class ClaudeCodeChecks
{
    private const string Success = "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"stop_reason\":\"end_turn\",\"result\":\"First line.\\n\\nSecond stanza.\",\"modelUsage\":{\"claude-haiku-fixture\":{}}}";
    private static readonly string Executable = Path.Combine(Path.GetTempPath(), "claude-fixture.exe");
    public static async Task Routing()
    {
        foreach (var ask in new[] { false, true })
        {
            var calls = 0;
            string? promptFile = null;
            var runner = new Fixture((start, input) =>
            {
                if (start.ArgumentList.Contains("--version")) return new(0, "2.1.293 (Claude Code)");
                if (start.ArgumentList.Contains("status")) return new(0, "{\"loggedIn\":true,\"authMethod\":\"claude.ai\"}");
                calls++;
                Check(!start.UseShellExecute && start.CreateNoWindow && start.RedirectStandardInput, "Inference must run without a shell or visible window.");
                var args = start.ArgumentList.ToArray();
                string Value(string flag) => args[Array.IndexOf(args, flag) + 1];
                foreach (var flag in new[] { "--strict-mcp-config", "--disable-slash-commands", "--no-session-persistence", "--no-chrome" })
                    Check(args.Contains(flag), "Missing isolated-request flag " + flag);
                Check(Value("--tools") == "" && Value("--setting-sources") == "" && Value("--disallowedTools") == "mcp__*", "Tools and external settings must be disabled.");
                Check(Value("--settings").Contains("disableAllHooks") && Value("--mcp-config").Contains("mcpServers"), "Hooks and external MCP servers must not run.");
                Check(Value("--model") == "haiku", "Auto must explicitly choose efficient Haiku.");
                Check(!args.Any(x => x.Contains("SPOKEN_PRIVATE") || x.Contains("SOURCE_PRIVATE")), "Speech/context must use stdin, not arguments.");
                using var body = JsonDocument.Parse(input!);
                Check(body.RootElement.GetProperty(ask ? "request" : "editing_instructions").GetString() == "SPOKEN_PRIVATE", "Spoken intent changed.");
                Check(body.RootElement.GetProperty("source_text").GetString() == "SOURCE_PRIVATE", "Selected context lost.");
                promptFile = Value("--system-prompt-file");
                Check(File.ReadAllText(promptFile).Contains(ask ? "spoken request" : "edit selected text"), "Wrong mode prompt routed to Claude.");
                if (!ask) Check(File.ReadAllText(promptFile).Contains("STYLE_PRIVATE"), "Long preferences lost.");
                Check(!args.Any(x => x.Contains("STYLE_PRIVATE")), "Long preferences must not appear in process arguments.");
                return new(0, Success);
            });
            using var http = new HttpClient(new RejectHttp());
            var models = new List<string>();
            var previews = new List<string>();
            var refiner = new UnifiedRefiner(http, new RejectSessionProvider(), new ClaudeCodeClient(runner, () => Executable));
            refiner.ModelResolved += models.Add;
            var text = await refiner.RefineAsync("SPOKEN_PRIVATE", "notepad", new AppConfig { Backend = "claude_code", CustomPrompt = "STYLE_PRIVATE" + new string('x', 31_000) }, previews.Add, default, "SOURCE_PRIVATE", askAi: ask);
            Check(text == "First line.\n\nSecond stanza." && previews.Single() == text && calls == 1, "Complete Claude text must be returned without wrappers.");
            Check(promptFile is not null && !File.Exists(promptFile), "Temporary instruction file must be removed after completion.");
            Check(models.SequenceEqual(new[] { "haiku", "claude-haiku-fixture" }), "Actual CLI model must replace the requested alias.");
        }
        (new AppConfig { Backend = "claude_code" }).Validate();
        Check(RefinementAuthentication.ActionFor("claude_code", false) == ChatGptAccountAction.SignIn
            && RefinementAuthentication.ActionFor("claude_code", true) == ChatGptAccountAction.ManageAccount, "Claude needs one current account action.");
    }

    public static async Task Failures()
    {
        foreach (var output in new[] { "garbage", "{}", Success.Replace("end_turn", "max_tokens"), Success.Replace("success", "error_max_turns"), Success.Replace("false", "true"), Success.Replace("First line.\\n\\nSecond stanza.", "") })
        {
            var client = Client(new(0, output));
            var rejected = false;
            try { await client.RefineAsync("hello", "answer", "auto", _ => throw new Exception("Invalid output was previewed."), _ => { }, default); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Malformed, incomplete or empty CLI result was accepted.");
        }
        var failed = false;
        try { await Client(new(1, "sensitive stderr and transcript")).RefineAsync("hello", "answer", "auto", _ => { }, _ => { }, default); }
        catch (InvalidOperationException ex) { failed = !ex.Message.Contains("sensitive"); }
        Check(failed, "Failed CLI exit must not return text or expose arbitrary diagnostics.");
        var authCommands = new List<string>();
        var authClient = new ClaudeCodeClient(new Fixture((start, _) =>
        {
            if (start.ArgumentList.Contains("--version")) return new(0, "2.1.293");
            authCommands.Add(start.ArgumentList.Last());
            Check(!start.UseShellExecute, "Authentication must invoke the official executable directly.");
            if (start.ArgumentList.Contains("login"))
                Check(!start.CreateNoWindow && !start.RedirectStandardInput && !start.RedirectStandardOutput && start.StandardOutputEncoding is null,
                    "Anthropic's own interactive login must own the terminal and credentials.");
            return new(0, "");
        }), () => Executable);
        await authClient.SignInAsync(default);
        await authClient.SignOutAsync(default);
        Check(authCommands.SequenceEqual(new[] { "login", "logout" }), "Account actions must use the official auth commands.");
        var missing = new ClaudeCodeClient(new Fixture((_, _) => throw new Exception("Missing executable must not start anything.")), () => null);
        Check(!(await missing.GetStatusAsync(default)).Installed, "Missing native CLI not reported.");
        var old = new ClaudeCodeClient(new Fixture((start, _) =>
        {
            Check(start.ArgumentList.Single() == "--version", "Old CLI must never receive unsupported auth/inference commands.");
            return new(0, "2.0.72 (Claude Code)");
        }), () => Executable);
        Check((await old.GetStatusAsync(default)).Message.Contains("Update"), "Old CLI must show update guidance.");
        foreach (var method in new[] { "none", "api_key", "api_key_helper", "oauth_token", "third_party" })
        {
            var client = new ClaudeCodeClient(new Fixture((start, _) => start.ArgumentList.Contains("--version") ? new(0, "2.1.293")
                : new(0, "{\"loggedIn\":true,\"authMethod\":\"" + method + "\"}")), () => Executable);
            Check(!(await client.GetStatusAsync(default)).SignedIn, "Subscription backend must not silently use other auth/billing methods.");
        }
    }

    public static async Task Processes()
    {
        ProcessStartInfo Start(string mode) => new(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false), StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
            ArgumentList = { "--claude-process-fixture", mode }
        };
        var runner = new ClaudeCodeProcessRunner();
        var result = await runner.RunAsync(Start("echo"), "café 😀\nsecond line", default);
        Check(result.ExitCode == 0 && result.Output == "café 😀\nsecond line", "Subprocess UTF-8 stdin/stdout changed.");
        using var cancel = new CancellationTokenSource(250);
        var cancelled = false;
        try { await runner.RunAsync(Start("wait"), "", cancel.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Owned CLI process must cancel promptly.");
        var bounded = false;
        try { await runner.RunAsync(Start("large"), "", default); }
        catch (InvalidDataException) { bounded = true; }
        Check(bounded, "Excessive output must be bounded and child terminated.");
    }

    private static ClaudeCodeClient Client(ClaudeCodeProcessResult result) => new(new Fixture((start, _) =>
        start.ArgumentList.Contains("--version") ? new(0, "2.1.293") : start.ArgumentList.Contains("status")
        ? new(0, "{\"loggedIn\":true,\"authMethod\":\"claude.ai\"}") : result), () => Executable);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Fixture(Func<ProcessStartInfo, string?, ClaudeCodeProcessResult> run) : IClaudeCodeProcessRunner
    {
        public Task<ClaudeCodeProcessResult> RunAsync(ProcessStartInfo start, string? input, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(run(start, input)); }
    }
    private sealed class RejectHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new Exception("Claude routing must not call ChatGPT HTTP.");
    }
}
