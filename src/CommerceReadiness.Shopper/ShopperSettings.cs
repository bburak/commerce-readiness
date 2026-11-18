using CommerceReadiness.Retrieval;
using CommerceReadiness.Shopper.Agent;
using Microsoft.Extensions.Configuration;

namespace CommerceReadiness.Shopper;

/// <param name="ReasoningEffort">low, medium or high; null leaves the provider default.</param>
public sealed record ShopperSettings(string Model, string PromptVersion, int MaxToolIterations, int MaxToolCalls, string? ReasoningEffort)
{
    public const string DefaultModel = "gpt-6.2-sol";
    public const int DefaultMaxToolIterations = 8;
    public const int DefaultMaxToolCalls = 20;

    /// <summary>Fixed: one consecutive-exception limit owned by the function-invocation layer.</summary>
    public const int MaxConsecutiveErrors = 3;

    public static ShopperSettings Default { get; } = new(DefaultModel, ShopperPrompt.Version, DefaultMaxToolIterations, DefaultMaxToolCalls, null);
}

/// <summary>
/// The Shopper composition root's configuration: environment variables plus the user-secrets store it shares with the
/// Retrieval CLI. Shopper__Model, Shopper__PromptVersion, Shopper__MaxToolIterations, Shopper__MaxToolCalls,
/// Shopper__ReasoningEffort, plus the retrieval keys so the embedding profile matches the indexed partition.
/// </summary>
public static class ShopperConfiguration
{
    public static (RetrievalSettings Retrieval, ShopperSettings Shopper) Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(ShopperConfiguration).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        return (RetrievalFrom(configuration), ShopperFrom(configuration));
    }

    public static RetrievalSettings RetrievalFrom(IConfiguration configuration) => new(
        configuration.GetConnectionString("RetrievalDb") ?? RetrievalSettings.DefaultConnectionString,
        NullIfEmpty(configuration["OPENAI_API_KEY"]),
        NullIfEmpty(configuration["Retrieval:EmbeddingModel"]) ?? RetrievalSettings.DefaultEmbeddingModel,
        Int(configuration["Retrieval:EmbeddingDimensions"]) ?? RetrievalSettings.DefaultEmbeddingDimensions);

    public static ShopperSettings ShopperFrom(IConfiguration configuration)
    {
        var settings = new ShopperSettings(
            NullIfEmpty(configuration["Shopper:Model"]) ?? ShopperSettings.DefaultModel,
            NullIfEmpty(configuration["Shopper:PromptVersion"]) ?? ShopperPrompt.Version,
            Int(configuration["Shopper:MaxToolIterations"]) ?? ShopperSettings.DefaultMaxToolIterations,
            Int(configuration["Shopper:MaxToolCalls"]) ?? ShopperSettings.DefaultMaxToolCalls,
            NullIfEmpty(configuration["Shopper:ReasoningEffort"]));
        return Validate(settings);
    }

    public static ShopperSettings Validate(ShopperSettings settings)
    {
        if (settings.PromptVersion != ShopperPrompt.Version)
        {
            throw new InvalidOperationException($"Unknown prompt version '{settings.PromptVersion}'; this build ships {ShopperPrompt.Version}.");
        }

        if (settings.MaxToolIterations is < 1 or > 20 || settings.MaxToolCalls is < 1 or > 50)
        {
            throw new InvalidOperationException("Shopper:MaxToolIterations must be 1-20 and Shopper:MaxToolCalls 1-50.");
        }

        if (settings.ReasoningEffort is not (null or "low" or "medium" or "high"))
        {
            throw new InvalidOperationException($"Unknown reasoning effort '{settings.ReasoningEffort}'; use low, medium or high.");
        }

        return settings;
    }

    private static int? Int(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
