using CommerceReadiness.Core.Analysis;
using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Storage;

namespace CommerceReadiness.Tests;

public class AnalyzerRuleTests
{
    private static readonly StoreSnapshot Clean = TestData.Seed42.Clean;
    private static readonly DateTimeOffset AsOf = TestData.Seed42.Context.Now;

    [Theory]
    [InlineData(DefectType.MissingRequiredAttribute)]
    [InlineData(DefectType.InventoryConflict)]
    [InlineData(DefectType.PriceConflict)]
    [InlineData(DefectType.DeliveryConflict)]
    [InlineData(DefectType.AmbiguousReturnRule)]
    public void Analyzer_detects_every_seed_42_defect_of_a_supported_type(DefectType type)
    {
        var defects = TestData.Seed42.GroundTruth.Defects.Where(d => d.Type == type).ToList();
        var matched = TestData.Seed42Evaluation.Matches.Select(m => m.DefectId).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(defects);
        Assert.All(defects, d => Assert.Contains(d.DefectId, matched));
    }

    [Fact]
    public void Analyzer_intentionally_does_not_detect_misleading_descriptions()
    {
        var misleading = TestData.Seed42.GroundTruth.Defects.Where(d => d.Type == DefectType.MisleadingDescription).ToList();
        var findingKeys = TestData.Seed42Analysis.Findings.Select(AnalyzerEvaluator.KeyOf).ToHashSet();

        Assert.NotEmpty(misleading);
        Assert.DoesNotContain(nameof(DefectType.MisleadingDescription), Enum.GetNames<FindingType>());
        Assert.All(misleading, d => Assert.DoesNotContain(AnalyzerEvaluator.KeyOf(d), findingKeys));
        Assert.All(misleading, d => Assert.Contains(TestData.Seed42Evaluation.DeferredDefects, u => u.DefectId == d.DefectId));
        Assert.Contains("DescriptionSemanticConsistency", TestData.Seed42Analysis.Summary.NotAnalyzed);
    }

    [Fact]
    public void No_decoy_is_flagged()
    {
        var findingKeys = TestData.Seed42Analysis.Findings.Select(AnalyzerEvaluator.KeyOf).ToHashSet();

        Assert.Equal(8, TestData.Seed42.GroundTruth.Decoys.Length);
        Assert.All(TestData.Seed42.GroundTruth.Decoys, d => Assert.DoesNotContain(AnalyzerEvaluator.KeyOf(d), findingKeys));
        Assert.Equal(0, TestData.Seed42Evaluation.DecoyHitCount);
    }

    [Fact]
    public void A_consistent_store_produces_no_findings()
    {
        var analysis = ReadinessAnalyzer.Analyze(Clean with { Kind = StoreKind.Published }, AsOf);

        Assert.Empty(analysis.Findings);
    }

    [Fact]
    public void Every_finding_carries_evidence_a_fix_and_the_v0_severity()
    {
        Assert.All(TestData.Seed42Analysis.Findings, f =>
        {
            Assert.NotEmpty(f.Evidence);
            Assert.False(string.IsNullOrWhiteSpace(f.Summary));
            Assert.False(string.IsNullOrWhiteSpace(f.SuggestedFix));
            Assert.Equal(FindingSeverities.For(f.Type), f.Severity);
            Assert.StartsWith("FND-", f.FindingId, StringComparison.Ordinal);
        });
        Assert.Equal(FindingSeverity.High, FindingSeverities.For(FindingType.InventoryConflict));
        Assert.Equal(FindingSeverity.Medium, FindingSeverities.For(FindingType.AmbiguousReturnRule));
    }

    [Fact]
    public void A_stale_in_stock_row_with_a_promise_is_an_inventory_conflict_not_a_delivery_conflict()
    {
        var sku = Clean.Inventory.First(i => i.Available == 0).Sku;
        var promise = DeliveryCalculator.EstimateFromWarehouse(Clean, Clean.GetInventory(sku).WarehouseId, City.Istanbul, AsOf);
        var observed = WithListing(sku, l => l with { StockStatus = StockStatus.InStock, DeliveryPromiseIstanbul = promise });

        var findings = ReadinessAnalyzer.Analyze(observed, AsOf).Findings;

        var finding = Assert.Single(findings);
        Assert.Equal(FindingType.InventoryConflict, finding.Type);
        Assert.Equal(sku, finding.Sku);
    }

