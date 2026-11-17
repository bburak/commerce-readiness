using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Shopping;

public enum Persona
{
    BudgetShopper,
    UrgentDelivery,
    ComparisonShopper,
    InstallmentSensitive,
    ReturnSensitive,
    SizeSensitive,
    GiftShopper,
    AvailabilitySensitive,
}

/// <summary>
/// What the oracle concluded for an intent. For <see cref="NoValidProduct"/> and
/// <see cref="InsufficientInformation"/> the correct future shopper behavior is to abstain
/// (or ask for the missing information) rather than violate a constraint.
/// </summary>
public enum IntentOutcome
{
    ValidProductsExist,
    NoValidProduct,
    InsufficientInformation,
}

/// <param name="AllowConditional">Whether a <see cref="ReturnAnswer.Conditional"/> policy satisfies the shopper.</param>
public sealed record ReturnRequirement(ReturnQuestion Question, bool AllowConditional, int? MinimumWindowDays = null);

/// <summary>
/// Hidden structured constraints of an intent. Only the oracle sees these; a future shopper only sees the text.
/// Every non-null constraint is mandatory. Being in stock is always mandatory.
/// </summary>
public sealed record ShopperConstraints
{
    public Category? Category { get; init; }
    public string? ProductType { get; init; }
    public decimal? MaxPriceTry { get; init; }
    public City? DeliverTo { get; init; }
    public int? DeliverWithinDays { get; init; }
    public decimal? MinimumLaptopSizeIn { get; init; }
    public decimal? MinimumCapacityLiters { get; init; }
    public WaterResistance? MinimumWaterResistance { get; init; }
    public int? ShoeSize { get; init; }
    public string? Color { get; init; }

    /// <summary>"Unisex" products also satisfy "Kadın" or "Erkek".</summary>
    public string? TargetGroup { get; init; }

    public int? MinimumBatteryHours { get; init; }
    public bool? NoiseCancellation { get; init; }
    public bool? Microphone { get; init; }
    public int? MinimumWarrantyMonths { get; init; }
    public int? InstallmentCount { get; init; }
    public ReturnRequirement? ReturnRequirement { get; init; }
}

public sealed record ConstraintViolation(string Constraint, string Required, string Actual);

/// <param name="LooksValidInPublished">
/// True when the published storefront data alone would make this SKU look valid: a trap for a shopper
/// that trusts broken published data.
/// </param>
public sealed record ForbiddenSku(string Sku, ImmutableArray<ConstraintViolation> Violations, bool LooksValidInPublished);

public sealed record IntentExpectation(
    IntentOutcome Outcome,
    ImmutableArray<string> ValidSkus,
    ImmutableArray<ForbiddenSku> Forbidden,
    ImmutableArray<string> MissingInformation)
{
    /// <summary>Convenience flag derived from <see cref="Outcome"/>.</summary>
    public bool NoValidAnswer => Outcome == IntentOutcome.NoValidProduct;

    /// <summary>Convenience flag derived from <see cref="Outcome"/>.</summary>
    public bool ShouldAbstain => Outcome != IntentOutcome.ValidProductsExist;
}

public sealed record ShopperIntent(
    string Id,
    Persona Persona,
    string Text,
    ShopperConstraints Constraints,
    IntentExpectation Expected);

public sealed record IntentsFile(string StoreVersion, DateTimeOffset SimulationClock, ImmutableArray<ShopperIntent> Intents);
