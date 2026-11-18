using CommerceReadiness.Core.Retrieval;

namespace CommerceReadiness.Retrieval.Index;

/// <summary>Reference implementation of the index semantics; exact cosine search over all stored vectors.</summary>
public sealed class InMemoryProductSearchIndex : IProductSearchIndex
{
    private readonly Dictionary<IndexPartition, SortedDictionary<string, (SearchDocument Document, float[] Embedding)>> _partitions = [];

    public string Name => "memory";

    public Task<IReadOnlyDictionary<string, string>> GetContentHashesAsync(IndexPartition partition, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, string> hashes = Rows(partition).ToDictionary(r => r.Key, r => r.Value.Document.ContentHash, StringComparer.Ordinal);
        return Task.FromResult(hashes);
    }

    public Task UpsertAsync(IndexPartition partition, IReadOnlyList<IndexEntry> entries, CancellationToken cancellationToken = default)
    {
        var rows = Rows(partition);
        foreach (var entry in entries)
        {
            var embedding = entry.Embedding
                ?? (rows.TryGetValue(entry.Document.Sku, out var existing)
                    ? existing.Embedding
                    : throw new InvalidOperationException($"{entry.Document.Sku} has no stored embedding to keep."));
            rows[entry.Document.Sku] = (entry.Document, embedding);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(IndexPartition partition, IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default)
    {
        var rows = Rows(partition);
        foreach (var sku in skus)
        {
            rows.Remove(sku);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        IndexPartition partition, StructuredFilters? filters, float[]? queryEmbedding, int top, CancellationToken cancellationToken = default)
    {
        var candidates = Rows(partition).Values.Where(r => filters is null || filters.Matches(r.Document));
        var ranked = queryEmbedding is null
            ? candidates.Select(r => (r.Document, Similarity: (double?)null)).OrderBy(r => r.Document.Sku, StringComparer.Ordinal)
            : candidates.Select(r => (r.Document, Similarity: (double?)CosineSimilarity(queryEmbedding, r.Embedding)))
                .OrderBy(r => IndexOrdering.RankingDistance(r.Similarity!.Value))
                .ThenBy(r => r.Document.Sku, StringComparer.Ordinal);

        IReadOnlyList<SearchResult> results = [.. ranked.Take(top).Select((r, i) => ToResult(r.Document, i + 1, r.Similarity))];
        return Task.FromResult(results);
    }

    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Vectors have different dimensions.");
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            normA += (double)a[i] * a[i];
            normB += (double)b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    internal static SearchResult ToResult(SearchDocument d, int rank, double? similarity) =>
        new(rank, d.Sku, d.ProductId, d.Title, d.Category, d.PriceTry, d.AvailableQuantity, d.ShoeSize, d.Color, similarity);

    private SortedDictionary<string, (SearchDocument Document, float[] Embedding)> Rows(IndexPartition partition)
    {
        if (!_partitions.TryGetValue(partition, out var rows))
        {
            rows = new SortedDictionary<string, (SearchDocument, float[])>(StringComparer.Ordinal);
            _partitions[partition] = rows;
        }

        return rows;
    }
}
