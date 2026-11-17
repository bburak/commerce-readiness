using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Evaluation.Metrics;

/// <summary>
/// Ground-truth readiness metrics. Every metric evaluates the same <see cref="EvaluationUnitSet"/> (built once from
/// the clean store) against an observed snapshot, so clean, published and repaired stores share denominators.
/// Authoritative values come from the hidden clean store; a Milestone 1 analyzer will later be scored against them.
/// </summary>
public static class ReadinessMetrics
{
    public const string CatalogCompletenessDefinition =
        "present required STRUCTURED attributes / applicable required structured attributes; " +
        "one unit per clean SKU x category requirement. Text in the description never counts.";

    public const string SemanticCoverageDefinition =
        "required attributes present structurally OR supported by explicit text evidence / applicable required attributes; " +
        "one unit per clean SKU x category requirement. Text evidence counts only when the asserted value equals the " +
        "authoritative clean-store value, so a misleading description can never raise coverage.";

    public const string DataConsistencyDefinition =
        "passed consistency checks / applicable consistency checks, with the applicable set fixed by the clean store. " +
        "Checks: catalog stockStatus vs Inventory (per SKU), catalog listedPrice vs Pricing (per SKU), catalog Istanbul " +
        "delivery promise vs Shipping calculation (per SKU with authoritative stock), description vs authoritative " +
        "attribute (per product x describable attribute; no claim passes), variant relationship (per variant of a " +
        "multi-variant category), return-policy cell explicit and authoritative (per cell). A unit whose observed data " +
        "is missing is NotEvaluable and counts as not passing.";

    public static StoreMetrics Compute(
        StoreSnapshot observed, EvaluationUnitSet units, StoreSnapshot clean, IEnumerable<SemanticFact> facts)
    {
        var observedFacts = facts.Where(f => f.Store == observed.Kind).ToList();
        return new StoreMetrics(
            observed.Kind,
            CatalogCompleteness(observed, units).ToSummary(),
            SemanticCoverage(observed, units, clean, observedFacts).ToSummary(),
            DataConsistency(observed, units, clean, observedFacts).ToSummary());
    }

    public static MetricResult CatalogCompleteness(StoreSnapshot observed, EvaluationUnitSet units)
    {
        var result = new List<MetricUnit>();
        foreach (var (sku, requirement) in units.Requirements)
        {
            var id = EvaluationUnits.Completeness(sku, requirement.Key);
            result.Add(HasSku(observed, sku)
                ? MetricUnit.Of(id, StructuredValues.Get(observed, sku, requirement) is not null)
                : MetricUnit.NotEvaluable(id, "SKU missing from observed catalog"));
        }

        return new MetricResult("CatalogCompleteness", CatalogCompletenessDefinition, [.. result]);
    }

    /// <param name="observedFacts">Semantic facts asserted by the observed store's descriptions.</param>
    public static MetricResult SemanticCoverage(
        StoreSnapshot observed, EvaluationUnitSet units, StoreSnapshot clean, IEnumerable<SemanticFact> observedFacts)
    {
        var supported = observedFacts
            .Where(f => f.Store == observed.Kind && SemanticSupport.Supports(f, clean))
            .Select(f => (f.ProductId, f.AttributeKey))
            .ToHashSet();

        var result = new List<MetricUnit>();
        foreach (var (sku, requirement) in units.Requirements)
        {
            var id = EvaluationUnits.Semantic(sku, requirement.Key);
            if (!HasSku(observed, sku))
            {
                result.Add(MetricUnit.NotEvaluable(id, "SKU missing from observed catalog"));
                continue;
            }

            var structural = StructuredValues.Get(observed, sku, requirement) is not null;
            var textual = supported.Contains((observed.GetVariant(sku).ProductId, requirement.Key));
            result.Add(MetricUnit.Of(id, structural || textual, !structural && textual ? "text evidence only" : null));
        }

        return new MetricResult("SemanticCoverage", SemanticCoverageDefinition, [.. result]);
    }

