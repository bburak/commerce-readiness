using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Evaluation.Metrics;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Core.SourceOfTruth;
using CommerceReadiness.DemoStore.Output;

namespace CommerceReadiness.Tests;

public class GroundTruthTests
{
    private static readonly StoreSnapshot Clean = TestData.Seed42.Clean;
    private static readonly StoreSnapshot Published = TestData.Seed42.Published;
    private static readonly GroundTruth Truth = TestData.Seed42.GroundTruth;

    [Fact]
    public void Ground_truth_covers_every_defect_type_and_has_several_decoys()
    {
        Assert.Equal("demostore-v0-seed-42", Truth.StoreVersion);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.FromHours(3)), Truth.SimulationClock);
        Assert.All(Enum.GetValues<DefectType>(), type => Assert.Contains(Truth.Defects, d => d.Type == type));
        Assert.True(Truth.Decoys.Length >= 6);
        Assert.All(Truth.Defects, d => Assert.False(string.IsNullOrWhiteSpace(d.ExpectedFix)));
    }

    [Fact]
    public void Every_defect_is_present_in_the_published_store()
    {
        foreach (var defect in Truth.Defects)
        {
            switch (defect.Type)
            {
                case DefectType.InventoryConflict:
                    Assert.Equal(defect.PublishedValue, Published.GetListing(defect.Sku!).StockStatus.ToCatalogText());
                    break;
                case DefectType.PriceConflict:
                    Assert.Equal(defect.PublishedValue, InvariantFormat.Money(Published.GetListing(defect.Sku!).ListedPrice));
                    break;
                case DefectType.DeliveryConflict:
                    Assert.Equal(defect.PublishedValue, InvariantFormat.Date(Published.GetListing(defect.Sku!).DeliveryPromiseIstanbul!.Value));
                    break;
                case DefectType.MissingRequiredAttribute:
                    Assert.Null(Published.GetProduct(defect.ProductId!).Attributes.Find(defect.Field));
                    break;
                case DefectType.MisleadingDescription:
                    Assert.Contains(defect.PublishedValue!, Published.GetProduct(defect.ProductId!).Description, StringComparison.Ordinal);
                    break;
                case DefectType.AmbiguousReturnRule:
                    Assert.Equal(ReturnAnswer.Unspecified, Published.ReturnRules.Single(r => r.CellId == defect.Policy).Answer);
                    break;
            }
        }
    }

    [Fact]
    public void Published_failures_are_exactly_the_injected_defects()
    {
        var expected = Truth.Defects.SelectMany(d => d.ExpectedImpacts).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected.Count, expected.Distinct().Count());

        AssertFailuresMatch(TestData.Seed42.Metrics.Stores.Single(s => s.Store == StoreKind.Published), expected);

        var directory = TestData.NewTempDirectory();
        DatasetWriter.Write(TestData.Seed42, directory);
        var reread = DatasetReader.ReadStore(Path.Combine(directory, DatasetWriter.PublishedFolder), StoreKind.Published, Truth.StoreVersion);
        AssertFailuresMatch(ReadinessMetrics.Compute(reread, TestData.Seed42Units, Clean, Truth.SemanticFacts), expected);
    }

    [Fact]
    public void Every_defect_has_a_valid_authoritative_counterpart()
    {
        var context = TestData.Seed42.Context;
        foreach (var defect in Truth.Defects)
        {
            Assert.Equal(DefectSource(defect.Type), defect.Source);
            switch (defect.Type)
            {
                case DefectType.InventoryConflict:
                    var available = Clean.GetInventory(defect.Sku!).Available;
                    Assert.Equal($"available={available}", defect.AuthoritativeValue);
                    Assert.NotEqual(available > 0, defect.PublishedValue == StockStatusText.InStock);
                    break;
                case DefectType.PriceConflict:
                    Assert.Equal(InvariantFormat.Money(Clean.GetPrice(defect.Sku!)), defect.AuthoritativeValue);
                    Assert.NotEqual(defect.AuthoritativeValue, defect.PublishedValue);
                    break;
                case DefectType.DeliveryConflict:
                    var calculated = DeliveryCalculator.EstimateDelivery(Clean, defect.Sku!, City.Istanbul, context.Now)!.Value;
                    Assert.Equal(InvariantFormat.Date(calculated), defect.AuthoritativeValue);
                    Assert.True(InvariantFormat.ParseDate(defect.PublishedValue!) < calculated);
                    break;
                case DefectType.MissingRequiredAttribute:
                    Assert.Equal(Clean.GetProduct(defect.ProductId!).Attributes.Find(defect.Field), defect.AuthoritativeValue);
                    break;
                case DefectType.MisleadingDescription:
                    Assert.Equal(Clean.GetProduct(defect.ProductId!).Attributes.Find(defect.Field), defect.AuthoritativeValue);
                    var lie = Truth.SemanticFacts.Single(f =>
                        f.Store == StoreKind.Published && f.ProductId == defect.ProductId && f.AttributeKey == defect.Field);
                    Assert.NotEqual(defect.AuthoritativeValue, lie.AssertedValue);
                    Assert.False(lie.SupportsAuthoritativeValue);
                    Assert.DoesNotContain(lie.Phrase, Clean.GetProduct(defect.ProductId!).Description, StringComparison.Ordinal);
                    break;
                case DefectType.AmbiguousReturnRule:
                    var rule = Clean.ReturnRules.Single(r => r.CellId == defect.Policy);
                    Assert.Equal(rule.Answer.ToString(), defect.AuthoritativeValue);
                    Assert.NotEqual(ReturnAnswer.Unspecified, rule.Answer);
                    break;
            }
        }
    }

    [Fact]
    public void Every_decoy_is_consistent_and_not_a_defect()
    {
        var published = TestData.Seed42.Metrics.Stores.Single(s => s.Store == StoreKind.Published);
        var failed = published.AllNotPassingUnits().ToHashSet(StringComparer.Ordinal);
        var defectUnits = Truth.Defects.SelectMany(d => d.ExpectedImpacts).ToHashSet(StringComparer.Ordinal);

        Assert.All(Truth.Decoys, decoy =>
        {
            var unit = EvaluationUnits.For(decoy);
            Assert.DoesNotContain(unit, failed);
            Assert.DoesNotContain(unit, defectUnits);
            Assert.Equal(DefectSource(decoy.NearMissOf), decoy.Source);
        });

        var allUnits = ReadinessMetrics.CatalogCompleteness(Published, TestData.Seed42Units).Units
            .Concat(ReadinessMetrics.DataConsistency(Published, TestData.Seed42Units, Clean, Truth.SemanticFacts).Units)
            .Select(u => u.UnitId)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(Truth.Decoys, decoy => Assert.Contains(EvaluationUnits.For(decoy), allUnits));
    }

    [Fact]
    public void Decoys_are_unchanged_between_clean_and_published()
    {
        foreach (var decoy in Truth.Decoys)
        {
            if (decoy.Sku is { } sku && decoy.NearMissOf is DefectType.InventoryConflict or DefectType.PriceConflict or DefectType.DeliveryConflict)
            {
                Assert.Equal(Clean.GetListing(sku), Published.GetListing(sku));
            }

            if (decoy.ProductId is { } productId && decoy.NearMissOf is DefectType.MisleadingDescription or DefectType.MissingRequiredAttribute)
            {
                Assert.Equal(Clean.GetProduct(productId).Description, Published.GetProduct(productId).Description);
                Assert.Equal(Clean.GetProduct(productId).Attributes, Published.GetProduct(productId).Attributes);
            }

            if (decoy.Policy is { } policy)
            {
                Assert.Equal(Clean.ReturnRules.Single(r => r.CellId == policy), Published.ReturnRules.Single(r => r.CellId == policy));
            }
        }
    }

    [Fact]
    public void Inventory_is_the_authority_for_stock()
    {
        Assert.Equal(DataSource.Inventory, FieldOwnership.OwnerOf("sku.stock"));
        Assert.Equal(DataSource.Pricing, FieldOwnership.OwnerOf("sku.price"));
        Assert.Equal(DataSource.Shipping, FieldOwnership.OwnerOf("sku.delivery"));

        var facts = new AuthoritativeFacts(Clean, TestData.Seed42.Context.Now);
        var conflicts = Truth.Defects.Where(d => d.Type == DefectType.InventoryConflict).ToList();
        Assert.NotEmpty(conflicts);
        foreach (var conflict in conflicts)
        {
            var sku = conflict.Sku!;
            var inventory = Published.GetInventory(sku);
            Assert.Equal(Clean.GetInventory(sku), inventory);
            Assert.Equal(inventory.Available > 0, facts.IsInStock(sku));
            Assert.NotEqual(Published.GetListing(sku).StockStatus == StockStatus.InStock, facts.IsInStock(sku));
        }
    }

    [Fact]
    public void Published_authoritative_sources_are_untouched()
    {
        Assert.Equal(JsonDefaults.Serialize(Clean.Prices), JsonDefaults.Serialize(Published.Prices));
        Assert.Equal(JsonDefaults.Serialize(Clean.Inventory), JsonDefaults.Serialize(Published.Inventory));
        Assert.Equal(JsonDefaults.Serialize(Clean.ShippingRules), JsonDefaults.Serialize(Published.ShippingRules));
        Assert.Equal(JsonDefaults.Serialize(Clean.Warehouses), JsonDefaults.Serialize(Published.Warehouses));
        Assert.Equal(JsonDefaults.Serialize(Clean.InstallmentRules), JsonDefaults.Serialize(Published.InstallmentRules));
    }

    private static void AssertFailuresMatch(StoreMetrics metrics, List<string> expected)
    {
        var actual = metrics.AllNotPassingUnits().Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected, actual);
    }

    private static DataSource DefectSource(DefectType type) => type switch
    {
        DefectType.InventoryConflict => DataSource.Inventory,
        DefectType.PriceConflict => DataSource.Pricing,
        DefectType.DeliveryConflict => DataSource.Shipping,
        DefectType.AmbiguousReturnRule => DataSource.ReturnPolicy,
        _ => DataSource.Catalog,
    };
}
