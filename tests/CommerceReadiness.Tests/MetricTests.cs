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

        var consistencyImpacts = Truth.Defects
            .SelectMany(d => d.ExpectedImpacts)
            .Count(u => !u.StartsWith("completeness:", StringComparison.Ordinal) && !u.StartsWith("semantic:", StringComparison.Ordinal));
        Assert.Equal(consistencyImpacts, published.DataConsistency.NotPassing);
        Assert.True(consistencyImpacts > Truth.Defects.Length - missing.Count, "Some defects should affect more than one unit.");

        var units = TestData.Seed42Units;
        Assert.Equal(
            (units.Skus.Length * 2) + units.Deliveries.Length + units.Claims.Length + units.VariantSkus.Length + units.ReturnCells.Length,
            published.DataConsistency.Denominator);

        foreach (var metric in new[] { published.CatalogCompleteness, published.SemanticCoverage, published.DataConsistency })
        {
            Assert.Equal(Math.Round((double)metric.Numerator / metric.Denominator, 4), metric.Value);
            Assert.Equal(metric.NotPassing, metric.FailedUnits.Length + metric.NotEvaluableUnits.Length);
            Assert.False(string.IsNullOrWhiteSpace(metric.Definition));
        }
    }

    [Fact]
    public void Clean_and_published_share_the_exact_same_denominators()
    {
        var clean = TestData.Seed42.Metrics.Stores.Single(s => s.Store == StoreKind.Clean);
        var published = TestData.Seed42.Metrics.Stores.Single(s => s.Store == StoreKind.Published);

        Assert.Equal(clean.DataConsistency.Denominator, published.DataConsistency.Denominator);
        Assert.Equal(clean.CatalogCompleteness.Denominator, published.CatalogCompleteness.Denominator);
        Assert.Equal(clean.SemanticCoverage.Denominator, published.SemanticCoverage.Denominator);
    }

    [Fact]
    public void An_out_of_stock_catalog_defect_cannot_remove_a_delivery_unit()
    {
        var units = TestData.Seed42Units;
        var sku = units.Deliveries[0].Sku;
        var observed = Clean with
        {
            Kind = StoreKind.Published,
            Listings = [.. Clean.Listings.Select(l => l.Sku == sku
                ? l with { StockStatus = StockStatus.OutOfStock, DeliveryPromiseIstanbul = null }
                : l)],
        };
        var observedFacts = Truth.SemanticFacts.Where(f => f.Store == StoreKind.Clean).Select(f => f with { Store = StoreKind.Published });

        var before = ReadinessMetrics.DataConsistency(Clean, units, Clean, Truth.SemanticFacts);
        var after = ReadinessMetrics.DataConsistency(observed, units, Clean, observedFacts);

        Assert.Equal(before.Denominator, after.Denominator);
        Assert.Equal(UnitStatus.NotEvaluable, after.Units.Single(u => u.UnitId == EvaluationUnits.Delivery(sku)).Status);
        Assert.Equal(UnitStatus.Failed, after.Units.Single(u => u.UnitId == EvaluationUnits.Stock(sku)).Status);
        Assert.Equal(before.Numerator - 2, after.Numerator);
    }

    [Fact]
    public void Removing_observed_data_makes_units_not_evaluable_instead_of_disappearing()
    {
        var units = TestData.Seed42Units;
        var sku = units.Deliveries[0].Sku;
        var observed = Clean with
        {
            Kind = StoreKind.Published,
            Listings = [.. Clean.Listings.Where(l => l.Sku != sku)],
            ReturnRules = [.. Clean.ReturnRules.Skip(1)],
        };

        var consistency = ReadinessMetrics.DataConsistency(observed, units, Clean, []);
        var completeness = ReadinessMetrics.CatalogCompleteness(observed, units);

        Assert.Equal(ReadinessMetrics.DataConsistency(Clean, units, Clean, Truth.SemanticFacts).Denominator, consistency.Denominator);
        Assert.Equal(ReadinessMetrics.CatalogCompleteness(Clean, units).Denominator, completeness.Denominator);
        Assert.Equal(UnitStatus.NotEvaluable, consistency.Units.Single(u => u.UnitId == EvaluationUnits.Stock(sku)).Status);
        Assert.Equal(UnitStatus.NotEvaluable, consistency.Units.Single(u => u.UnitId == EvaluationUnits.Return(Clean.ReturnRules[0].CellId)).Status);
        Assert.All(completeness.Units.Where(u => u.UnitId.Contains(sku, StringComparison.Ordinal)), u => Assert.Equal(UnitStatus.NotEvaluable, u.Status));
    }

    [Fact]
    public void Every_injected_defect_has_an_expected_impact_that_is_observed()
    {
        var published = TestData.Seed42.Metrics.Stores.Single(s => s.Store == StoreKind.Published);
        var notPassing = published.AllNotPassingUnits().ToHashSet(StringComparer.Ordinal);

        Assert.Equal(24, Truth.Defects.Length);
        Assert.All(Truth.Defects, d =>
        {
            Assert.NotEmpty(d.ExpectedImpacts);
            Assert.All(d.ExpectedImpacts, unit => Assert.Contains(unit, notPassing));
        });

        var coverage = TestData.Seed42.Metrics.PublishedDefectCoverage;
        Assert.Equal(24, coverage.InjectedDefects);
        Assert.Equal(24, coverage.DefectsWithExpectedImpact);
        Assert.Equal(24, coverage.DefectsDetectedByFailingUnits);
        Assert.Equal(published.CatalogCompleteness.NotPassing, coverage.CatalogCompletenessFailures);
        Assert.Equal(published.SemanticCoverage.NotPassing, coverage.SemanticCoverageFailures);
        Assert.Equal(published.DataConsistency.NotPassing, coverage.DataConsistencyFailures);
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

        var lie = new SemanticFact(StoreKind.Published, product.ProductId, null, AttributeKeys.WaterResistance,
            nameof(WaterResistance.Waterproof), DescriptionPhrases.For(AttributeKeys.WaterResistance, nameof(WaterResistance.Waterproof)), false);
        var lieFlaggedAsSupporting = lie with { SupportsAuthoritativeValue = true };
        var honest = new SemanticFact(StoreKind.Published, product.ProductId, null, AttributeKeys.WaterResistance,
            truth, DescriptionPhrases.For(AttributeKeys.WaterResistance, truth), true);

        var units = TestData.Seed42Units;
        var withoutText = ReadinessMetrics.SemanticCoverage(observed, units, Clean, baseFacts);
        var withLie = ReadinessMetrics.SemanticCoverage(observed, units, Clean, [.. baseFacts, lie]);
        var withMislabelledLie = ReadinessMetrics.SemanticCoverage(observed, units, Clean, [.. baseFacts, lieFlaggedAsSupporting]);
        var withHonestText = ReadinessMetrics.SemanticCoverage(observed, units, Clean, [.. baseFacts, honest]);

        Assert.False(withoutText.Units.Single(u => u.UnitId == unit).Passed);
        Assert.Equal(withoutText.Numerator, withLie.Numerator);
        Assert.Equal(withoutText.Numerator, withMislabelledLie.Numerator);
        Assert.False(withLie.Units.Single(u => u.UnitId == unit).Passed);
        Assert.Equal(withoutText.Numerator + 1, withHonestText.Numerator);

        var claimUnit = EvaluationUnits.Claim(product.ProductId, AttributeKeys.WaterResistance);
        var consistencyWithLie = ReadinessMetrics.DataConsistency(observed, units, Clean, [.. baseFacts, lie]);
        var consistencyWithoutText = ReadinessMetrics.DataConsistency(observed, units, Clean, baseFacts);
        Assert.False(consistencyWithLie.Units.Single(u => u.UnitId == claimUnit).Passed);
        Assert.True(consistencyWithoutText.Units.Single(u => u.UnitId == claimUnit).Passed);
        Assert.Equal(consistencyWithoutText.Denominator, consistencyWithLie.Denominator);
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
