using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Retrieval;
using CommerceReadiness.Retrieval.Index;

namespace CommerceReadiness.Tests;

/// <summary>Runs only when RETRIEVAL_TEST_DB points at a pgvector database (docker compose up -d).</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "RETRIEVAL_TEST_DB";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
        {
            Skip = $"Set {ConnectionVariable} to a pgvector connection string (see .env.example) to run integration tests.";
        }
    }

    public static string ConnectionString => Environment.GetEnvironmentVariable(ConnectionVariable)!;
}

[Trait("Category", "Integration")]
public class PgVectorIntegrationTests
{
    [PostgresFact]
    public async Task Schema_initialization_is_idempotent()
    {
        await using var index = new PgVectorProductSearchIndex(PostgresFactAttribute.ConnectionString);

        await index.InitializeAsync();
        await index.InitializeAsync();

        Assert.Empty(await index.GetContentHashesAsync(new IndexPartition($"it-{Guid.NewGuid():N}", "none", 3)));
    }

    [PostgresFact]
    public async Task Controlled_vectors_come_back_in_exact_cosine_order_like_the_reference_index()
    {
        await using var pg = new PgVectorProductSearchIndex(PostgresFactAttribute.ConnectionString);
        await pg.InitializeAsync();
        var memory = new InMemoryProductSearchIndex();
        var partition = new IndexPartition($"it-{Guid.NewGuid():N}", "controlled", 3);
        var template = SearchDocumentBuilder.Build(TestData.Seed42.Published)[0] with { StoreVersion = partition.StoreVersion };
        IReadOnlyList<IndexEntry> entries =
        [
            new(template with { Sku = "SKU-C" }, [1, 0, 0]),
            new(template with { Sku = "SKU-A" }, [1, 0, 0]),
            new(template with { Sku = "SKU-B" }, [0.8f, 0.6f, 0]),
            new(template with { Sku = "SKU-E" }, [0, 0, 1]),
            new(template with { Sku = "SKU-D" }, [0, 1, 0]),
        ];
        await pg.UpsertAsync(partition, entries);
        await memory.UpsertAsync(partition, entries);

        var fromPg = await pg.SearchAsync(partition, null, [1, 0, 0], 10);
        var fromMemory = await memory.SearchAsync(partition, null, [1, 0, 0], 10);

        Assert.Equal(["SKU-A", "SKU-C", "SKU-B", "SKU-D", "SKU-E"], fromPg.Select(r => r.Sku));
        Assert.Equal(fromMemory.Select(r => r.Sku), fromPg.Select(r => r.Sku));
        Assert.Equal(0.8, fromPg[2].SimilarityScore!.Value, 5);
        await pg.DeleteAsync(partition, [.. entries.Select(e => e.Document.Sku)]);
    }

    [PostgresFact]
    public async Task Seed_42_filters_and_rankings_match_the_reference_index()
    {
        await using var pg = new PgVectorProductSearchIndex(PostgresFactAttribute.ConnectionString);
        await pg.InitializeAsync();
        var storeVersion = $"it-{Guid.NewGuid():N}";
        var published = TestData.Seed42.Published with { StoreVersion = storeVersion };
        var embeddings = RetrievalTestSupport.Hashing();
        var memory = new InMemoryProductSearchIndex();
        await ProductIndexer.IndexAsync(published, embeddings, pg);
        await ProductIndexer.IndexAsync(published, embeddings, memory);

        SearchRequest[] requests =
        [
            new(storeVersion, null, new StructuredFilters { Category = Category.Backpacks, MaxPriceTry = 3000m, InStockOnly = true }, SearchMode.StructuredOnly, 50),
            new(storeVersion, null, new StructuredFilters { Category = Category.Sneakers, ShoeSize = 42 }, SearchMode.StructuredOnly, 50),
            new(storeVersion, null, new StructuredFilters { Category = Category.SmallElectronics, MinimumWarrantyMonths = 12 }, SearchMode.StructuredOnly, 50),
            new(storeVersion, "yağmurda kullanılabilecek laptop çantası", new StructuredFilters(), SearchMode.SemanticOnly, 10),
            new(storeVersion, "yağmurda kullanılabilecek laptop çantası",
                new StructuredFilters { Category = Category.Backpacks, InStockOnly = true, MinimumLaptopSizeIn = 15.6m }, SearchMode.Hybrid, 10),
            new(storeVersion, "dış sesi kesen kulaklık", new StructuredFilters { Category = Category.Headphones, MinimumBatteryHours = 25 }, SearchMode.Hybrid, 10),
        ];

        foreach (var request in requests)
        {
            var fromPg = await ProductSearchService.SearchAsync(request, embeddings, pg);
            var fromMemory = await ProductSearchService.SearchAsync(request, embeddings, memory);
            Assert.Equal(TieGroups(fromMemory), TieGroups(fromPg));
            Assert.Equal(
                fromMemory.Select(r => (r.Sku, r.PriceTry, r.AvailableQuantity, r.ShoeSize, r.Color)).Order(),
                fromPg.Select(r => (r.Sku, r.PriceTry, r.AvailableQuantity, r.ShoeSize, r.Color)).Order());
        }

        await pg.DeleteAsync(IndexPartition.For(storeVersion, embeddings.Profile), [.. published.Variants.Select(v => v.Sku)]);
    }

    [PostgresFact]
    public async Task Reindexing_against_postgres_is_idempotent_and_removes_unpublished_skus()
    {
        await using var pg = new PgVectorProductSearchIndex(PostgresFactAttribute.ConnectionString);
        await pg.InitializeAsync();
        var published = TestData.Seed42.Published with { StoreVersion = $"it-{Guid.NewGuid():N}" };
        var generator = new CountingEmbeddingGenerator();
        var embeddings = RetrievalTestSupport.Hashing(generator);

        var first = await ProductIndexer.IndexAsync(published, embeddings, pg);
        var second = await ProductIndexer.IndexAsync(published, embeddings, pg);
        var removed = published.Variants[^1].Sku;
        var third = await ProductIndexer.IndexAsync(published with { Variants = published.Variants.RemoveAt(published.Variants.Length - 1) }, embeddings, pg);

        Assert.Equal(new IndexingReport(100, 100, 0, 0), first);
        Assert.Equal(new IndexingReport(100, 0, 100, 0), second);
        Assert.Equal(new IndexingReport(99, 0, 99, 1), third);
        Assert.Equal(100, generator.Inputs);
        var partition = IndexPartition.For(published.StoreVersion, embeddings.Profile);
        var hashes = await pg.GetContentHashesAsync(partition);
        Assert.DoesNotContain(removed, hashes.Keys);
        await pg.DeleteAsync(partition, [.. hashes.Keys]);
    }

    /// <summary>Consecutive results with scores within 1e-6 form one group: pgvector stores float4 while C# accumulates in double.</summary>
    private static List<string> TieGroups(IReadOnlyList<SearchResult> results)
    {
        var groups = new List<List<SearchResult>>();
        foreach (var result in results)
        {
            if (groups.Count > 0 && result.SimilarityScore is { } score && groups[^1][^1].SimilarityScore is { } previous
                && Math.Abs(score - previous) < 1e-6)
            {
                groups[^1].Add(result);
            }
            else
            {
                groups.Add([result]);
            }
        }

        return [.. groups.Select(g => string.Join(",", g.Select(r => r.Sku).Order(StringComparer.Ordinal)))];
    }
}
