using System.Text.Json;
using System.Text.Json.Serialization;

namespace LLama.Abstractions;

// Typed metadata preserves converter options without runtime reflection.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(float[]))]
[JsonSerializable(typeof(MetadataOverrideConverter.KeyTypeValue))]
internal partial class ModelParamsJsonContext : JsonSerializerContext
{
    // JsonSerializer supplies frozen options to converters. A context must bind
    // its own mutable copy instead of modifying/rebinding the caller's instance.
    internal static ModelParamsJsonContext For(JsonSerializerOptions options) => new(new JsonSerializerOptions(options));
}
