using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Core.Shopping;

namespace CommerceReadiness.Core.Evaluation.Retrieval;

public enum RetrievalCaseGroup
{
    /// <summary>Projected from one of the shopper intents; query text is the intent text verbatim.</summary>
    ShopperDerived,

    /// <summary>Templates whose differentiating need is deliberately left to semantic retrieval.</summary>
    DiscoveryOnly,
}

/// <summary>
/// Only <see cref="RelevantProductsExist"/> cases are scored with Recall/Precision/HitRate. NoRelevantProduct cases are
/// reported separately (retrieval is candidate generation, not the final decision), and insufficient-information
/// cases are excluded because a shopper should ask a clarifying question first.
/// </summary>
public enum RetrievalOutcome
{
    RelevantProductsExist,
    NoRelevantProduct,
    InsufficientDiscoveryInformation,
}

/// <param name="Filters">Hard filters the search receives (exact constraints only).</param>
/// <param name="RelevanceConstraints">Hidden discovery constraints that define relevance; never given to search.</param>
/// <param name="HardFilterPoolSize">Clean SKUs that pass the hard filters; larger than the relevant count means semantic ranking has a job.</param>
public sealed record RetrievalBenchmarkCase(
    string CaseId,
    RetrievalCaseGroup Group,
    string? SourceIntentId,
    string QueryText,
    StructuredFilters Filters,
    ShopperConstraints RelevanceConstraints,
    ImmutableArray<string> RelevantSkus,
    RetrievalOutcome Outcome,
    int HardFilterPoolSize)
{
    public bool IsSemanticDiscriminating => Outcome == RetrievalOutcome.RelevantProductsExist && HardFilterPoolSize > RelevantSkus.Length;
}

public sealed record RetrievalBenchmarkFile(int SchemaVersion, string StoreVersion, DateTimeOffset AsOf, ImmutableArray<RetrievalBenchmarkCase> Cases)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// Retrieval-stage policy over shopper constraints. Discovery constraints define relevance; of those, only exact
/// ones become hard filters. Delivery, installment and return constraints belong to later shopper tools and are
/// excluded from retrieval relevance.
/// </summary>
public static class RetrievalPolicy
{
    public static ShopperConstraints DiscoveryProjection(ShopperConstraints constraints) => constraints with
    {
        DeliverTo = null,
        DeliverWithinDays = null,
        InstallmentCount = null,
        ReturnRequirement = null,
    };

    public static StructuredFilters HardFilters(ShopperConstraints constraints) => new()
    {
        Category = constraints.Category,
        MaxPriceTry = constraints.MaxPriceTry,
        InStockOnly = true,
        ShoeSize = constraints.ShoeSize,
        Color = constraints.Color,
        MinimumLaptopSizeIn = constraints.MinimumLaptopSizeIn,
        MinimumCapacityLiters = constraints.MinimumCapacityLiters,
        MinimumBatteryHours = constraints.MinimumBatteryHours,
        MinimumWarrantyMonths = constraints.MinimumWarrantyMonths,
    };

    /// <summary>The hard-filter subset expressed as shopper constraints, so the oracle can count the filtered pool.</summary>
    public static ShopperConstraints HardFilterProjection(ShopperConstraints constraints) => new()
    {
        Category = constraints.Category,
        MaxPriceTry = constraints.MaxPriceTry,
        ShoeSize = constraints.ShoeSize,
        Color = constraints.Color,
        MinimumLaptopSizeIn = constraints.MinimumLaptopSizeIn,
        MinimumCapacityLiters = constraints.MinimumCapacityLiters,
        MinimumBatteryHours = constraints.MinimumBatteryHours,
        MinimumWarrantyMonths = constraints.MinimumWarrantyMonths,
    };
}

/// <summary>
/// Evaluation-only: computes relevant SKUs from the hidden clean store by reusing the shopper constraint semantics.
/// Relevant = in stock (authoritative inventory) and violating no discovery constraint.
/// </summary>
public static class RetrievalRelevanceOracle
{
    public static ImmutableArray<string> RelevantSkus(StoreSnapshot clean, ShopperConstraints discoveryConstraints, DateTimeOffset asOf)
    {
        var facts = new AuthoritativeFacts(clean, asOf);
        return [.. facts.AllSkus.Where(sku =>
            (discoveryConstraints.Category is not { } category || facts.CategoryOf(sku) == category)
            && ConstraintEvaluator.Evaluate(discoveryConstraints, sku, facts, asOf).IsEmpty)];
    }

    public static RetrievalBenchmarkCase CreateCase(
        StoreSnapshot clean,
        DateTimeOffset asOf,
        string caseId,
        RetrievalCaseGroup group,
        string? sourceIntentId,
        string queryText,
        ShopperConstraints constraints,
        bool insufficientInformation)
    {
        var relevance = RetrievalPolicy.DiscoveryProjection(constraints);
        var relevant = insufficientInformation ? [] : RelevantSkus(clean, relevance, asOf);
        var pool = insufficientInformation ? 0 : RelevantSkus(clean, RetrievalPolicy.HardFilterProjection(relevance), asOf).Length;
        var outcome = insufficientInformation
            ? RetrievalOutcome.InsufficientDiscoveryInformation
            : relevant.IsEmpty ? RetrievalOutcome.NoRelevantProduct : RetrievalOutcome.RelevantProductsExist;
        return new RetrievalBenchmarkCase(
            caseId, group, sourceIntentId, queryText, RetrievalPolicy.HardFilters(relevance), relevance, relevant, outcome, pool);
    }
}
