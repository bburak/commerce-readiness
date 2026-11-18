using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Retrieval;
using CommerceReadiness.Retrieval.Embeddings;
using CommerceReadiness.Retrieval.Index;

namespace CommerceReadiness.Tests;

public class RetrievalSearchTests
{
    private static readonly StoreSnapshot Published = TestData.Seed42.Published;
    private static readonly EmbeddingSetup Embeddings = RetrievalTestSupport.Hashing();
    private static readonly ImmutableArray<SearchDocument> Documents = SearchDocumentBuilder.Build(Published);

    [Fact]
    public async Task Category_price_and_stock_filters_are_exact()
    {
        var index = await RetrievalTestSupport.IndexedSeed42Async();
        var staleInStock = TestData.Seed42.GroundTruth.Defects.First(d =>
            d.Type == DefectType.InventoryConflict && d.PublishedValue == StockStatusText.InStock).Sku!;
        var category = Published.ProductOf(staleInStock).Category;

        var filtered = await Structured(index, new StructuredFilters { Category = category, MaxPriceTry = 3000m, InStockOnly = true });
        var unfilteredStock = await Structured(index, new StructuredFilters { Category = category });

        Assert.NotEmpty(filtered);
        Assert.All(filtered, r =>
        {
            Assert.Equal(category, r.Category);
            Assert.True(r.PriceTry <= 3000m);
            Assert.True(r.AvailableQuantity > 0);
        });
        Assert.DoesNotContain(filtered, r => r.Sku == staleInStock);
        Assert.Contains(unfilteredStock, r => r.Sku == staleInStock);
        Assert.Equal(Documents.Count(d => new StructuredFilters { Category = category, MaxPriceTry = 3000m, InStockOnly = true }.Matches(d)), filtered.Count);
    }

    [Fact]
    public async Task Sneaker_size_and_color_filters_match_the_sku()
    {
        var index = await RetrievalTestSupport.IndexedSeed42Async();
        var variant = Published.Variants.First(v => Published.GetProduct(v.ProductId).Category == Category.Sneakers);
        var size = int.Parse(variant.Attributes[AttributeKeys.ShoeSize], CultureInfo.InvariantCulture);
        var color = variant.Attributes[AttributeKeys.Color];

        var results = await Structured(index, new StructuredFilters { Category = Category.Sneakers, ShoeSize = size, Color = color });

        Assert.Contains(results, r => r.Sku == variant.Sku);
        Assert.All(results, r =>
        {
            Assert.Equal(size, r.ShoeSize);
            Assert.Equal(color, r.Color);
        });
    }

    [Fact]
    public async Task Numeric_thresholds_are_filters_and_missing_values_fail_them()
    {
        var index = await RetrievalTestSupport.IndexedSeed42Async();
        var missingWarranty = TestData.Seed42.GroundTruth.Defects.Single(d =>
            d.Type == DefectType.MissingRequiredAttribute && d.Field == AttributeKeys.WarrantyMonths).Sku!;

        var laptop = await Structured(index, new StructuredFilters { Category = Category.Backpacks, MinimumLaptopSizeIn = 15.6m });
        var capacity = await Structured(index, new StructuredFilters { Category = Category.Backpacks, MinimumCapacityLiters = 30m });
        var battery = await Structured(index, new StructuredFilters { Category = Category.Headphones, MinimumBatteryHours = 40 });
        var warranty = await Structured(index, new StructuredFilters { Category = Category.SmallElectronics, MinimumWarrantyMonths = 12 });
        var all = await Structured(index, new StructuredFilters { Category = Category.SmallElectronics });

        var documents = Documents.ToDictionary(d => d.Sku, StringComparer.Ordinal);
        Assert.All(laptop, r => Assert.True(documents[r.Sku].LaptopSizeIn >= 15.6m));
        Assert.All(capacity, r => Assert.True(documents[r.Sku].CapacityLiters >= 30m));
        Assert.All(battery, r => Assert.True(documents[r.Sku].BatteryHours >= 40));
        Assert.DoesNotContain(warranty, r => r.Sku == missingWarranty);
        Assert.Contains(all, r => r.Sku == missingWarranty);
        Assert.Equal(all.Count - 1, warranty.Count);
    }

