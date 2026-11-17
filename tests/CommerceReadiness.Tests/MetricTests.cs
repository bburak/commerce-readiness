using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Evaluation.Metrics;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.DemoStore;
using CommerceReadiness.DemoStore.Generation;

namespace CommerceReadiness.Tests;

public class MetricTests
{
    private static readonly StoreSnapshot Clean = TestData.Seed42.Clean;
    private static readonly GroundTruth Truth = TestData.Seed42.GroundTruth;

    [Fact]
    public void Numerators_and_denominators_follow_from_requirements_and_defects()
    {
        var published = TestData.Seed42.Metrics.Stores.Single(s => s.Store == StoreKind.Published);
        var requirementUnits = Clean.Variants.Sum(v => CategoryRequirements.For(Clean.GetProduct(v.ProductId).Category).Length);
        Assert.Equal(675, requirementUnits);

        var missing = Truth.Defects.Where(d => d.Type == DefectType.MissingRequiredAttribute).ToList();
        var cleanFacts = Truth.SemanticFacts.Where(f => f.Store == StoreKind.Clean).ToList();
        var missingWithoutText = missing.Count(d => !cleanFacts.Any(f => f.ProductId == d.ProductId && f.AttributeKey == d.Field));

        Assert.Equal(requirementUnits, published.CatalogCompleteness.Denominator);
        Assert.Equal(requirementUnits - missing.Count, published.CatalogCompleteness.Numerator);

        Assert.Equal(requirementUnits, published.SemanticCoverage.Denominator);
        Assert.Equal(requirementUnits - missingWithoutText, published.SemanticCoverage.Numerator);
        Assert.Equal(DefectInjector.MissingWithoutTextEvidenceCount, missingWithoutText);

        Assert.Equal(Truth.Defects.Length - missing.Count, published.DataConsistency.Denominator - published.DataConsistency.Numerator);

        foreach (var metric in new[] { published.CatalogCompleteness, published.SemanticCoverage, published.DataConsistency })
        {
            Assert.Equal(Math.Round((double)metric.Numerator / metric.Denominator, 4), metric.Value);
            Assert.Equal(metric.Denominator - metric.Numerator, metric.FailedUnits.Length);
            Assert.False(string.IsNullOrWhiteSpace(metric.Definition));
        }
    }

    [Fact]
    public void Metrics_are_identical_across_runs()
    {
        Assert.Equal(
            JsonDefaults.Serialize(DemoStoreBuilder.Build(42).Metrics),
            JsonDefaults.Serialize(DemoStoreBuilder.Build(42).Metrics));
    }

    [Fact]
    public void Text_evidence_raises_semantic_coverage_only_when_it_supports_the_authoritative_value()
    {
        var product = Clean.Products.First(p =>
            p.Category == Category.Backpacks && p.Attributes[AttributeKeys.WaterResistance] != nameof(WaterResistance.Waterproof));
        var sku = Clean.VariantsOf(product.ProductId).Single().Sku;
        var truth = product.Attributes[AttributeKeys.WaterResistance];
        var unit = EvaluationUnits.Semantic(sku, AttributeKeys.WaterResistance);

        // Observed store where the structured attribute is missing and the description says nothing about water.
        var observed = Clean with
        {
            Kind = StoreKind.Published,
            Products = [.. Clean.Products.Select(p => p.ProductId == product.ProductId
                ? p with { Attributes = p.Attributes.Remove(AttributeKeys.WaterResistance) }
                : p)],
        };
        var baseFacts = Truth.SemanticFacts
            .Where(f => f.Store == StoreKind.Clean && !(f.ProductId == product.ProductId && f.AttributeKey == AttributeKeys.WaterResistance))
            .Select(f => f with { Store = StoreKind.Published })
            .ToList();

        var lie = new SemanticFact(StoreKind.Published, product.ProductId, AttributeKeys.WaterResistance,
            nameof(WaterResistance.Waterproof), DescriptionPhrases.For(AttributeKeys.WaterResistance, nameof(WaterResistance.Waterproof)), false);
        var lieFlaggedAsSupporting = lie with { SupportsAuthoritativeValue = true };
        var honest = new SemanticFact(StoreKind.Published, product.ProductId, AttributeKeys.WaterResistance,
            truth, DescriptionPhrases.For(AttributeKeys.WaterResistance, truth), true);

        var withoutText = ReadinessMetrics.SemanticCoverage(observed, Clean, baseFacts);
        var withLie = ReadinessMetrics.SemanticCoverage(observed, Clean, [.. baseFacts, lie]);
        var withMislabelledLie = ReadinessMetrics.SemanticCoverage(observed, Clean, [.. baseFacts, lieFlaggedAsSupporting]);
        var withHonestText = ReadinessMetrics.SemanticCoverage(observed, Clean, [.. baseFacts, honest]);

        Assert.False(withoutText.Units.Single(u => u.UnitId == unit).Passed);
        Assert.Equal(withoutText.Numerator, withLie.Numerator);
        Assert.Equal(withoutText.Numerator, withMislabelledLie.Numerator);
        Assert.False(withLie.Units.Single(u => u.UnitId == unit).Passed);
        Assert.Equal(withoutText.Numerator + 1, withHonestText.Numerator);

        var consistency = ReadinessMetrics.DataConsistency(observed, Clean, [.. baseFacts, lie], TestData.Seed42.Context);
        Assert.False(consistency.Units.Single(u => u.UnitId == EvaluationUnits.Claim(product.ProductId, AttributeKeys.WaterResistance)).Passed);
    }

    [Fact]
    public void Generated_misleading_descriptions_never_count_as_semantic_evidence()
    {
        var published = TestData.Seed42.Metrics.Stores.Single(s => s.Store == StoreKind.Published);
        foreach (var defect in Truth.Defects.Where(d => d.Type == DefectType.MisleadingDescription))
        {
            var fact = Truth.SemanticFacts.Single(f =>
                f.Store == StoreKind.Published && f.ProductId == defect.ProductId && f.AttributeKey == defect.Field);
            Assert.False(SemanticSupport.Supports(fact, Clean));
            Assert.Contains(EvaluationUnits.Claim(defect.ProductId!, defect.Field), published.DataConsistency.FailedUnits);
        }
    }
}
