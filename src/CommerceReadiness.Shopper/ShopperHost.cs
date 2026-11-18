using CommerceReadiness.Core.Storage;
using CommerceReadiness.Retrieval;
using CommerceReadiness.Retrieval.Embeddings;
using CommerceReadiness.Retrieval.Index;
using CommerceReadiness.Shopper.Tools;

namespace CommerceReadiness.Shopper;

/// <summary>
/// Opens the published store and a Hybrid retrieval index for one dataset. Hashing keeps offline tests free of
/// OpenAI; OpenAI embeddings match the Milestone 2 indexed partition for real runs.
/// </summary>
public sealed class ShopperHost : IAsyncDisposable
{
    private ShopperHost(LoadedStore published, EmbeddingSetup embeddings, IProductSearchIndex index, IAsyncDisposable? owned)
    {
        Published = published;
        Embeddings = embeddings;
        Index = index;
        Tools = new CommerceTools(published, embeddings, index);
        _owned = owned;
    }

    private readonly IAsyncDisposable? _owned;

    public LoadedStore Published { get; }

    public EmbeddingSetup Embeddings { get; }

    public IProductSearchIndex Index { get; }

    public CommerceTools Tools { get; }

    public static async Task<ShopperHost> OpenAsync(
        string datasetDirectory, RetrievalSettings retrieval, EmbeddingProvider embeddings, string indexKind, CancellationToken cancellationToken = default)
    {
        var published = PublishedStore.Load(datasetDirectory);
        var setup = EmbeddingGeneratorFactory.Create(embeddings, retrieval);
        IProductSearchIndex index;
        IAsyncDisposable? owned = null;
        if (indexKind == "memory")
        {
            index = new InMemoryProductSearchIndex();
        }
        else if (indexKind == "pgvector")
        {
            var pg = new PgVectorProductSearchIndex(retrieval.ConnectionString);
            await pg.InitializeAsync(cancellationToken);
            index = pg;
            owned = pg;
        }
        else
        {
            throw new ArgumentException($"Unknown index '{indexKind}'; use pgvector or memory.");
        }

        await ProductIndexer.IndexAsync(published.Store, setup, index, cancellationToken);
        return new ShopperHost(published, setup, index, owned);
    }

    public ValueTask DisposeAsync() => _owned?.DisposeAsync() ?? ValueTask.CompletedTask;
}
