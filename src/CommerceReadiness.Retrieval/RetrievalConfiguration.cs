using Microsoft.Extensions.Configuration;

namespace CommerceReadiness.Retrieval;

/// <summary>
/// Configuration from environment variables and .NET user-secrets. Secrets are never stored in the repository.
/// ConnectionStrings__RetrievalDb, OPENAI_API_KEY, Retrieval__EmbeddingModel, Retrieval__EmbeddingDimensions.
/// </summary>
public static class RetrievalConfiguration
{
    public static RetrievalSettings Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(RetrievalConfiguration).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        return FromConfiguration(configuration);
    }

    public static RetrievalSettings FromConfiguration(IConfiguration configuration) => new(
        configuration.GetConnectionString("RetrievalDb") ?? RetrievalSettings.DefaultConnectionString,
        NullIfEmpty(configuration["OPENAI_API_KEY"]),
        NullIfEmpty(configuration["Retrieval:EmbeddingModel"]) ?? RetrievalSettings.DefaultEmbeddingModel,
        int.TryParse(configuration["Retrieval:EmbeddingDimensions"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dimensions)
            ? dimensions
            : RetrievalSettings.DefaultEmbeddingDimensions);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
