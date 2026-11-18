using CommerceReadiness.Core.SourceOfTruth;

namespace CommerceReadiness.Core.Analysis;

/// <summary>
/// Issues the deterministic analyzer can detect from published data alone. Semantic contradictions between
/// description text and structured data are intentionally not part of this baseline.
/// </summary>
public enum FindingType
{
    MissingRequiredAttribute,
    InventoryConflict,
    PriceConflict,
    DeliveryConflict,
    AmbiguousReturnRule,
}

public enum FindingSeverity
{
    High,
    Medium,
}

/// <summary>
/// V0 product heuristic, not a universal commerce risk score: conflicts that can make a shopper buy something
/// unavailable, at the wrong price or with a wrong delivery date are High; gaps that block a confident answer are Medium.
/// </summary>
public static class FindingSeverities
{
    public static FindingSeverity For(FindingType type) => type switch
    {
        FindingType.InventoryConflict or FindingType.PriceConflict or FindingType.DeliveryConflict => FindingSeverity.High,
        FindingType.MissingRequiredAttribute or FindingType.AmbiguousReturnRule => FindingSeverity.Medium,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}

/// <summary>One observed value the finding is based on, with the source it was read from.</summary>
public sealed record FindingEvidence(DataSource Source, string Field, string? Value);

/// <param name="FindingId">Derived only from finding type and the observable entity and field it concerns.</param>
/// <param name="ProductId">Set for every product or SKU finding. A finding with ProductId but no Sku concerns the whole product.</param>
/// <param name="Policy">Return-policy cell (category:question) for policy findings.</param>
public sealed record Finding(
    string FindingId,
    FindingType Type,
    FindingSeverity Severity,
    string? ProductId,
    string? Sku,
    string? Policy,
    string Field,
    string Summary,
    ImmutableArray<FindingEvidence> Evidence,
    string SuggestedFix);

public sealed record FindingTypeCount(FindingType Type, int Count);

public sealed record FindingSeverityCount(FindingSeverity Severity, int Count);

public sealed record AnalysisSummary(
    string AnalyzerVersion,
    string StoreVersion,
    DateTimeOffset AsOf,
    int ProductsAnalyzed,
    int SkusAnalyzed,
    int TotalFindings,
    ImmutableArray<FindingSeverityCount> BySeverity,
    ImmutableArray<FindingTypeCount> ByType,
    ImmutableArray<string> NotAnalyzed);

public sealed record ReadinessAnalysis(AnalysisSummary Summary, ImmutableArray<Finding> Findings);
