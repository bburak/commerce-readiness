using Microsoft.Extensions.Configuration;

namespace CommerceReadiness.Retrieval;

/// <summary>
/// Configuration from environment variables and .NET user-secrets. Secrets are never stored in the repository.
/// ConnectionStrings__RetrievalDb, OPENAI_API_KEY, Retrieval__EmbeddingModel, Retrieval__EmbeddingDimensions.
/// </summary>
public sealed record RetrievalSettings(string ConnectionString, string? OpenAiApiKey, string EmbeddingModel, int EmbeddingDimensions)
{
    public const string DefaultEmbeddingModel = "text-embedding-3-small";
    public const int DefaultEmbeddingDimensions = 1536;

    /// <summary>Matches docker-compose.yml defaults; a local development database, not a secret.</summary>
    public const string DefaultConnectionString =
        "Host=localhost;Port=5433;Database=commerce_readiness;Username=commerce;Password=commerce_dev";

    public static RetrievalSettings Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<RetrievalSettings>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        return FromConfiguration(configuration);
    }

    public static RetrievalSettings FromConfiguration(IConfiguration configuration) => new(
        configuration.GetConnectionString("RetrievalDb") ?? DefaultConnectionString,
        NullIfEmpty(configuration["OPENAI_API_KEY"]),
        NullIfEmpty(configuration["Retrieval:EmbeddingModel"]) ?? DefaultEmbeddingModel,
        int.TryParse(configuration["Retrieval:EmbeddingDimensions"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dimensions)
            ? dimensions
            : DefaultEmbeddingDimensions);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
