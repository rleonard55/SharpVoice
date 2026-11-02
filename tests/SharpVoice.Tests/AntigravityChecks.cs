using System.Diagnostics;
using SharpVoice.Core;
using SharpVoice.Refiner;

internal static class AntigravityChecks
{
    public static async Task Routing()
    {
        foreach (var mode in new[] { "dictation", "edit", "ask" })
        {
            var ask = mode == "ask";
            var source = mode == "dictation" ? null : "SOURCE_PRIVATE";
            var calls = 0;
            var runner = new Fixture((start, input) =>
            {
                if (start.ArgumentList.Contains("--version")) return new(0, "1.4.12");
                calls++;
                Check(start.ArgumentList.Contains("gemini-3.8-flash-medium"), "Auto must pin efficient Flash.");
                Check(start.ArgumentList.Contains("--disable-slash-commands") && start.ArgumentList.Contains("--agent"), "Text-only agent and slash protection required.");
                Check(!start.ArgumentList.Any(x => x.Contains("PRIVATE")), "Dictation must not appear in process arguments.");
                Check(input is not null && input.Contains("SPOKEN_PRIVATE") && (source is null || input.Contains("SOURCE_PRIVATE")) && (mode == "dictation" || input.Contains(ask ? "spoken request" : "edit selected text")), "Mode/context routing failed.");
                return new(0, Stream("{\"status\":\"SUCCESS\",\"response\":\"Complete answer.\",\"conversation_id\":\"fixture\"}"));
            });
            var client = new AntigravityClient(runner, () => "C:\\fixture\\agy.exe", definition => Check(definition.Contains("excludeDefaultComponents: true") && definition.Contains("tools: []"), "Agent must remove default tools."));
            using var http = new HttpClient();
            var refiner = new UnifiedRefiner(http, new RejectSession(), gemini: client);
            var models = new List<string>(); refiner.ModelResolved += models.Add;
            var answer = await refiner.RefineAsync("SPOKEN_PRIVATE", "notepad", new AppConfig { Backend = "gemini_antigravity", MinimumAiWords = mode == "dictation" ? 0 : 8 }, _ => { }, default, source, askAi: ask);
            Check(answer == "Complete answer." && calls == 1 && models.Last() == AntigravityClient.DefaultModel, "Gemini output/model routing failed.");
        }
    }
    public static Task Isolation()
    {
        var folder = Directory.CreateTempSubdirectory("SharpVoice-Google-Isolation-");
        try
        {
            AntigravityClient.PrepareAgentAt(folder.FullName, AntigravityClient.AgentDefinition);
            AntigravityClient.PrepareAgentAt(folder.FullName, AntigravityClient.AgentDefinition);
            var target = Path.Combine(folder.FullName, "config", "agents", "sharpvoice-text-only.md");
            Check(File.ReadAllText(target) == AntigravityClient.AgentDefinition, "Agent definition was altered.");
            File.WriteAllText(target, "user-modified");
            var rejected = false;
            try { AntigravityClient.PrepareAgentAt(folder.FullName, AntigravityClient.AgentDefinition); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && File.ReadAllText(target) == "user-modified", "User agent modifications must not be overwritten or executed.");
            File.Delete(target);
            foreach (var name in new[] { "hooks.json", "mcp_config.json", "plugins.json" })
            {
                var path = Path.Combine(folder.FullName, "config", name);
                File.WriteAllText(path, "{\"enabled\":true}");
                rejected = false;
                try { AntigravityClient.PrepareAgentAt(folder.FullName, AntigravityClient.AgentDefinition); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected && !File.Exists(target), "Global customization must fail before preparing a request.");
                File.Delete(path);
            }
            foreach (var relative in new[] { "config/agents/sharpvoice-text-only/agent.md", "config/rules/rule.md", "config/skills/test/SKILL.md", "antigravity-cli/skills/test/SKILL.md", "skills/test/SKILL.md", "GEMINI.md", "config/AGENTS.md" })
            {
                var path = Path.Combine(folder.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "customization");
                rejected = false;
                try { AntigravityClient.PrepareAgentAt(folder.FullName, AntigravityClient.AgentDefinition); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "A global customization source was not refused: " + relative);
                File.Delete(path);
                var parent = Path.GetDirectoryName(path)!;
                while (parent != folder.FullName && !Directory.EnumerateFileSystemEntries(parent).Any()) { Directory.Delete(parent); parent = Path.GetDirectoryName(parent)!; }
            }
            var plugin = Path.Combine(folder.FullName, "config", "plugins", "fixture");
            Directory.CreateDirectory(plugin);
            File.WriteAllText(Path.Combine(plugin, "plugin.json"), "{\"name\":\"fixture\"}");
            AntigravityClient.PrepareAgentAt(folder.FullName, AntigravityClient.AgentDefinition);
            File.WriteAllText(Path.Combine(plugin, "HOOKS.JSON"), "{}");
            rejected = false;
            try { AntigravityClient.PrepareAgentAt(folder.FullName, AntigravityClient.AgentDefinition); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Plugin hooks must be refused.");
            File.Delete(Path.Combine(plugin, "HOOKS.JSON"));
            var sharedConfig = Path.Combine(folder.FullName, "config", "config.json");
            File.WriteAllText(sharedConfig, "{\"sidecars\":{\"fixture\":{\"enabled\":true}}}");
            rejected = false;
            try { AntigravityClient.PrepareAgentAt(folder.FullName, AntigravityClient.AgentDefinition); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Enabled sidecars must be refused.");
            File.WriteAllText(sharedConfig, "{\"sidecars\":true}");
            rejected = false;
            try { AntigravityClient.PrepareAgentAt(folder.FullName, AntigravityClient.AgentDefinition); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Unknown sidecar shape must fail closed.");
            File.Delete(sharedConfig);
            Directory.CreateDirectory(Path.Combine(folder.FullName, "antigravity-cli"));
            var settings = Path.Combine(folder.FullName, "antigravity-cli", "settings.json");
            File.WriteAllText(settings, "{\"modelProvider\":\"gemini\"}");
            rejected = false;
            try { AntigravityClient.PrepareAgentAt(folder.FullName, AntigravityClient.AgentDefinition); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "API-key mode must not silently change billing.");
        }
        finally { folder.Delete(true); }
        return Task.CompletedTask;
    }
    public static async Task Failures()
    {
        foreach (var output in new[] { "{}", "garbage", "{\"status\":\"RUNNING\",\"response\":\"partial\"}", "{\"status\":\"SUCCESS\",\"response\":\"\"}", "{\"status\":\"SUCCESS\",\"response\":\"partial\",\"denied_actions\":[\"command\"]}" })
        {
            var client = new AntigravityClient(new Fixture((s, _) => s.ArgumentList.Contains("--version") ? new(0, "1.4.12") : new(0, Stream(output))), () => "C:\\fixture\\agy.exe", _ => { });
            var failed = false;
            try { await client.RefineAsync("input", "instructions", "auto", _ => throw new Exception("Invalid output previewed"), _ => { }, default); }
            catch (InvalidDataException) { failed = true; }
            Check(failed, "Incomplete or denied result accepted.");
        }
        var old = new AntigravityClient(new Fixture((_, _) => new(0, "1.0.0")), () => "C:\\fixture\\agy.exe", _ => { });
        Check((await old.GetStatusAsync(default)).Contains("Update"), "Old CLI needs setup guidance.");
        var missing = new AntigravityClient(new Fixture((_, _) => throw new Exception("Missing executable invoked")), () => null, _ => { });
        Check((await missing.GetStatusAsync(default)).Contains("Install"), "Missing CLI needs setup guidance.");
    }
    private static string Stream(string result) => "{\"event\":\"init\",\"init\":{\"agent\":\"sharpvoice-text-only\",\"model\":\"gemini-3.8-flash-medium\"}}\n{\"event\":\"result\",\"result\":" + result + "}\n";
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private sealed class Fixture(Func<ProcessStartInfo, string?, ClaudeCodeProcessResult> respond) : IClaudeCodeProcessRunner
    {
        public Task<ClaudeCodeProcessResult> RunAsync(ProcessStartInfo start, string? input, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(respond(start, input)); }
    }
    private sealed class RejectSession : SharpVoice.Refiner.Auth.IChatGptSessionProvider
    {
        public Task<SharpVoice.Refiner.Auth.ChatGptSession> GetSessionAsync(CancellationToken ct) => throw new Exception("Gemini must not use ChatGPT auth.");
    }
}
