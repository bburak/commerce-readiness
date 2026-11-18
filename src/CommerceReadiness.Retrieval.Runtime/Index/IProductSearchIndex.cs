using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Retrieval.Embeddings;

namespace CommerceReadiness.Retrieval.Index;

/// <summary>Documents of one store version embedded with one embedding profile.</summary>
public sealed record IndexPartition(string StoreVersion, string EmbeddingModel, int EmbeddingDimensions)
{
    public static IndexPartition For(string storeVersion, EmbeddingProfile profile) => new(storeVersion, profile.Model, profile.Dimensions);
}

/// <param name="Embedding">New vector, or null to keep the stored one and only refresh the structured fields.</param>
public sealed record IndexEntry(SearchDocument Document, float[]? Embedding);

/// <summary>
/// Shared ranking rule: cosine distance compared at 1e-6 precision, then SKU ordinal. pgvector stores float4 while the
/// reference index accumulates in double, so near-identical documents (sibling variants) would otherwise reorder on
/// floating-point noise.
/// </summary>
public static class IndexOrdering
{
    public const int DistanceDecimals = 6;

    /// <remarks>Rounds half away from zero, like PostgreSQL round(numeric, n).</remarks>
    public static decimal RankingDistance(double similarity) =>
        Math.Round((decimal)(1 - similarity), DistanceDecimals, MidpointRounding.AwayFromZero);
}

/// <summary>
/// The one storage seam of retrieval: pgvector at runtime, an in-memory reference implementation for tests and offline
/// runs. Both apply the same semantics: optional hard filters, then cosine ordering when a query vector is given
/// (otherwise neutral SKU ordering), with ties broken by SKU ordinal.
/// </summary>
public interface IProductSearchIndex
{
    string Name { get; }

    Task<IReadOnlyDictionary<string, string>> GetContentHashesAsync(IndexPartition partition, CancellationToken cancellationToken = default);

    Task UpsertAsync(IndexPartition partition, IReadOnlyList<IndexEntry> entries, CancellationToken cancellationToken = default);

    Task DeleteAsync(IndexPartition partition, IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default);

    /// <param name="filters">Null means no hard filters (SemanticOnly).</param>
    /// <param name="queryEmbedding">Null means no similarity ranking (StructuredOnly).</param>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        IndexPartition partition, StructuredFilters? filters, float[]? queryEmbedding, int top, CancellationToken cancellationToken = default);
}
