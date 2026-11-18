using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Retrieval.Embeddings;
using CommerceReadiness.Retrieval.Index;
using Microsoft.Extensions.AI;

namespace CommerceReadiness.Retrieval;

/// <param name="Embedded">Documents whose semantic text was new or changed and were sent to the embedding generator.</param>
/// <param name="Reused">Documents whose stored vector was kept; their structured fields were still refreshed.</param>
/// <param name="Deleted">SKUs no longer published for this store version.</param>
public sealed record IndexingReport(int Documents, int Embedded, int Reused, int Deleted);

/// <summary>
/// Idempotent indexing of a published store: only documents with a new or changed content hash are embedded,
/// structured fields (price, quantity) are always refreshed, and SKUs that disappeared are removed.
/// </summary>
public static class ProductIndexer
{
    public static async Task<IndexingReport> IndexAsync(
        StoreSnapshot published,
        EmbeddingSetup embeddings,
        IProductSearchIndex index,
        CancellationToken cancellationToken = default)
    {
        var documents = SearchDocumentBuilder.Build(published);
        var partition = IndexPartition.For(published.StoreVersion, embeddings.Profile);
        var existing = await index.GetContentHashesAsync(partition, cancellationToken);

        var toEmbed = documents
            .Where(d => !existing.TryGetValue(d.Sku, out var hash) || hash != d.ContentHash)
            .ToList();
        var vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        if (toEmbed.Count > 0)
        {
            var generated = await embeddings.Generator.GenerateAsync(
                toEmbed.Select(d => d.SemanticText),
                new EmbeddingGenerationOptions { Dimensions = embeddings.Profile.Dimensions },
                cancellationToken);
            if (generated.Count != toEmbed.Count)
            {
                throw new InvalidOperationException($"Expected {toEmbed.Count} embeddings, received {generated.Count}.");
            }

            for (var i = 0; i < toEmbed.Count; i++)
            {
                var vector = generated[i].Vector.ToArray();
                if (vector.Length != embeddings.Profile.Dimensions)
                {
                    throw new InvalidOperationException(
                        $"Embedding for {toEmbed[i].Sku} has {vector.Length} dimensions; the profile expects {embeddings.Profile.Dimensions}.");
                }

                vectors[toEmbed[i].Sku] = vector;
            }
        }

        await index.UpsertAsync(partition, [.. documents.Select(d => new IndexEntry(d, vectors.GetValueOrDefault(d.Sku)))], cancellationToken);

        var publishedSkus = documents.Select(d => d.Sku).ToHashSet(StringComparer.Ordinal);
        var removed = existing.Keys.Where(sku => !publishedSkus.Contains(sku)).Order(StringComparer.Ordinal).ToList();
        if (removed.Count > 0)
        {
            await index.DeleteAsync(partition, removed, cancellationToken);
        }

        return new IndexingReport(documents.Length, toEmbed.Count, documents.Length - toEmbed.Count, removed.Count);
    }
}
