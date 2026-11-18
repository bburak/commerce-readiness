namespace CommerceReadiness.Retrieval;

/// <summary>
/// Retrieval runtime settings. Executables load them from their own configuration sources; the runtime never reads
/// user-secrets or environment variables itself.
/// </summary>
public sealed record RetrievalSettings(string ConnectionString, string? OpenAiApiKey, string EmbeddingModel, int EmbeddingDimensions)
{
    public const string DefaultEmbeddingModel = "text-embedding-3-small";
    public const int DefaultEmbeddingDimensions = 1536;

    /// <summary>Matches docker-compose.yml defaults; a local development database, not a secret.</summary>
    public const string DefaultConnectionString =
        "Host=localhost;Port=5433;Database=commerce_readiness;Username=commerce;Password=commerce_dev";
}
