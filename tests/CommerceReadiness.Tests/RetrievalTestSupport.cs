using CommerceReadiness.Core.Domain;
using CommerceReadiness.Retrieval;
using CommerceReadiness.Retrieval.Embeddings;
using CommerceReadiness.Retrieval.Index;
using Microsoft.Extensions.AI;

namespace CommerceReadiness.Tests;

internal static class RetrievalTestSupport
{
    public static EmbeddingSetup Hashing(IEmbeddingGenerator<string, Embedding<float>>? generator = null) =>
        new(generator ?? new HashingEmbeddingGenerator(),
            new EmbeddingProfile(EmbeddingProvider.Hashing, HashingEmbeddingGenerator.ModelId, HashingEmbeddingGenerator.DefaultDimensions));

    public static async Task<InMemoryProductSearchIndex> IndexedSeed42Async(EmbeddingSetup? embeddings = null)
    {
        var index = new InMemoryProductSearchIndex();
        await ProductIndexer.IndexAsync(TestData.Seed42.Published, embeddings ?? Hashing(), index);
        return index;
    }

    public static async Task<(int Code, string Output, string Error)> RunCliAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var settings = new RetrievalSettings(RetrievalSettings.DefaultConnectionString, null, "text-embedding-3-small", 1536);
        var code = await RetrievalCli.RunAsync(args, output, error, settings, TimeProvider.System);
        return (code, output.ToString(), error.ToString());
    }

    public static StoreSnapshot WithProduct(StoreSnapshot store, string productId, Func<Product, Product> change) => store with
    {
        Products = [.. store.Products.Select(p => p.ProductId == productId ? change(p) : p)],
    };
}

/// <summary>Counts embedding calls and inputs so tests can prove what was (not) re-embedded.</summary>
internal sealed class CountingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly HashingEmbeddingGenerator _inner = new();

    public int Calls { get; private set; }

    public int Inputs { get; private set; }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = values.ToList();
        Calls++;
        Inputs += list.Count;
        return await _inner.GenerateAsync(list, options, cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
