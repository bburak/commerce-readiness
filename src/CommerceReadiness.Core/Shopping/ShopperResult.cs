using System.ComponentModel;
using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Shopping;

public enum ShopperOutcome
{
    Recommended,
    NoMatch,
    NeedsClarification,
}

/// <summary>
/// Confirmed: the recommendation is asserted to satisfy every mandatory constraint of the request.
/// Uncertain: at least one relevant fact could not be established from published tools.
/// </summary>
public enum RecommendationConfidence
{
    Confirmed,
    Uncertain,
}

public enum ClaimType
{
    ProductAttribute,
    Price,
    Availability,
    Delivery,
    ReturnPolicy,
    PaymentOption,
}

public enum ProductAttributeField
{
    Category,
    Brand,
    ProductType,
    WaterResistance,
    LaptopSizeIn,
    CapacityLiters,
    TargetGroup,
    NoiseCancellation,
    Microphone,
    BatteryHours,
    WarrantyMonths,
    ShoeSize,
    Color,
}

public static class ProductAttributeFields
{
    /// <summary>The published attribute key behind a field; null for <see cref="ProductAttributeField.Category"/>.</summary>
    public static string? KeyOf(ProductAttributeField field) => field switch
    {
        ProductAttributeField.Brand => Catalog.AttributeKeys.Brand,
        ProductAttributeField.ProductType => Catalog.AttributeKeys.ProductType,
        ProductAttributeField.WaterResistance => Catalog.AttributeKeys.WaterResistance,
        ProductAttributeField.LaptopSizeIn => Catalog.AttributeKeys.LaptopSizeIn,
        ProductAttributeField.CapacityLiters => Catalog.AttributeKeys.CapacityLiters,
        ProductAttributeField.TargetGroup => Catalog.AttributeKeys.TargetGroup,
        ProductAttributeField.NoiseCancellation => Catalog.AttributeKeys.NoiseCancellation,
        ProductAttributeField.Microphone => Catalog.AttributeKeys.Microphone,
        ProductAttributeField.BatteryHours => Catalog.AttributeKeys.BatteryHours,
        ProductAttributeField.WarrantyMonths => Catalog.AttributeKeys.WarrantyMonths,
        ProductAttributeField.ShoeSize => Catalog.AttributeKeys.ShoeSize,
        ProductAttributeField.Color => Catalog.AttributeKeys.Color,
        _ => null,
    };

    public static ProductAttributeField? ForKey(string key) =>
        Enum.GetValues<ProductAttributeField>().Where(f => KeyOf(f) == key).Select(f => (ProductAttributeField?)f).FirstOrDefault();
}

/// <summary>Decision-critical information a shopper can be asked for.</summary>
public enum ShopperMissingField
{
    Category,
    ShoeSize,
    DeliveryCity,
    DeliveryDeadline,
    MaxPrice,
    Color,
    ProductType,
    TargetGroup,
}

public sealed record Clarification(
    [property: Description("One focused Turkish question for the shopper.")] string QuestionTr,
    ImmutableArray<ShopperMissingField> MissingFields);

/// <param name="AttributeField">Required exactly when Type is ProductAttribute.</param>
/// <param name="City">Required exactly when Type is Delivery.</param>
/// <param name="ReturnQuestion">Required exactly when Type is ReturnPolicy.</param>
/// <param name="Value">Canonical value, see <see cref="ClaimFormat"/>.</param>
/// <param name="EvidenceCallIds">EVID-... identifiers of the tool results that support the claim.</param>
public sealed record GroundedClaim(
    ClaimType Type,
    ProductAttributeField? AttributeField,
    City? City,
    ReturnQuestion? ReturnQuestion,
    [property: Description("Canonical value: money 1299.00, date yyyy-MM-dd, InStock/OutOfStock, enum names, true/false, integers, Answer or Answer:windowDays.")]
    string Value,
    ImmutableArray<string> EvidenceCallIds);

