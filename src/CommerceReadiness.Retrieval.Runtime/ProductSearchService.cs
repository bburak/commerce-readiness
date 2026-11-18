using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Retrieval.Embeddings;
using CommerceReadiness.Retrieval.Index;
using Microsoft.Extensions.AI;

namespace CommerceReadiness.Retrieval;

/// <summary>Maps a search mode onto the index: which filters apply and whether the query text is embedded.</summary>
public static class ProductSearchService
{
    public static async Task<IReadOnlyList<SearchResult>> SearchAsync(
        SearchRequest request, EmbeddingSetup embeddings, IProductSearchIndex index, CancellationToken cancellationToken = default)
    {
        var partition = IndexPartition.For(request.StoreVersion, embeddings.Profile);
        if (request.Mode == SearchMode.StructuredOnly)
        {
            return await index.SearchAsync(partition, request.Filters, null, request.Top, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(request.QueryText))
        {
            throw new ArgumentException($"{request.Mode} search needs query text.", nameof(request));
        }

        var generated = await embeddings.Generator.GenerateAsync(
            [request.QueryText], new EmbeddingGenerationOptions { Dimensions = embeddings.Profile.Dimensions }, cancellationToken);
        var query = generated[0].Vector.ToArray();
        var filters = request.Mode == SearchMode.Hybrid ? request.Filters : null;
        return await index.SearchAsync(partition, filters, query, request.Top, cancellationToken);
    }
}
