using System.Text.Json;
using SharpVoice.Core;

internal static class OnboardingChecks
{
    internal static Task Preferences()
    {
        var root = Path.Combine(Path.GetTempPath(), "SharpVoiceSetup-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "config.json");
        try
        {
            var fresh = ConfigManager.Load(path);
            Check(fresh.CleanupPrompt == DefaultPrompts.MasterPrompt && fresh.CustomPrompt == "", "Fresh installs must pre-fill General Instructions with the packaged prompt.");
            Check(!fresh.SetupFinished && fresh.Backend == "none", "Fresh users must be offered setup without requiring AI.");
            ConfigManager.Save(fresh, path);
            Check(!ConfigManager.Load(path).SetupFinished, "An unfinished wizard must remain available after restarting.");
            var finished = fresh with { SetupFinished = true, CleanupPrompt = "Use British English.\n" + DefaultPrompts.MasterPrompt, CustomPrompt = "Preserve this preference.", PillPosition = new(-900, 300) };
            ConfigManager.Save(finished, path);
            Check(ConfigManager.Load(path) == finished, "Skipping/finishing setup must persist without losing preferences.");
            ConfigManager.Save(finished with { CleanupPrompt = "", CustomPrompt = "" }, path);
            Check(ConfigManager.Load(path).CustomPrompt == "" && ConfigManager.Load(path).CleanupPrompt == "", "An explicitly cleared General Instructions field must stay empty.");
            ConfigManager.Save(finished, path);
            Check(finished != finished with { SetupFinished = false }, "Setup state must participate in equality.");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject()) if (property.Name != "setup_finished") property.WriteTo(writer);
                writer.WriteEndObject();
            }
            File.WriteAllBytes(path, stream.ToArray());
            var legacy = ConfigManager.Load(path);
            Check(legacy.SetupFinished && legacy.CustomPrompt == finished.CustomPrompt && legacy.PillPosition == finished.PillPosition,
                "Existing users must keep tray startup and their preferences instead of being forced through setup.");
            return Task.CompletedTask;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
