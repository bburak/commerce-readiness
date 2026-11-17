using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Evaluation;

/// <summary>
/// Stable identifiers of the evaluable units behind every metric, plus the traceability from defects and decoys to
/// those units. A decoy maps to the single unit it nearly breaks; a defect can affect more than one unit.
/// </summary>
public static class EvaluationUnits
{
    public static string Completeness(string sku, string key) => $"completeness:{sku}:{key}";

    public static string Semantic(string sku, string key) => $"semantic:{sku}:{key}";

    public static string Stock(string sku) => $"stock:{sku}";

    public static string Price(string sku) => $"price:{sku}";

    public static string Delivery(string sku) => $"delivery:{sku}";

    public static string Claim(string productId, string key) => $"claim:{productId}:{key}";

    public static string Variant(string sku) => $"variant:{sku}";

    public static string Return(string cellId) => $"return:{cellId}";

    /// <summary>The unit a decoy nearly breaks; it must stay passing.</summary>
    public static string For(DecoyCase decoy) => PrimaryUnit(decoy.NearMissOf, decoy.ProductId, decoy.Sku, decoy.Policy, decoy.Field);

    /// <summary>
    /// Every unit a defect is expected to make not pass, primary unit first:
    /// a missing attribute without supporting text also fails Semantic Coverage, and a stale OUT_OF_STOCK row
    /// publishes no delivery promise, which makes its delivery unit NotEvaluable.
    /// </summary>
    public static ImmutableArray<string> ExpectedImpacts(InjectedDefect defect, IEnumerable<SemanticFact> cleanFacts)
    {
        var primary = PrimaryUnit(defect.Type, defect.ProductId, defect.Sku, defect.Policy, defect.Field);
        switch (defect.Type)
        {
            case DefectType.MissingRequiredAttribute
                when !cleanFacts.Any(f => f.Store == StoreKind.Clean && f.ProductId == defect.ProductId && f.AttributeKey == defect.Field
                                          && (f.Sku is null || f.Sku == defect.Sku)):
                return [primary, Semantic(Require(defect.Sku), defect.Field)];
            case DefectType.InventoryConflict when defect.PublishedValue == StockStatusText.OutOfStock:
                return [primary, Delivery(Require(defect.Sku))];
            default:
                return [primary];
        }
    }

    private static string PrimaryUnit(DefectType type, string? productId, string? sku, string? policy, string field) => type switch
    {
        DefectType.MissingRequiredAttribute => Completeness(Require(sku), field),
        DefectType.InventoryConflict => Stock(Require(sku)),
        DefectType.PriceConflict => Price(Require(sku)),
        DefectType.DeliveryConflict => Delivery(Require(sku)),
        DefectType.MisleadingDescription => Claim(Require(productId), field),
        DefectType.AmbiguousReturnRule => Return(Require(policy)),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static string Require(string? value) =>
        value ?? throw new InvalidOperationException("Case is missing the entity identifier its type requires.");
}
