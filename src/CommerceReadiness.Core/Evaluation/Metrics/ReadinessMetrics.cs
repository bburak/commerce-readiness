using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Time;

namespace CommerceReadiness.Core.Evaluation.Metrics;

/// <summary>
/// Ground-truth readiness metrics. They are computed with access to the hidden clean store, so they measure
/// the true state of an observed store; a Milestone 1 analyzer will later be scored against them.
/// </summary>
public static class ReadinessMetrics
{
    public const string CatalogCompletenessDefinition =
        "present required STRUCTURED attributes / applicable required structured attributes; " +
        "one unit per SKU x category requirement. Text in the description never counts.";

    public const string SemanticCoverageDefinition =
        "required attributes present structurally OR supported by explicit text evidence / applicable required attributes; " +
        "one unit per SKU x category requirement. Text evidence counts only when the asserted value equals the " +
        "authoritative clean-store value, so a misleading description can never raise coverage.";

    public const string DataConsistencyDefinition =
        "passed applicable consistency checks / applicable consistency checks. Checks: catalog stockStatus vs Inventory " +
        "(per SKU), catalog listedPrice vs Pricing (per SKU), catalog Istanbul delivery promise vs Shipping calculation " +
        "(per SKU with a promise and authoritative stock), description claim vs authoritative attribute (per claim), " +
        "variant relationship (per variant of a multi-variant category), return-policy cell explicit and authoritative (per cell).";

    public static StoreMetrics Compute(
        StoreSnapshot observed, StoreSnapshot clean, IEnumerable<SemanticFact> facts, SimulationContext context)
    {
        var observedFacts = facts.Where(f => f.Store == observed.Kind).ToList();
        return new StoreMetrics(
            observed.Kind,
            CatalogCompleteness(observed).ToSummary(),
            SemanticCoverage(observed, clean, observedFacts).ToSummary(),
            DataConsistency(observed, clean, observedFacts, context).ToSummary());
    }

    public static MetricResult CatalogCompleteness(StoreSnapshot observed)
    {
        var units = new List<MetricUnit>();
        foreach (var (sku, requirement) in RequirementUnits(observed))
        {
            var present = StructuredValues.Get(observed, sku, requirement) is not null;
            units.Add(new MetricUnit(EvaluationUnits.Completeness(sku, requirement.Key), present));
        }

        return new MetricResult("CatalogCompleteness", CatalogCompletenessDefinition, [.. units]);
    }

    /// <param name="observedFacts">Semantic facts asserted by the observed store's descriptions.</param>
    public static MetricResult SemanticCoverage(StoreSnapshot observed, StoreSnapshot clean, IEnumerable<SemanticFact> observedFacts)
    {
        var supported = observedFacts
            .Where(f => f.Store == observed.Kind && SemanticSupport.Supports(f, clean))
            .Select(f => (f.ProductId, f.AttributeKey))
            .ToHashSet();

        var units = new List<MetricUnit>();
        foreach (var (sku, requirement) in RequirementUnits(observed))
        {
            var structural = StructuredValues.Get(observed, sku, requirement) is not null;
            var textual = supported.Contains((observed.GetVariant(sku).ProductId, requirement.Key));
            var detail = structural ? null : textual ? "text evidence only" : null;
            units.Add(new MetricUnit(EvaluationUnits.Semantic(sku, requirement.Key), structural || textual, detail));
        }

        return new MetricResult("SemanticCoverage", SemanticCoverageDefinition, [.. units]);
    }

    public static MetricResult DataConsistency(
        StoreSnapshot observed, StoreSnapshot clean, IEnumerable<SemanticFact> observedFacts, SimulationContext context)
    {
        var units = new List<MetricUnit>();
        foreach (var listing in observed.Listings.OrderBy(l => l.Sku, StringComparer.Ordinal))
        {
            var available = clean.GetInventory(listing.Sku).Available;
            var stockConsistent = (listing.StockStatus == StockStatus.InStock) == (available > 0);
            units.Add(new MetricUnit(
                EvaluationUnits.Stock(listing.Sku),
                stockConsistent,
                $"catalog={listing.StockStatus.ToCatalogText()}; inventory.available={InvariantFormat.Number(available)}"));

            var price = clean.GetPrice(listing.Sku);
            units.Add(new MetricUnit(
                EvaluationUnits.Price(listing.Sku),
                listing.ListedPrice == price,
                $"catalog={InvariantFormat.Money(listing.ListedPrice)}; pricing={InvariantFormat.Money(price)}"));

            if (listing.DeliveryPromiseIstanbul is { } promise
                && DeliveryCalculator.EstimateDelivery(clean, listing.Sku, City.Istanbul, context.Now) is { } calculated)
            {
                units.Add(new MetricUnit(
                    EvaluationUnits.Delivery(listing.Sku),
                    promise == calculated,
                    $"catalog={InvariantFormat.Date(promise)}; shipping={InvariantFormat.Date(calculated)}"));
            }
        }

        foreach (var fact in observedFacts
                     .Where(f => f.Store == observed.Kind)
                     .OrderBy(f => f.ProductId, StringComparer.Ordinal)
                     .ThenBy(f => f.AttributeKey, StringComparer.Ordinal))
        {
            units.Add(new MetricUnit(
                EvaluationUnits.Claim(fact.ProductId, fact.AttributeKey),
                SemanticSupport.Supports(fact, clean),
                $"asserted={fact.AssertedValue}"));
        }

        foreach (var variant in observed.Variants.OrderBy(v => v.Sku, StringComparer.Ordinal))
        {
            var product = observed.Products.FirstOrDefault(p => p.ProductId == variant.ProductId);
            if (product is null)
            {
                units.Add(new MetricUnit(EvaluationUnits.Variant(variant.Sku), false, "parent product missing"));
                continue;
            }

            var variantKeys = CategoryRequirements.VariantLevelKeys(product.Category).ToList();
            if (variantKeys.Count == 0)
            {
                continue;
            }

            var signature = VariantSignature(variant, variantKeys);
            var unique = observed.VariantsOf(product.ProductId).Count(v => VariantSignature(v, variantKeys) == signature) == 1;
            units.Add(new MetricUnit(EvaluationUnits.Variant(variant.Sku), unique, unique ? null : $"duplicate variant {signature}"));
        }

        foreach (var rule in observed.ReturnRules)
        {
            var authoritative = clean.FindReturnRule(rule.Category, rule.Question);
            var consistent = rule.Answer != ReturnAnswer.Unspecified
                && authoritative is not null
                && rule.Answer == authoritative.Answer
                && rule.WindowDays == authoritative.WindowDays;
            units.Add(new MetricUnit(EvaluationUnits.Return(rule.CellId), consistent, $"answer={rule.Answer}"));
        }

        return new MetricResult("DataConsistency", DataConsistencyDefinition, [.. units]);
    }

    private static IEnumerable<(string Sku, AttributeRequirement Requirement)> RequirementUnits(StoreSnapshot store)
    {
        foreach (var variant in store.Variants.OrderBy(v => v.Sku, StringComparer.Ordinal))
        {
            foreach (var requirement in CategoryRequirements.For(store.GetProduct(variant.ProductId).Category))
            {
                yield return (variant.Sku, requirement);
            }
        }
    }

    private static string VariantSignature(ProductVariant variant, IEnumerable<string> keys) =>
        string.Join("|", keys.Select(k => variant.Attributes.Find(k) ?? string.Empty));
}
