using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CommerceReadiness.Core.Serialization;

namespace CommerceReadiness.Shopper;

/// <summary>Compact JSON with the repository's conventions (camelCase, string enums) for model-facing payloads.</summary>
public static class ShopperJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonDefaults.Options)
        {
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.MakeReadOnly();
        return options;
    }
}
