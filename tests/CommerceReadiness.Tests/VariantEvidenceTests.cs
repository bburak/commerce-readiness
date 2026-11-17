using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Evaluation.Metrics;

namespace CommerceReadiness.Tests;

/// <summary>Variant-level text evidence must stay with its own SKU and never spread to sibling variants.</summary>
public class VariantEvidenceTests
{
    private static readonly StoreSnapshot Clean = TestData.Seed42.Clean;
    private static readonly EvaluationUnitSet Units = TestData.Seed42Units;

    private static readonly Product Sneaker = Clean.Products.First(p =>
        p.Category == Category.Sneakers
        && Clean.VariantsOf(p.ProductId).Select(v => v.Attributes[AttributeKeys.Color]).Distinct().Count() > 1);

    private static readonly ProductVariant VariantA = Clean.VariantsOf(Sneaker.ProductId).First();

    private static readonly ProductVariant VariantB = Clean.VariantsOf(Sneaker.ProductId)
        .First(v => v.Attributes[AttributeKeys.Color] != VariantA.Attributes[AttributeKeys.Color]);

    private static readonly ImmutableArray<SemanticFact> BaseFacts =
        [.. TestData.Seed42.GroundTruth.SemanticFacts.Where(f => f.Store == StoreKind.Clean).Select(f => f with { Store = StoreKind.Published })];

    [Fact]
    public void A_sku_scoped_fact_covers_only_its_own_sku()
    {
        var observed = WithoutVariantAttributes(VariantA.Sku, VariantB.Sku);
        var size = VariantA.Attributes[AttributeKeys.ShoeSize];
        var color = VariantA.Attributes[AttributeKeys.Color];
        var phrase = $"{size} numara {color.ToLowerInvariant()} seçenek mevcut.";
        SemanticFact[] evidence =
        [
            new(StoreKind.Published, Sneaker.ProductId, VariantA.Sku, AttributeKeys.ShoeSize, size, phrase, true),
            new(StoreKind.Published, Sneaker.ProductId, VariantA.Sku, AttributeKeys.Color, color, phrase, true),
        ];

        var coverage = ReadinessMetrics.SemanticCoverage(observed, Units, Clean, [.. BaseFacts, .. evidence]);

        Assert.True(Unit(coverage, EvaluationUnits.Semantic(VariantA.Sku, AttributeKeys.ShoeSize)).Passed);
        Assert.True(Unit(coverage, EvaluationUnits.Semantic(VariantA.Sku, AttributeKeys.Color)).Passed);
        Assert.False(Unit(coverage, EvaluationUnits.Semantic(VariantB.Sku, AttributeKeys.ShoeSize)).Passed);
        Assert.False(Unit(coverage, EvaluationUnits.Semantic(VariantB.Sku, AttributeKeys.Color)).Passed);
    }

    [Fact]
    public void Variant_level_evidence_without_a_sku_never_counts()
    {
        var observed = WithoutVariantAttributes(VariantA.Sku, VariantB.Sku);
        var color = VariantA.Attributes[AttributeKeys.Color];
        var unscoped = new SemanticFact(StoreKind.Published, Sneaker.ProductId, null, AttributeKeys.Color, color, $"{color} renk seçeneği.", true);

        var without = ReadinessMetrics.SemanticCoverage(observed, Units, Clean, BaseFacts);
        var with = ReadinessMetrics.SemanticCoverage(observed, Units, Clean, [.. BaseFacts, unscoped]);

        Assert.False(SemanticSupport.Supports(unscoped, Clean));
        Assert.Equal(without.Numerator, with.Numerator);
        Assert.False(Unit(with, EvaluationUnits.Semantic(VariantA.Sku, AttributeKeys.Color)).Passed);
        Assert.False(Unit(with, EvaluationUnits.Semantic(VariantB.Sku, AttributeKeys.Color)).Passed);
    }

