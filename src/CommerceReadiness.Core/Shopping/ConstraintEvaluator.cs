using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Shopping;

public static class ConstraintEvaluator
{
    private const string Unknown = "unknown";

    /// <summary>
    /// Information without which no recommendation can be made safely:
    /// a category, a shoe size for sneakers, and a destination city when a delivery deadline is given.
    /// </summary>
    public static ImmutableArray<string> MissingInformation(ShopperConstraints constraints)
    {
        var missing = new List<string>();
        if (constraints.Category is null)
        {
            missing.Add("category");
        }

        if (constraints.Category == Category.Sneakers && constraints.ShoeSize is null)
        {
            missing.Add("shoeSize");
        }

        if (constraints.DeliverWithinDays is not null && constraints.DeliverTo is null)
        {
            missing.Add("deliverTo");
        }

        return [.. missing];
    }

    /// <summary>All mandatory constraints the SKU violates; empty means the SKU is valid.</summary>
    public static ImmutableArray<ConstraintViolation> Evaluate(
        ShopperConstraints c, string sku, ISkuFactSource facts, DateTimeOffset now)
    {
        var violations = new List<ConstraintViolation>();
        var category = facts.CategoryOf(sku);

        if (c.Category is { } wantedCategory && wantedCategory != category)
        {
            violations.Add(new("category", wantedCategory.ToString(), category.ToString()));
        }

        var inStock = facts.IsInStock(sku);
        if (inStock != true)
        {
            violations.Add(new("inStock", "true", inStock is null ? Unknown : InvariantFormat.Bool(inStock.Value)));
        }

        if (c.MaxPriceTry is { } maxPrice)
        {
            var price = facts.PriceOf(sku);
            if (price is null || price > maxPrice)
            {
                violations.Add(new("maxPriceTry", $"<= {InvariantFormat.Money(maxPrice)}", price is null ? Unknown : InvariantFormat.Money(price.Value)));
            }
        }

        if (c.DeliverTo is { } city)
        {
            var date = facts.DeliveryDate(sku, city);
            if (date is null)
            {
                violations.Add(new("deliverTo", city.ToString(), "notDeliverable"));
            }
            else if (c.DeliverWithinDays is { } days)
            {
                var deadline = DeliveryCalculator.Deadline(now, days);
                if (date.Value > deadline)
                {
                    violations.Add(new("deliverWithinDays", $"<= {InvariantFormat.Date(deadline)}", InvariantFormat.Date(date.Value)));
                }
            }
        }

        AtLeast(violations, "minimumLaptopSizeIn", facts.AttributeOf(sku, AttributeKeys.LaptopSizeIn), c.MinimumLaptopSizeIn);
        AtLeast(violations, "minimumCapacityLiters", facts.AttributeOf(sku, AttributeKeys.CapacityLiters), c.MinimumCapacityLiters);
        AtLeast(violations, "minimumBatteryHours", facts.AttributeOf(sku, AttributeKeys.BatteryHours), c.MinimumBatteryHours);
        AtLeast(violations, "minimumWarrantyMonths", facts.AttributeOf(sku, AttributeKeys.WarrantyMonths), c.MinimumWarrantyMonths);

        if (c.MinimumWaterResistance is { } minimumWater)
        {
            var raw = facts.AttributeOf(sku, AttributeKeys.WaterResistance);
            if (!Enum.TryParse(raw, ignoreCase: false, out WaterResistance actual) || actual < minimumWater)
            {
                violations.Add(new("minimumWaterResistance", $">= {minimumWater}", raw ?? Unknown));
            }
        }

        EqualTo(violations, "shoeSize", facts.AttributeOf(sku, AttributeKeys.ShoeSize), c.ShoeSize is { } size ? InvariantFormat.Number(size) : null);
        EqualTo(violations, "color", facts.AttributeOf(sku, AttributeKeys.Color), c.Color);
        EqualTo(violations, "productType", facts.AttributeOf(sku, AttributeKeys.ProductType), c.ProductType);
        EqualTo(violations, "noiseCancellation", facts.AttributeOf(sku, AttributeKeys.NoiseCancellation), c.NoiseCancellation is { } anc ? InvariantFormat.Bool(anc) : null);
        EqualTo(violations, "microphone", facts.AttributeOf(sku, AttributeKeys.Microphone), c.Microphone is { } mic ? InvariantFormat.Bool(mic) : null);

        if (c.TargetGroup is { } group)
        {
            var actual = facts.AttributeOf(sku, AttributeKeys.TargetGroup);
            if (actual != group && actual != "Unisex")
            {
                violations.Add(new("targetGroup", group, actual ?? Unknown));
            }
        }

        if (c.InstallmentCount is { } installments)
        {
            var max = facts.MaxInstallments(sku);
            if (max < installments)
            {
                violations.Add(new("installmentCount", $">= {InvariantFormat.Number(installments)}", InvariantFormat.Number(max)));
            }
        }

        if (c.ReturnRequirement is { } requirement)
        {
            var rule = facts.ReturnRuleFor(category, requirement.Question);
            if (!SatisfiesReturn(rule, requirement))
            {
                violations.Add(new("returnRequirement", Describe(requirement), Describe(rule)));
            }
        }

        return [.. violations];
    }

    public static bool SatisfiesReturn(ReturnRule? rule, ReturnRequirement requirement)
    {
        if (rule is null)
        {
            return false;
        }

        var answerAccepted = rule.Answer == ReturnAnswer.Yes
            || (rule.Answer == ReturnAnswer.Conditional && requirement.AllowConditional);
        var windowAccepted = requirement.MinimumWindowDays is not { } minimumDays || (rule.WindowDays ?? 0) >= minimumDays;
        return answerAccepted && windowAccepted;
    }

    private static void AtLeast(List<ConstraintViolation> violations, string name, string? actualText, decimal? minimum)
    {
        if (minimum is not { } min)
        {
            return;
        }

        if (!InvariantFormat.TryParseDecimal(actualText, out var actual) || actual < min)
        {
            violations.Add(new(name, $">= {InvariantFormat.Number(min)}", actualText ?? Unknown));
        }
    }

    private static void EqualTo(List<ConstraintViolation> violations, string name, string? actual, string? required)
    {
        if (required is not null && actual != required)
        {
            violations.Add(new(name, required, actual ?? Unknown));
        }
    }

    private static string Describe(ReturnRequirement requirement) =>
        $"{requirement.Question}: Yes{(requirement.AllowConditional ? " or Conditional" : string.Empty)}" +
        (requirement.MinimumWindowDays is { } days ? $", window >= {InvariantFormat.Number(days)} days" : string.Empty);

    private static string Describe(ReturnRule? rule) =>
        rule is null
            ? Unknown
            : rule.Answer + (rule.WindowDays is { } days ? $", window {InvariantFormat.Number(days)} days" : string.Empty);
}
