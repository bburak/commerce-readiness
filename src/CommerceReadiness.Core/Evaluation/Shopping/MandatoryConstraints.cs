using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Shopping;

namespace CommerceReadiness.Core.Evaluation.Shopping;

/// <summary>
/// One mandatory constraint of a hidden intent and how an authoritative evidence value must satisfy it.
/// Used for trace-based decision grounding; satisfaction mirrors <see cref="ConstraintEvaluator"/> semantics.
/// Emitting a matching <see cref="GroundedClaim"/> is optional and scored separately via claim metrics.
/// </summary>
public sealed record RequiredClaim(
    string Constraint,
    ClaimType Type,
    ProductAttributeField? Field,
    City? City,
    ReturnQuestion? Question,
    Func<string, bool> IsSatisfiedBy)
{
    public bool Matches(GroundedClaim claim) =>
        claim.Type == Type && claim.AttributeField == Field && claim.City == City && claim.ReturnQuestion == Question;
}

public static class MandatoryConstraints
{
    /// <summary>Mandatory constraints that need satisfying authoritative tool evidence: in stock always, plus every non-null constraint.</summary>
    public static ImmutableArray<RequiredClaim> For(ShopperConstraints c, DateTimeOffset asOf)
    {
        var required = new List<RequiredClaim>();

        void Attribute(string name, ProductAttributeField field, Func<string, bool> satisfied) =>
            required.Add(new(name, ClaimType.ProductAttribute, field, null, null, satisfied));

        if (c.Category is { } category)
        {
            Attribute("category", ProductAttributeField.Category, v => v == category.ToString());
        }

        required.Add(new("inStock", ClaimType.Availability, null, null, null, v => v == ClaimFormat.InStock));

        if (c.MaxPriceTry is { } maxPrice)
        {
            required.Add(new("maxPriceTry", ClaimType.Price, null, null, null, v => Decimal(v) is { } price && price <= maxPrice));
        }

        if (c.DeliverTo is { } city)
        {
            var deadline = c.DeliverWithinDays is { } days ? DeliveryCalculator.Deadline(asOf, days) : (DateOnly?)null;
            required.Add(new(c.DeliverWithinDays is null ? "deliverTo" : "deliverWithinDays", ClaimType.Delivery, null, city, null,
                v => Date(v) is { } date && (deadline is not { } d || date <= d)));
        }

        if (c.ProductType is { } productType)
        {
            Attribute("productType", ProductAttributeField.ProductType, v => v == productType);
        }

        if (c.TargetGroup is { } group)
        {
            Attribute("targetGroup", ProductAttributeField.TargetGroup, v => v == group || v == "Unisex");
        }

        if (c.ShoeSize is { } size)
        {
            Attribute("shoeSize", ProductAttributeField.ShoeSize, v => Decimal(v) == size);
        }

        if (c.Color is { } color)
        {
            Attribute("color", ProductAttributeField.Color, v => v == color);
        }

        if (c.NoiseCancellation is { } anc)
        {
            Attribute("noiseCancellation", ProductAttributeField.NoiseCancellation, v => v == InvariantFormat.Bool(anc));
        }

        if (c.Microphone is { } mic)
        {
            Attribute("microphone", ProductAttributeField.Microphone, v => v == InvariantFormat.Bool(mic));
        }

        if (c.MinimumWaterResistance is { } water)
        {
            Attribute("minimumWaterResistance", ProductAttributeField.WaterResistance,
                v => Enum.TryParse<WaterResistance>(v, ignoreCase: false, out var actual) && actual >= water);
        }

        AtLeast(required, "minimumLaptopSizeIn", ProductAttributeField.LaptopSizeIn, c.MinimumLaptopSizeIn);
        AtLeast(required, "minimumCapacityLiters", ProductAttributeField.CapacityLiters, c.MinimumCapacityLiters);
        AtLeast(required, "minimumBatteryHours", ProductAttributeField.BatteryHours, c.MinimumBatteryHours);
        AtLeast(required, "minimumWarrantyMonths", ProductAttributeField.WarrantyMonths, c.MinimumWarrantyMonths);

        if (c.InstallmentCount is { } installments)
        {
            required.Add(new("installmentCount", ClaimType.PaymentOption, null, null, null, v => Decimal(v) is { } max && max >= installments));
        }

        if (c.ReturnRequirement is { } requirement)
        {
            required.Add(new("returnRequirement", ClaimType.ReturnPolicy, null, null, requirement.Question,
                v => ParseReturn(v) is { } rule && ConstraintEvaluator.SatisfiesReturn(
                    new ReturnRule(default, requirement.Question, rule.Answer, null, rule.WindowDays), requirement)));
        }

        return [.. required];
    }

    public static (ReturnAnswer Answer, int? WindowDays)? ParseReturn(string value)
    {
        var parts = value.Split(':');
        if (!Enum.TryParse<ReturnAnswer>(parts[0], ignoreCase: false, out var answer))
        {
            return null;
        }

        return parts.Length == 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            ? (answer, days)
            : (answer, null);
    }

    private static void AtLeast(List<RequiredClaim> required, string name, ProductAttributeField field, decimal? minimum)
    {
        if (minimum is { } min)
        {
            required.Add(new(name, ClaimType.ProductAttribute, field, null, null, v => Decimal(v) is { } actual && actual >= min));
        }
    }

    private static decimal? Decimal(string value) => InvariantFormat.TryParseDecimal(value, out var d) ? d : null;

    private static DateOnly? Date(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
