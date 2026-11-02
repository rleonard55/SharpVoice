using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LLama.Abstractions;

internal static class LlamaConverterChecks
{
    public static void Metadata()
    {
        if (JsonSerializer.IsReflectionEnabledByDefault) throw new Exception("Regression requires JSON reflection disabled.");
        var converter = new MetadataOverrideConverter();
        foreach (var source in new[] { new MetadataOverride("count", 7), new MetadataOverride("rate", 1.25f), new MetadataOverride("enabled", true) })
        {
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes)) converter.Write(writer, source, new());
            var json = bytes.ToArray();
            var reader = new Utf8JsonReader(json); reader.Read();
            var value = converter.Read(ref reader, typeof(MetadataOverride), new());
            using var second = new MemoryStream();
            using (var writer = new Utf8JsonWriter(second)) converter.Write(writer, value, new());
            if (!json.AsSpan().SequenceEqual(second.ToArray())) throw new Exception("Metadata round-trip changed its type, key or value.");
        }
        // Real serializer calls pass frozen options into converters; repeated operations
        // must work without rebinding or modifying that shared options instance.
        var context = new LlamaConverterTestJsonContext(new() { PropertyNameCaseInsensitive = true });
        var original = new MetadataOverride("reused", 11);
        var serialized = JsonSerializer.Serialize(original, context.MetadataOverride);
        for (var i = 0; i < 2; i++)
        {
            var restored = JsonSerializer.Deserialize(serialized, context.MetadataOverride)!;
            if (restored.Key != original.Key) throw new Exception("Serializer metadata round-trip changed the key.");
        }
        var lowercase = JsonSerializer.Deserialize("{\"type\":0,\"key\":\"case\",\"value\":11}", context.MetadataOverride)!;
        if (lowercase.Key != "case") throw new Exception("Serializer options were not preserved through the converter.");
        // The upstream reader honors serializer naming and case options.
        var camel = new Utf8JsonReader(Encoding.UTF8.GetBytes("{\"type\":0,\"key\":\"count\",\"value\":9}")); camel.Read();
        var decoded = converter.Read(ref camel, typeof(MetadataOverride), new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        if (decoded.Key != "count") throw new Exception("Metadata naming policy was ignored.");
        var insensitive = new Utf8JsonReader(Encoding.UTF8.GetBytes("{\"TYPE\":0,\"KEY\":\"count\",\"VALUE\":9}")); insensitive.Read();
        if (converter.Read(ref insensitive, typeof(MetadataOverride), new() { PropertyNameCaseInsensitive = true }).Key != "count")
            throw new Exception("Metadata case-insensitive option was ignored.");
    }
    public static void TensorSplits()
    {
        var root = SharpVoice.Core.NativeRuntimeBundle.Ensure() ?? throw new Exception("No native bundle.");
        LLama.Native.NativeLibraryConfig.All.WithLibrary(Path.Combine(root, "llama", "llama.dll"), Path.Combine(root, "llama", "mtmd.dll"))
            .WithCuda(false).WithVulkan(false).WithAutoFallback(false);
        var converter = new TensorSplitsCollectionConverter();
        var reader = new Utf8JsonReader("[0.25,0.75]"u8); reader.Read();
        var decoded = converter.Read(ref reader, typeof(TensorSplitsCollection), new());
        if (decoded[0] != 0.25f || decoded[1] != 0.75f) throw new Exception("Tensor split values changed.");
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
            converter.Write(writer, decoded, new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.WriteAsString });
        using var document = JsonDocument.Parse(bytes.ToArray());
        if (document.RootElement[0].GetString() != "0.25") throw new Exception("Tensor writer ignored number handling.");
        var quoted = new Utf8JsonReader("[\"0.5\"]"u8); quoted.Read();
        var fromString = converter.Read(ref quoted, typeof(TensorSplitsCollection), new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString });
        if (fromString[0] != 0.5f) throw new Exception("Tensor reader ignored number handling.");
        var context = new LlamaConverterTestJsonContext(new() { NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString });
        var serialized = JsonSerializer.Serialize(decoded, context.TensorSplitsCollection);
        for (var i = 0; i < 2; i++)
        {
            var restored = JsonSerializer.Deserialize(serialized, context.TensorSplitsCollection)!;
            if (restored[0] != 0.25f || restored[1] != 0.75f) throw new Exception("Serializer tensor round-trip changed values.");
        }
        var nil = new Utf8JsonReader("null"u8); nil.Read();
        if (converter.Read(ref nil, typeof(TensorSplitsCollection), new()).Any(x => x != 0)) throw new Exception("Null tensor splits were not zero-filled.");
    }

}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(MetadataOverride))]
[JsonSerializable(typeof(TensorSplitsCollection))]
internal partial class LlamaConverterTestJsonContext : JsonSerializerContext;