public sealed record Recommendation(string Sku, RecommendationConfidence Confidence, ImmutableArray<GroundedClaim> Claims);

/// <summary>
/// The structured, authoritative output of one shopper run. <see cref="Recommendations"/>[0] is the primary
/// recommendation. Invariants are enforced by <see cref="ShopperResultValidator"/>.
/// </summary>
public sealed record ShopperResult(
    int SchemaVersion,
    ShopperOutcome Outcome,
    Clarification? Clarification,
    [property: Description("Primary recommendation first. Empty unless Outcome is Recommended.")]
    ImmutableArray<Recommendation> Recommendations,
    string? NoMatchReason,
    ImmutableArray<string> SupportingEvidenceCallIds)
{
    public const int CurrentSchemaVersion = 1;
}

public static class ShopperResultValidator
{
    public const int MaxRecommendations = 3;

    /// <summary>Structural invariant violations; empty means the result is well-formed.</summary>
    public static ImmutableArray<string> Validate(ShopperResult result)
    {
        var errors = new List<string>();
        if (result.SchemaVersion != ShopperResult.CurrentSchemaVersion)
        {
            errors.Add($"schemaVersion must be {ShopperResult.CurrentSchemaVersion}");
        }

        if (result.Recommendations.IsDefault || result.SupportingEvidenceCallIds.IsDefault)
        {
            errors.Add("recommendations and supportingEvidenceCallIds are required");
            return [.. errors];
        }

        switch (result.Outcome)
        {
            case ShopperOutcome.Recommended:
                if (result.Recommendations.Length is 0 or > MaxRecommendations)
                {
                    errors.Add($"Recommended needs 1 to {MaxRecommendations} recommendations");
                }
                else if (result.Recommendations[0].Confidence != RecommendationConfidence.Confirmed)
                {
                    errors.Add("the primary recommendation must be Confirmed");
                }

                if (result.Clarification is not null || result.NoMatchReason is not null)
                {
                    errors.Add("Recommended allows no clarification and no noMatchReason");
                }

                break;
            case ShopperOutcome.NoMatch:
                if (result.Recommendations.Length > 0 || result.Clarification is not null)
                {
                    errors.Add("NoMatch allows no recommendations and no clarification");
                }

                if (string.IsNullOrWhiteSpace(result.NoMatchReason))
                {
                    errors.Add("NoMatch needs a noMatchReason");
                }

                break;
            case ShopperOutcome.NeedsClarification:
                if (result.Recommendations.Length > 0 || result.NoMatchReason is not null)
                {
                    errors.Add("NeedsClarification allows no recommendations and no noMatchReason");
                }

                if (result.Clarification is not { } clarification
                    || string.IsNullOrWhiteSpace(clarification.QuestionTr)
                    || clarification.MissingFields.IsDefaultOrEmpty)
                {
                    errors.Add("NeedsClarification needs a question and at least one missing field");
                }

                break;
            default:
                errors.Add($"unknown outcome {result.Outcome}");
                break;
        }

        if (result.Recommendations.Select(r => r.Sku).Distinct(StringComparer.Ordinal).Count() != result.Recommendations.Length)
        {
            errors.Add("recommendation SKUs must be unique");
        }

        foreach (var recommendation in result.Recommendations)
        {
            if (string.IsNullOrWhiteSpace(recommendation.Sku) || recommendation.Claims.IsDefault)
            {
                errors.Add("every recommendation needs a sku and a claims array");
                continue;
            }

            foreach (var claim in recommendation.Claims)
            {
                if ((claim.Type == ClaimType.ProductAttribute) != (claim.AttributeField is not null)
                    || (claim.Type == ClaimType.Delivery) != (claim.City is not null)
                    || (claim.Type == ClaimType.ReturnPolicy) != (claim.ReturnQuestion is not null))
                {
                    errors.Add($"{recommendation.Sku}: {claim.Type} claim has wrong qualifiers");
                }

                if (claim.EvidenceCallIds.IsDefault)
                {
                    errors.Add($"{recommendation.Sku}: {claim.Type} claim needs an evidenceCallIds array");
                }
            }
        }

        return [.. errors];
    }
}

