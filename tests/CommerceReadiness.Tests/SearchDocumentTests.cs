using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Core.Serialization;

namespace CommerceReadiness.Tests;

public class SearchDocumentTests
{
    private static readonly StoreSnapshot Published = TestData.Seed42.Published;

    [Fact]
    public void Builder_is_deterministic_with_one_document_per_sku()
    {
        var first = SearchDocumentBuilder.Build(Published);
        var second = SearchDocumentBuilder.Build(TestData.Seed42.Published with { });

        Assert.Equal(JsonDefaults.Serialize(first), JsonDefaults.Serialize(second));
        Assert.Equal(Published.Variants.Length, first.Length);
        Assert.Equal(Published.Variants.Select(v => v.Sku).Order(StringComparer.Ordinal), first.Select(d => d.Sku));
    }

    [Fact]
    public void Semantic_text_excludes_volatile_commerce_facts()
    {
        foreach (var document in SearchDocumentBuilder.Build(Published))
        {
            var listing = Published.GetListing(document.Sku);
            var text = document.SemanticText;
            Assert.DoesNotContain(InvariantFormat.Money(Published.GetPrice(document.Sku)), text, StringComparison.Ordinal);
            Assert.DoesNotContain(InvariantFormat.Money(listing.ListedPrice), text, StringComparison.Ordinal);
            Assert.DoesNotContain("TL", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TRY", text, StringComparison.Ordinal);
            Assert.DoesNotContain(StockStatusText.InStock, text, StringComparison.Ordinal);
            Assert.DoesNotContain(StockStatusText.OutOfStock, text, StringComparison.Ordinal);
            Assert.DoesNotContain("stok", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("2026-11", text, StringComparison.Ordinal);
            Assert.DoesNotContain("teslim", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("taksit", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("iade", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Structured_fields_come_from_authoritative_published_sources()
    {
        var documents = SearchDocumentBuilder.Build(Published).ToDictionary(d => d.Sku, StringComparer.Ordinal);
        var priceDefect = TestData.Seed42.GroundTruth.Defects.First(d => d.Type == Core.Evaluation.DefectType.PriceConflict);
        var staleInStock = TestData.Seed42.GroundTruth.Defects.First(d =>
            d.Type == Core.Evaluation.DefectType.InventoryConflict && d.PublishedValue == StockStatusText.InStock);

        Assert.Equal(Published.GetPrice(priceDefect.Sku!), documents[priceDefect.Sku!].PriceTry);
        Assert.NotEqual(Published.GetListing(priceDefect.Sku!).ListedPrice, documents[priceDefect.Sku!].PriceTry);
        Assert.Equal(0, documents[staleInStock.Sku!].AvailableQuantity);
    }

    [Fact]
    public void Content_hash_is_stable_and_changes_only_with_semantic_content()
    {
        var documents = SearchDocumentBuilder.Build(Published);
        var target = documents.First(d => d.Category == Category.Backpacks);
        var changed = RetrievalTestSupport.WithProduct(Published, target.ProductId, p => p with { Description = p.Description + " Yeni sezon." });
        var priceChanged = Published with
        {
            Prices = [.. Published.Prices.Select(p => p.Sku == target.Sku ? p with { PriceTry = p.PriceTry + 100m } : p)],
        };

        Assert.Equal(ContentHash.Of("abc"), ContentHash.Of("abc"));
        Assert.NotEqual(ContentHash.Of("abc"), ContentHash.Of("abd"));
        Assert.Equal(64, target.ContentHash.Length);

        var afterText = SearchDocumentBuilder.Build(changed).ToDictionary(d => d.Sku, StringComparer.Ordinal);
        var afterPrice = SearchDocumentBuilder.Build(priceChanged).ToDictionary(d => d.Sku, StringComparer.Ordinal);
        Assert.NotEqual(target.ContentHash, afterText[target.Sku].ContentHash);
        Assert.All(documents.Where(d => d.ProductId != target.ProductId), d => Assert.Equal(d.ContentHash, afterText[d.Sku].ContentHash));
        Assert.Equal(target.ContentHash, afterPrice[target.Sku].ContentHash);
        Assert.Equal(target.PriceTry + 100m, afterPrice[target.Sku].PriceTry);
    }

    [Fact]
    public void Variant_size_and_color_stay_on_their_own_sku()
    {
        var documents = SearchDocumentBuilder.Build(Published);
        var sneaker = Published.Products.First(p => p.Category == Category.Sneakers);
        var variants = Published.VariantsOf(sneaker.ProductId).ToList();

        foreach (var variant in variants)
        {
            var document = documents.Single(d => d.Sku == variant.Sku);
            var ownLine = $"Varyant: {variant.Attributes[AttributeKeys.ShoeSize]} numara, {variant.Attributes[AttributeKeys.Color]}";
            Assert.Contains(ownLine, document.SemanticText, StringComparison.Ordinal);
            Assert.Single(document.SemanticText.Split('\n'), line => line.StartsWith("Varyant:", StringComparison.Ordinal));
            Assert.Equal(int.Parse(variant.Attributes[AttributeKeys.ShoeSize], CultureInfo.InvariantCulture), document.ShoeSize);
            Assert.Equal(variant.Attributes[AttributeKeys.Color], document.Color);
        }

        var nonSneaker = documents.First(d => d.Category == Category.Backpacks);
        Assert.DoesNotContain("Varyant:", nonSneaker.SemanticText, StringComparison.Ordinal);
        Assert.Null(nonSneaker.ShoeSize);
    }

    [Fact]
    public void Numeric_filter_fields_are_parsed_and_missing_attributes_stay_null()
    {
        var documents = SearchDocumentBuilder.Build(Published).ToDictionary(d => d.Sku, StringComparer.Ordinal);
        var missingWarranty = TestData.Seed42.GroundTruth.Defects.Single(d =>
            d.Type == Core.Evaluation.DefectType.MissingRequiredAttribute && d.Field == AttributeKeys.WarrantyMonths);

        var document = documents[missingWarranty.Sku!];
        Assert.Null(document.WarrantyMonths);
        Assert.DoesNotContain("Garanti:", document.SemanticText, StringComparison.Ordinal);
        Assert.All(documents.Values.Where(d => d.Category == Category.Backpacks), d =>
        {
            Assert.NotNull(d.LaptopSizeIn);
            Assert.NotNull(d.CapacityLiters);
        });
        Assert.All(documents.Values.Where(d => d.Category == Category.Headphones), d => Assert.NotNull(d.BatteryHours));
    }

    [Fact]
    public void Clean_store_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => SearchDocumentBuilder.Build(TestData.Seed42.Clean));
    }
}