    [Fact]
    public async Task Structured_only_uses_neutral_sku_ordering_not_price()
    {
        var index = await RetrievalTestSupport.IndexedSeed42Async();
        var filters = new StructuredFilters { Category = Category.Headphones, MaxPriceTry = 5000m };

        var first = await Structured(index, filters);
        var second = await Structured(index, filters);

        Assert.Equal(first.Select(r => r.Sku), second.Select(r => r.Sku));
        Assert.Equal(first.Select(r => r.Sku).Order(StringComparer.Ordinal), first.Select(r => r.Sku));
        Assert.NotEqual(first.OrderBy(r => r.PriceTry).Select(r => r.Sku), first.Select(r => r.Sku));
        Assert.All(first, r => Assert.Null(r.SimilarityScore));
        Assert.Equal(Enumerable.Range(1, first.Count), first.Select(r => r.Rank));
    }

    [Fact]
    public async Task Hybrid_applies_hard_filters_before_vector_ranking()
    {
        var index = await RetrievalTestSupport.IndexedSeed42Async();
        var headphone = Documents.First(d => d.Category == Category.Headphones);
        var filters = new StructuredFilters { Category = Category.Backpacks };

        var semantic = await Search(index, headphone.SemanticText, filters, SearchMode.SemanticOnly, 5);
        var hybrid = await Search(index, headphone.SemanticText, filters, SearchMode.Hybrid, 5);

        Assert.Equal(headphone.Sku, semantic[0].Sku);
        Assert.Contains(semantic, r => r.Category != Category.Backpacks);
        Assert.All(hybrid, r => Assert.Equal(Category.Backpacks, r.Category));
        Assert.Equal(hybrid.Select(r => r.SimilarityScore).OrderByDescending(s => s), hybrid.Select(r => r.SimilarityScore));
    }

    [Fact]
    public async Task Cosine_ties_break_by_sku_ordinal()
    {
        var index = new InMemoryProductSearchIndex();
        var partition = new IndexPartition("tie-test", "controlled", 3);
        var template = Documents[0];
        IndexEntry Entry(string sku, float[] vector) => new(template with { StoreVersion = "tie-test", Sku = sku }, vector);
        await index.UpsertAsync(partition,
        [
            Entry("SKU-C", [1, 0, 0]),
            Entry("SKU-A", [1, 0, 0]),
            Entry("SKU-B", [0.9f, 0.1f, 0]),
            Entry("SKU-D", [0, 1, 0]),
        ]);

        var results = await index.SearchAsync(partition, null, [1, 0, 0], 10);

        Assert.Equal(["SKU-A", "SKU-C", "SKU-B", "SKU-D"], results.Select(r => r.Sku));
        Assert.Equal(1.0, results[0].SimilarityScore!.Value, 6);
        Assert.Equal(0.0, results[3].SimilarityScore!.Value, 6);
    }

    [Fact]
    public async Task Semantic_modes_need_query_text()
    {
        var index = await RetrievalTestSupport.IndexedSeed42Async();

        await Assert.ThrowsAsync<ArgumentException>(() => Search(index, null, new StructuredFilters(), SearchMode.Hybrid, 5));
        await Assert.ThrowsAsync<ArgumentException>(() => Search(index, " ", new StructuredFilters(), SearchMode.SemanticOnly, 5));
    }

    private static Task<IReadOnlyList<SearchResult>> Structured(IProductSearchIndex index, StructuredFilters filters) =>
        Search(index, null, filters, SearchMode.StructuredOnly, 1000);

    private static Task<IReadOnlyList<SearchResult>> Search(IProductSearchIndex index, string? query, StructuredFilters filters, SearchMode mode, int top) =>
        ProductSearchService.SearchAsync(new SearchRequest(Published.StoreVersion, query, filters, mode, top), Embeddings, index);
}