    [Fact]
    public void A_missing_delivery_promise_alone_is_not_a_finding()
    {
        var sku = Clean.Inventory.First(i => i.Available > 0).Sku;
        var observed = WithListing(sku, l => l with { DeliveryPromiseIstanbul = null });

        Assert.Empty(ReadinessAnalyzer.Analyze(observed, AsOf).Findings);
    }

    [Fact]
    public void Discounted_compare_at_prices_and_conditional_returns_are_not_flagged()
    {
        Assert.Contains(Clean.Listings, l => l.CompareAtPrice is not null);
        Assert.Contains(Clean.ReturnRules, r => r.Answer == ReturnAnswer.Conditional);
        Assert.Empty(ReadinessAnalyzer.Analyze(Clean with { Kind = StoreKind.Published }, AsOf).Findings);
    }

    [Fact]
    public void A_missing_product_level_attribute_is_one_finding_for_the_whole_product()
    {
        var sneaker = Clean.Products.First(p => p.Category == Category.Sneakers);
        var observed = Clean with
        {
            Kind = StoreKind.Published,
            Products = [.. Clean.Products.Select(p => p.ProductId == sneaker.ProductId
                ? p with { Attributes = p.Attributes.Remove(AttributeKeys.Material) }
                : p)],
        };

        var finding = Assert.Single(ReadinessAnalyzer.Analyze(observed, AsOf).Findings);

        Assert.Equal(sneaker.ProductId, finding.ProductId);
        Assert.Null(finding.Sku);
        Assert.Equal($"FND-MRA-{sneaker.ProductId}-{AttributeKeys.Material}", finding.FindingId);
        Assert.True(Clean.VariantsOf(sneaker.ProductId).Count() > 1);
    }

    [Fact]
    public void A_missing_variant_level_attribute_is_one_finding_per_sku()
    {
        var variants = Clean.Variants.Where(v => Clean.GetProduct(v.ProductId).Category == Category.Sneakers).Take(2).Select(v => v.Sku).ToList();
        var observed = Clean with
        {
            Kind = StoreKind.Published,
            Variants = [.. Clean.Variants.Select(v => variants.Contains(v.Sku) ? v with { Attributes = v.Attributes.Remove(AttributeKeys.Color) } : v)],
        };

        var findings = ReadinessAnalyzer.Analyze(observed, AsOf).Findings;

        Assert.Equal(variants, findings.Select(f => f.Sku!).Order(StringComparer.Ordinal));
        Assert.All(findings, f =>
        {
            Assert.Equal(AttributeKeys.Color, f.Field);
            Assert.Equal(f.Sku, AnalyzerEvaluator.KeyOf(f).Target);
        });
    }

    [Fact]
    public void Description_text_never_satisfies_a_missing_structured_attribute()
    {
        var defect = TestData.Seed42.GroundTruth.Defects.First(d =>
            d.Type == DefectType.MissingRequiredAttribute
            && TestData.Seed42.GroundTruth.SemanticFacts.Any(f => f.Store == StoreKind.Published && f.ProductId == d.ProductId && f.AttributeKey == d.Field));

        Assert.Contains(TestData.Seed42Analysis.Findings, f => f.Type == FindingType.MissingRequiredAttribute && f.ProductId == defect.ProductId && f.Field == defect.Field);
    }

    [Fact]
    public void Inventory_and_price_findings_cite_the_authoritative_source()
    {
        foreach (var finding in TestData.Seed42Analysis.Findings.Where(f => f.Type is FindingType.InventoryConflict or FindingType.PriceConflict))
        {
            Assert.Contains(finding.Evidence, e => e.Source == Core.SourceOfTruth.DataSource.Catalog);
            Assert.Contains(finding.Evidence, e => e.Source is Core.SourceOfTruth.DataSource.Inventory or Core.SourceOfTruth.DataSource.Pricing);
            Assert.Contains(finding.Field, new[] { PublishedFields.StockStatus, PublishedFields.ListedPrice });
        }
    }

    private static StoreSnapshot WithListing(string sku, Func<CatalogListing, CatalogListing> change) => Clean with
    {
        Kind = StoreKind.Published,
        Listings = [.. Clean.Listings.Select(l => l.Sku == sku ? change(l) : l)],
    };
}
