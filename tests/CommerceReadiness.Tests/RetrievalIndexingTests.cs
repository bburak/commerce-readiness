using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Core.Storage;
using CommerceReadiness.Retrieval;
using CommerceReadiness.Retrieval.Embeddings;
using CommerceReadiness.Retrieval.Index;
using Microsoft.Extensions.AI;

namespace CommerceReadiness.Tests;

public class RetrievalIndexingTests
{
    private static readonly StoreSnapshot Published = TestData.Seed42.Published;

    [Fact]
    public async Task Reindexing_unchanged_documents_does_not_call_the_embedding_generator()
    {
        var generator = new CountingEmbeddingGenerator();
        var embeddings = RetrievalTestSupport.Hashing(generator);
        var index = new InMemoryProductSearchIndex();

        var first = await ProductIndexer.IndexAsync(Published, embeddings, index);
        var second = await ProductIndexer.IndexAsync(Published, embeddings, index);

        Assert.Equal(new IndexingReport(100, 100, 0, 0), first);
        Assert.Equal(new IndexingReport(100, 0, 100, 0), second);
        Assert.Equal(1, generator.Calls);
        Assert.Equal(100, generator.Inputs);
    }

    [Fact]
    public async Task A_changed_document_is_the_only_one_re_embedded()
    {
        var generator = new CountingEmbeddingGenerator();
        var embeddings = RetrievalTestSupport.Hashing(generator);
        var index = new InMemoryProductSearchIndex();
        await ProductIndexer.IndexAsync(Published, embeddings, index);

        var backpack = Published.Products.First(p => p.Category == Category.Backpacks);
        var changed = RetrievalTestSupport.WithProduct(Published, backpack.ProductId, p => p with { Description = p.Description + " Yeni sezon." });
        var report = await ProductIndexer.IndexAsync(changed, embeddings, index);

        Assert.Equal(1, report.Embedded);
        Assert.Equal(101, generator.Inputs);
        var partition = IndexPartition.For(Published.StoreVersion, embeddings.Profile);
        var hashes = await index.GetContentHashesAsync(partition);
        Assert.Equal(SearchDocumentBuilder.Build(changed).Single(d => d.ProductId == backpack.ProductId).ContentHash, hashes[Published.VariantsOf(backpack.ProductId).Single().Sku]);
    }

    [Fact]
    public async Task A_price_change_refreshes_the_row_without_re_embedding()
    {
        var generator = new CountingEmbeddingGenerator();
        var embeddings = RetrievalTestSupport.Hashing(generator);
        var index = new InMemoryProductSearchIndex();
        await ProductIndexer.IndexAsync(Published, embeddings, index);

        var sku = Published.Variants[0].Sku;
        var repriced = Published with { Prices = [.. Published.Prices.Select(p => p.Sku == sku ? p with { PriceTry = 99.90m } : p)] };
        var report = await ProductIndexer.IndexAsync(repriced, embeddings, index);

        Assert.Equal(0, report.Embedded);
        Assert.Equal(1, generator.Calls);
        var results = await ProductSearchService.SearchAsync(
            new SearchRequest(Published.StoreVersion, null, new StructuredFilters { MaxPriceTry = 100m }, SearchMode.StructuredOnly, 10),
            embeddings, index);
        Assert.Equal(sku, Assert.Single(results).Sku);
    }

    [Fact]
    public async Task A_removed_sku_is_no_longer_searchable()
    {
        var embeddings = RetrievalTestSupport.Hashing();
        var index = new InMemoryProductSearchIndex();
        await ProductIndexer.IndexAsync(Published, embeddings, index);

        var removed = Published.Variants.First(v => Published.GetProduct(v.ProductId).Category == Category.Sneakers).Sku;
        var shrunk = Published with
        {
            Variants = [.. Published.Variants.Where(v => v.Sku != removed)],
            Listings = [.. Published.Listings.Where(l => l.Sku != removed)],
        };
        var report = await ProductIndexer.IndexAsync(shrunk, embeddings, index);

        Assert.Equal(1, report.Deleted);
        var hashes = await index.GetContentHashesAsync(IndexPartition.For(Published.StoreVersion, embeddings.Profile));
        Assert.DoesNotContain(removed, hashes.Keys);
        var results = await ProductSearchService.SearchAsync(
            new SearchRequest(Published.StoreVersion, null, new StructuredFilters(), SearchMode.StructuredOnly, 1000), embeddings, index);
        Assert.DoesNotContain(results, r => r.Sku == removed);
        Assert.Equal(99, results.Count);
    }

    [Fact]
    public async Task Embeddings_with_the_wrong_dimension_are_rejected()
    {
        var wrong = new EmbeddingSetup(
            new HashingEmbeddingGenerator(8),
            new EmbeddingProfile(EmbeddingProvider.Hashing, HashingEmbeddingGenerator.ModelId, HashingEmbeddingGenerator.DefaultDimensions));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ProductIndexer.IndexAsync(Published, wrong, new InMemoryProductSearchIndex()));
    }

    [Fact]
    public async Task Indexing_rejects_unsupported_store_schema_versions()
    {
        var dataset = TestData.NewSeed42DatasetDirectory();
        var manifest = Path.Combine(dataset, DatasetLayout.PublishedFolder, StoreFileNames.Manifest);
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal));

        var (code, _, error) = await RetrievalTestSupport.RunCliAsync("index", "--dataset", dataset, "--embeddings", "hashing", "--index", "memory");

        Assert.Equal(2, code);
        Assert.Contains("Unsupported store schema version 2", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hashing_embeddings_are_deterministic_and_normalized()
    {
        var generator = new HashingEmbeddingGenerator();
        var first = await generator.GenerateAsync(["Su itici sırt çantası"]);
        var second = await generator.GenerateAsync(["Su itici sırt çantası"]);
        var other = await generator.GenerateAsync(["Aktif gürültü engelleme"]);

        Assert.Equal(first[0].Vector.ToArray(), second[0].Vector.ToArray());
        Assert.NotEqual(first[0].Vector.ToArray(), other[0].Vector.ToArray());
        Assert.Equal(1.0, Math.Sqrt(first[0].Vector.ToArray().Sum(x => (double)x * x)), 5);
        Assert.Equal(HashingEmbeddingGenerator.DefaultDimensions, first[0].Vector.Length);
    }
}