    public static MetricResult DataConsistency(
        StoreSnapshot observed, EvaluationUnitSet units, StoreSnapshot clean, IEnumerable<SemanticFact> observedFacts)
    {
        var result = new List<MetricUnit>();

        foreach (var sku in units.Skus)
        {
            var listing = observed.Listings.FirstOrDefault(l => l.Sku == sku);
            if (listing is null)
            {
                result.Add(MetricUnit.NotEvaluable(EvaluationUnits.Stock(sku), "listing missing"));
                result.Add(MetricUnit.NotEvaluable(EvaluationUnits.Price(sku), "listing missing"));
                continue;
            }

            var available = clean.GetInventory(sku).Available;
            result.Add(MetricUnit.Of(
                EvaluationUnits.Stock(sku),
                (listing.StockStatus == StockStatus.InStock) == (available > 0),
                $"catalog={listing.StockStatus.ToCatalogText()}; inventory.available={InvariantFormat.Number(available)}"));

            var price = clean.GetPrice(sku);
            result.Add(MetricUnit.Of(
                EvaluationUnits.Price(sku),
                listing.ListedPrice == price,
                $"catalog={InvariantFormat.Money(listing.ListedPrice)}; pricing={InvariantFormat.Money(price)}"));
        }

        foreach (var (sku, authoritative) in units.Deliveries)
        {
            var id = EvaluationUnits.Delivery(sku);
            var promise = observed.Listings.FirstOrDefault(l => l.Sku == sku)?.DeliveryPromiseIstanbul;
            result.Add(promise is { } date
                ? MetricUnit.Of(id, date == authoritative, $"catalog={InvariantFormat.Date(date)}; shipping={InvariantFormat.Date(authoritative)}")
                : MetricUnit.NotEvaluable(id, $"no delivery promise published; shipping={InvariantFormat.Date(authoritative)}"));
        }

        var factsByClaim = observedFacts
            .Where(f => f.Store == observed.Kind)
            .ToLookup(f => (f.ProductId, f.AttributeKey));
        foreach (var (productId, key) in units.Claims)
        {
            var id = EvaluationUnits.Claim(productId, key);
            if (!observed.Products.Any(p => p.ProductId == productId))
            {
                result.Add(MetricUnit.NotEvaluable(id, "product missing"));
                continue;
            }

            var claims = factsByClaim[(productId, key)].ToList();
            result.Add(MetricUnit.Of(
                id,
                claims.All(f => SemanticSupport.Supports(f, clean)),
                claims.Count == 0 ? "no claim" : $"asserted={string.Join("|", claims.Select(f => f.AssertedValue))}"));
        }

        foreach (var sku in units.VariantSkus)
        {
            var id = EvaluationUnits.Variant(sku);
            var variant = observed.Variants.FirstOrDefault(v => v.Sku == sku);
            var product = variant is null ? null : observed.Products.FirstOrDefault(p => p.ProductId == variant.ProductId);
            if (variant is null || product is null)
            {
                result.Add(MetricUnit.NotEvaluable(id, variant is null ? "variant missing" : "parent product missing"));
                continue;
            }

            var variantKeys = CategoryRequirements.VariantLevelKeys(product.Category).ToList();
            var signature = VariantSignature(variant, variantKeys);
            var unique = observed.VariantsOf(product.ProductId).Count(v => VariantSignature(v, variantKeys) == signature) == 1;
            result.Add(MetricUnit.Of(id, unique, unique ? null : $"duplicate variant {signature}"));
        }

        foreach (var cellId in units.ReturnCells)
        {
            var id = EvaluationUnits.Return(cellId);
            var rule = observed.ReturnRules.FirstOrDefault(r => r.CellId == cellId);
            if (rule is null)
            {
                result.Add(MetricUnit.NotEvaluable(id, "policy cell missing"));
                continue;
            }

            var authoritative = clean.ReturnRules.First(r => r.CellId == cellId);
            var consistent = rule.Answer != ReturnAnswer.Unspecified
                && rule.Answer == authoritative.Answer
                && rule.WindowDays == authoritative.WindowDays;
            result.Add(MetricUnit.Of(id, consistent, $"answer={rule.Answer}"));
        }

        return new MetricResult("DataConsistency", DataConsistencyDefinition, [.. result]);
    }

    private static bool HasSku(StoreSnapshot observed, string sku)
    {
        var variant = observed.Variants.FirstOrDefault(v => v.Sku == sku);
        return variant is not null
            && observed.Products.Any(p => p.ProductId == variant.ProductId)
            && observed.Listings.Any(l => l.Sku == sku);
    }

    private static string VariantSignature(ProductVariant variant, IEnumerable<string> keys) =>
        string.Join("|", keys.Select(k => variant.Attributes.Find(k) ?? string.Empty));
}
