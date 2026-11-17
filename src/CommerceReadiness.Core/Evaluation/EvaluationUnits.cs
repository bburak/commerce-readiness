namespace CommerceReadiness.Core.Evaluation;

/// <summary>
/// Stable identifiers of the evaluable units behind every metric. Each defect and decoy maps to exactly one unit,
/// which lets tests prove that published failures are exactly the injected defects.
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

    public static string For(InjectedDefect defect) => ForCase(defect.Type, defect.ProductId, defect.Sku, defect.Policy, defect.Field);

    public static string For(DecoyCase decoy) => ForCase(decoy.NearMissOf, decoy.ProductId, decoy.Sku, decoy.Policy, decoy.Field);

    private static string ForCase(DefectType type, string? productId, string? sku, string? policy, string field) => type switch
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
