using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Time;

namespace CommerceReadiness.Core.Evaluation;

public sealed record RequirementUnit(string Sku, AttributeRequirement Requirement);

public sealed record DeliveryUnit(string Sku, DateOnly AuthoritativeDate);

public sealed record ClaimUnit(string ProductId, string AttributeKey);

/// <summary>
/// The fixed universe of evaluable units, built once from the clean authoritative store and then evaluated
/// against every snapshot (clean, published, repaired). Nothing in an observed snapshot can add or remove a unit,
/// so all snapshots share the same denominators.
/// </summary>
public sealed record EvaluationUnitSet(
    string StoreVersion,
    ImmutableArray<RequirementUnit> Requirements,
    ImmutableArray<string> Skus,
    ImmutableArray<DeliveryUnit> Deliveries,
    ImmutableArray<ClaimUnit> Claims,
    ImmutableArray<string> VariantSkus,
    ImmutableArray<string> ReturnCells)
{
    public static EvaluationUnitSet FromClean(StoreSnapshot clean, SimulationContext context)
    {
        if (clean.Kind != StoreKind.Clean)
        {
            throw new ArgumentException("The evaluation universe is built from the clean store only.", nameof(clean));
        }

        var variants = clean.Variants.OrderBy(v => v.Sku, StringComparer.Ordinal).ToList();
        Category CategoryOf(ProductVariant v) => clean.GetProduct(v.ProductId).Category;

        return new EvaluationUnitSet(
            clean.StoreVersion,
            [.. variants.SelectMany(v => CategoryRequirements.For(CategoryOf(v)).Select(r => new RequirementUnit(v.Sku, r)))],
            [.. variants.Select(v => v.Sku)],
            [.. variants
                .Select(v => (v.Sku, Date: DeliveryCalculator.EstimateDelivery(clean, v.Sku, City.Istanbul, context.Now)))
                .Where(x => x.Date is not null)
                .Select(x => new DeliveryUnit(x.Sku, x.Date!.Value))],
            [.. clean.Products
                .OrderBy(p => p.ProductId, StringComparer.Ordinal)
                .SelectMany(p => CategoryRequirements.DescriptionClaimKeys(p.Category).Select(k => new ClaimUnit(p.ProductId, k)))],
            [.. variants.Where(v => CategoryRequirements.VariantLevelKeys(CategoryOf(v)).Any()).Select(v => v.Sku)],
            [.. clean.ReturnRules.Select(r => r.CellId)]);
    }
}