    [Fact]
    public void A_wrong_sku_scoped_claim_fails_only_that_skus_claim_unit()
    {
        var observed = Clean with { Kind = StoreKind.Published };
        var wrongColor = VariantA.Attributes[AttributeKeys.Color];
        var lie = new SemanticFact(StoreKind.Published, Sneaker.ProductId, VariantB.Sku, AttributeKeys.Color, wrongColor, $"{wrongColor} renk.", false);

        var clean = ReadinessMetrics.DataConsistency(Clean, Units, Clean, TestData.Seed42.GroundTruth.SemanticFacts);
        var withLie = ReadinessMetrics.DataConsistency(observed, Units, Clean, [.. BaseFacts, lie]);
        var coverage = ReadinessMetrics.SemanticCoverage(
            WithoutVariantAttributes(VariantB.Sku), Units, Clean, [.. BaseFacts, lie]);

        Assert.False(SemanticSupport.Supports(lie, Clean));
        Assert.Equal(clean.Denominator, withLie.Denominator);
        Assert.Equal(UnitStatus.Failed, Unit(withLie, EvaluationUnits.Claim(VariantB.Sku, AttributeKeys.Color)).Status);
        Assert.Equal(UnitStatus.Passed, Unit(withLie, EvaluationUnits.Claim(VariantA.Sku, AttributeKeys.Color)).Status);
        Assert.Equal(clean.Numerator - 1, withLie.Numerator);
        Assert.False(Unit(coverage, EvaluationUnits.Semantic(VariantB.Sku, AttributeKeys.Color)).Passed);
    }

    [Fact]
    public void A_sku_scoped_fact_cannot_borrow_a_sku_from_another_product()
    {
        var foreignSku = Clean.Variants.First(v => v.ProductId != Sneaker.ProductId && Clean.GetProduct(v.ProductId).Category == Category.Sneakers);
        var fact = new SemanticFact(StoreKind.Published, Sneaker.ProductId, foreignSku.Sku, AttributeKeys.Color,
            foreignSku.Attributes[AttributeKeys.Color], "renk", true);

        Assert.False(SemanticSupport.Supports(fact, Clean));
    }

    [Fact]
    public void A_product_scoped_fact_still_covers_every_sku_of_the_product()
    {
        var observed = Clean with
        {
            Kind = StoreKind.Published,
            Products = [.. Clean.Products.Select(p => p.ProductId == Sneaker.ProductId
                ? p with { Attributes = p.Attributes.Remove(AttributeKeys.Material) }
                : p)],
        };

        var coverage = ReadinessMetrics.SemanticCoverage(observed, Units, Clean, BaseFacts);

        Assert.All(Clean.VariantsOf(Sneaker.ProductId), v =>
            Assert.True(Unit(coverage, EvaluationUnits.Semantic(v.Sku, AttributeKeys.Material)).Passed));
    }

    [Fact]
    public void Every_variant_attribute_of_every_sneaker_sku_has_a_claim_unit()
    {
        var sneakerSkus = Clean.Variants.Where(v => Clean.GetProduct(v.ProductId).Category == Category.Sneakers).ToList();
        var variantClaims = Units.Claims.Where(c => c.Sku is not null).ToList();

        Assert.Equal(sneakerSkus.Count * 2, variantClaims.Count);
        Assert.All(sneakerSkus, v =>
        {
            Assert.Contains(variantClaims, c => c.Sku == v.Sku && c.AttributeKey == AttributeKeys.ShoeSize);
            Assert.Contains(variantClaims, c => c.Sku == v.Sku && c.AttributeKey == AttributeKeys.Color);
        });
    }

    private static StoreSnapshot WithoutVariantAttributes(params string[] skus) => Clean with
    {
        Kind = StoreKind.Published,
        Variants = [.. Clean.Variants.Select(v => skus.Contains(v.Sku) ? v with { Attributes = Attributes.Empty } : v)],
    };

    private static MetricUnit Unit(MetricResult result, string unitId) => result.Units.Single(u => u.UnitId == unitId);
}
