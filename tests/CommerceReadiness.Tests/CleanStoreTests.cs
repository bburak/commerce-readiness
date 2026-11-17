using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Evaluation.Metrics;
using CommerceReadiness.Core.Serialization;

namespace CommerceReadiness.Tests;

public class CleanStoreTests
{
    private static readonly StoreSnapshot Clean = TestData.Seed42.Clean;

    [Fact]
    public void Store_has_about_100_skus_split_across_four_categories()
    {
        Assert.Equal(StoreKind.Clean, Clean.Kind);
        Assert.Equal(100, Clean.Variants.Length);

        int Skus(Category category) => Clean.Variants.Count(v => Clean.GetProduct(v.ProductId).Category == category);
        Assert.Equal(25, Skus(Category.Backpacks));
        Assert.Equal(30, Skus(Category.Sneakers));
        Assert.Equal(20, Skus(Category.Headphones));
        Assert.Equal(25, Skus(Category.SmallElectronics));
        Assert.Equal(8, Clean.Products.Count(p => p.Category == Category.Sneakers));
    }

    [Fact]
    public void Clean_store_contains_no_injected_defects()
    {
        var context = TestData.Seed42.Context;
        foreach (var listing in Clean.Listings)
        {
            var inventory = Clean.GetInventory(listing.Sku);
            Assert.Equal(inventory.Available > 0, listing.StockStatus == StockStatus.InStock);
            Assert.Equal(Clean.GetPrice(listing.Sku), listing.ListedPrice);
            Assert.Equal(DeliveryCalculator.EstimateDelivery(Clean, listing.Sku, City.Istanbul, context.Now), listing.DeliveryPromiseIstanbul);
        }

        Assert.DoesNotContain(Clean.ReturnRules, r => r.Answer == ReturnAnswer.Unspecified);

        var cleanFacts = TestData.Seed42.GroundTruth.SemanticFacts.Where(f => f.Store == StoreKind.Clean).ToList();
        Assert.NotEmpty(cleanFacts);
        Assert.All(cleanFacts, f =>
        {
            Assert.True(f.SupportsAuthoritativeValue);
            Assert.True(SemanticSupport.Supports(f, Clean));
            Assert.Contains(f.Phrase, Clean.GetProduct(f.ProductId).Description, StringComparison.Ordinal);
        });

        foreach (var variant in Clean.Variants)
        {
            foreach (var requirement in CategoryRequirements.For(Clean.GetProduct(variant.ProductId).Category))
            {
                Assert.NotNull(StructuredValues.Get(Clean, variant.Sku, requirement));
            }
        }
    }

    [Fact]
    public void Clean_store_scores_exactly_100_percent_on_every_metric()
    {
        var metrics = TestData.Seed42.Metrics.Stores.Single(s => s.Store == StoreKind.Clean);

        foreach (var metric in new[] { metrics.CatalogCompleteness, metrics.SemanticCoverage, metrics.DataConsistency })
        {
            Assert.True(metric.Denominator > 0, $"{metric.Name} has no applicable units.");
            Assert.Equal(metric.Denominator, metric.Numerator);
            Assert.Equal(1.0, metric.Value);
            Assert.Empty(metric.FailedUnits);
        }
    }

    [Fact]
    public void Sneaker_stock_is_tracked_per_sku()
    {
        var sneakerSkus = Clean.Variants.Where(v => Clean.GetProduct(v.ProductId).Category == Category.Sneakers).ToList();

        Assert.Equal(Clean.Variants.Length, Clean.Inventory.Length);
        Assert.All(sneakerSkus, v => Assert.Single(Clean.Inventory, i => i.Sku == v.Sku));
        Assert.All(sneakerSkus, v => Assert.False(string.IsNullOrEmpty(v.Attributes.Find(AttributeKeys.ShoeSize))));
        Assert.Contains(
            sneakerSkus.GroupBy(v => v.ProductId),
            group => group.Select(v => Clean.GetInventory(v.Sku).Available).Distinct().Count() > 1);
        Assert.Contains(
            sneakerSkus.GroupBy(v => v.ProductId),
            group => group.Any(v => Clean.GetInventory(v.Sku).Available == 0) && group.Any(v => Clean.GetInventory(v.Sku).Available > 0));

        var variantConflict = TestData.Seed42.GroundTruth.Defects.First(d =>
            d.Type == DefectType.InventoryConflict && d.PublishedValue == StockStatusText.InStock && sneakerSkus.Any(v => v.Sku == d.Sku));
        var siblings = sneakerSkus.Where(v => v.ProductId == variantConflict.ProductId && v.Sku != variantConflict.Sku).ToList();
        Assert.NotEmpty(siblings);
        Assert.All(siblings, s =>
            Assert.Equal(Clean.GetListing(s.Sku).StockStatus, TestData.Seed42.Published.GetListing(s.Sku).StockStatus));
    }

    [Fact]
    public void Clean_and_published_stores_are_distinct()
    {
        var published = TestData.Seed42.Published;

        Assert.Equal(StoreKind.Published, published.Kind);
        Assert.NotEqual(JsonDefaults.Serialize(Clean with { Kind = StoreKind.Published }), JsonDefaults.Serialize(published));
        Assert.NotEqual(
            ReadinessMetrics.CatalogCompleteness(Clean).Numerator,
            ReadinessMetrics.CatalogCompleteness(published).Numerator);
    }
}
