using SharpVoice.Core;
using System.Text.Json;

internal static class PillLayoutChecks
{
    internal static Task Placement()
    {
        Check(PillLayout.Clamp(new(-1500, 500), -1920, 0, 0, 1080) == new PillPosition(-1500, 500), "Valid negative monitor coordinates must be retained.");
        Check(PillLayout.Clamp(new(5000, -900), 0, 40, 1920, 1080) == new PillPosition(1400, 40), "A disconnected monitor must not leave the pill offscreen.");
        Check(PillLayout.Clamp(new(int.MinValue, int.MaxValue), -1920, -1080, 0, 0) == new PillPosition(-1920, -96), "Extreme stored coordinates must clamp without overflow.");
        Check(PillLayout.Clamp(new(100, 100), 0, 0, 300, 60) == new PillPosition(0, 0), "Tiny work areas must keep the pill accessible.");
        return Task.CompletedTask;
    }

    internal static Task Persistence()
    {
        var root = Path.Combine(Path.GetTempPath(), "SharpVoicePillConfig-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "config.json");
        try
        {
            var config = new AppConfig { PillPosition = new(-1300, 480), Backend = "none", CustomPrompt = "Preserve user instructions.", AutomaticUpdates = false };
            ConfigManager.Save(config, path);
            Check(ConfigManager.Load(path) == config, "Position and existing preferences must round-trip together.");
            Check(config != config with { PillPosition = new(100, 200) }, "A position change must participate in configuration equality.");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject()) if (property.Name != "pill_position") property.WriteTo(writer);
                writer.WriteEndObject();
            }
            File.WriteAllBytes(path, bytes.ToArray());
            Check(ConfigManager.Load(path).PillPosition is null, "Legacy settings must retain the default placement.");
            return Task.CompletedTask;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