/// <summary>
/// Canonical claim value formats. A value that does not parse for its claim type and field is malformed, which is
/// reported separately from unsupported evidence.
/// </summary>
public static class ClaimFormat
{
    public const string InStock = "InStock";
    public const string OutOfStock = "OutOfStock";

    private enum ValueKind
    {
        Money,
        Availability,
        Date,
        Return,
        Count,
        Decimal,
        Text,
        Bool,
        CategoryName,
        WaterResistanceName,
    }

    public static bool IsWellFormed(GroundedClaim claim) => Kind(claim) is { } kind && Parse(kind, claim.Value) is not null;

    /// <summary>Whether two canonical values of the claim's kind are equal (numbers compare numerically).</summary>
    public static bool Equivalent(GroundedClaim claim, string evidenceValue) =>
        Kind(claim) is { } kind && Parse(kind, claim.Value) is { } claimed && Parse(kind, evidenceValue) is { } observed
        && Equals(claimed, observed);

    public static string Return(ReturnAnswer answer, int? windowDays) =>
        windowDays is { } days ? $"{answer}:{InvariantFormat.Number(days)}" : answer.ToString();

    private static ValueKind? Kind(GroundedClaim claim) => claim.Type switch
    {
        ClaimType.Price => ValueKind.Money,
        ClaimType.Availability => ValueKind.Availability,
        ClaimType.Delivery => ValueKind.Date,
        ClaimType.ReturnPolicy => ValueKind.Return,
        ClaimType.PaymentOption => ValueKind.Count,
        ClaimType.ProductAttribute => claim.AttributeField switch
        {
            ProductAttributeField.Category => ValueKind.CategoryName,
            ProductAttributeField.WaterResistance => ValueKind.WaterResistanceName,
            ProductAttributeField.LaptopSizeIn or ProductAttributeField.CapacityLiters => ValueKind.Decimal,
            ProductAttributeField.BatteryHours or ProductAttributeField.WarrantyMonths or ProductAttributeField.ShoeSize => ValueKind.Count,
            ProductAttributeField.NoiseCancellation or ProductAttributeField.Microphone => ValueKind.Bool,
            ProductAttributeField.Brand or ProductAttributeField.ProductType or ProductAttributeField.TargetGroup
                or ProductAttributeField.Color => ValueKind.Text,
            _ => null,
        },
        _ => null,
    };

    private static object? Parse(ValueKind kind, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim())
        {
            return null;
        }

        return kind switch
        {
            ValueKind.Money => System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+\.\d{2}$")
                && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var money)
                    ? money
                    : null,
            ValueKind.Decimal =>
                decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) ? d : null,
            ValueKind.Count => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null,
            ValueKind.Availability => text is InStock or OutOfStock ? text : null,
            ValueKind.Date => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
            ValueKind.Bool => text is "true" or "false" ? text : null,
            ValueKind.Text => text,
            ValueKind.CategoryName => ParseName<Category>(text),
            ValueKind.WaterResistanceName => ParseName<WaterResistance>(text),
            ValueKind.Return => ParseReturn(text),
            _ => null,
        };
    }

    private static object? ParseName<TEnum>(string text)
        where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>().Contains(text, StringComparer.Ordinal) ? text : null;

    private static object? ParseReturn(string text)
    {
        var parts = text.Split(':');
        if (parts.Length > 2 || ParseName<ReturnAnswer>(parts[0]) is null)
        {
            return null;
        }

        if (parts.Length == 1)
        {
            return text;
        }

        return int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var days) ? $"{parts[0]}:{days}" : null;
    }
}
