using CommerceReadiness.Core.Shopping;

namespace CommerceReadiness.Core.Evaluation.Shopping;

public sealed record ShopperRunRecord(string IntentId, int RunIndex, ShopperRun Run);

public sealed record ClaimCheck(ClaimType Type, ProductAttributeField? Field, string Value, ClaimVerdict Verdict);

/// <param name="Detail">
/// Why the constraint is (not) grounded from the tool trace: e.g. missing evidence, evidence that does not satisfy
/// it, or the satisfying EvidenceCallId. Trace grounding is decision evidence, not a claim about model reasoning.
/// </param>
public sealed record ConstraintGrounding(string Constraint, bool Grounded, string Detail);

public sealed record RecommendationEvaluation(
    string Sku,
    bool IsPrimary,
    RecommendationConfidence Confidence,
    bool ValidInClean,
    ImmutableArray<string> CleanViolations,
    ImmutableArray<ConstraintGrounding> Constraints,
    ImmutableArray<ClaimCheck> Claims)
{
    public bool AllConstraintsGrounded => Constraints.All(c => c.Grounded);

    public bool AnyContradicted => Claims.Any(c => c.Verdict == ClaimVerdict.Contradicted);
}

public sealed record SearchFilterCheck(string Field, string? Expected, string? Actual, bool Correct);

/// <param name="GroundedRecommendation">Only for valid-product intents.</param>
/// <param name="GroundedNoMatch">Only for no-valid-product intents: NoMatch plus evidence for a decisive blocking constraint.</param>
/// <param name="ClarificationCorrect">Only for insufficient-information intents: every expected missing field was asked for.</param>
public sealed record RunEvaluation(
    string IntentId,
    int RunIndex,
    ShopperRunStatus Status,
    ShopperOutcome ExpectedOutcome,
    ShopperOutcome? ActualOutcome,
    bool OutcomeCorrect,
    ImmutableArray<RecommendationEvaluation> Recommendations,
    bool? GroundedRecommendation,
    bool? GroundedNoMatch,
    bool? ClarificationCorrect,
    ImmutableArray<ShopperMissingField> ExtraneousClarificationFields,
    ImmutableArray<SearchFilterCheck> SearchFilters,
    int ToolCalls,
    int FailedToolCalls);

/// <summary>
/// A rate over explicit units. <see cref="Value"/> is null (not applicable) when the denominator is zero.
/// <see cref="Flagged"/> lists failing units for higher-is-better metrics and counted units for lower-is-better ones.
/// </summary>
public sealed record ShopperMetric(
    string Name,
    string Definition,
    bool HigherIsBetter,
    int Numerator,
    int Denominator,
    double? Value,
    ImmutableArray<string> Flagged)
{
    public static ShopperMetric Rate(string name, string definition, bool higherIsBetter, IEnumerable<(string Unit, bool Counted)> units)
    {
        var list = units.ToList();
        var numerator = list.Count(u => u.Counted);
        return new ShopperMetric(
            name,
            definition,
            higherIsBetter,
            numerator,
            list.Count,
            list.Count == 0 ? null : Math.Round((double)numerator / list.Count, 4),
            [.. list.Where(u => u.Counted != higherIsBetter).Select(u => u.Unit)]);
    }
}

/// <param name="Outcomes">Per run, in run order: the outcome, or the run status when the run did not complete.</param>
public sealed record IntentStability(string IntentId, ShopperOutcome ExpectedOutcome, ImmutableArray<string> Outcomes, ImmutableArray<string> PrimarySkus, bool Stable);

/// <summary>
/// How a MisleadingDescription SKU fared in runs whose search results contained it. Evaluation-only; the shopper never
/// sees defect labels.
/// </summary>
public sealed record MisleadingDescriptionCase(
    string DefectId,
    string Sku,
    string Field,
    string? PublishedStructuredValue,
    int RunsExposed,
    int RunsWithDetailsFetched,
    int ConfirmedRecommendations,
    int ConfirmedInvalidRecommendations,
    int MisleadingAttributeClaims,
    int RejectedAfterDetails);

public sealed record UsageSummary(long? InputTokens, long? OutputTokens, long? TotalTokens, double AverageToolCallsPerRun, double AverageDurationMs);

public sealed record ShopperEvaluationReport(
    int AttemptedRuns,
    int Intents,
    ImmutableArray<ShopperMetric> Primary,
    ImmutableArray<ShopperMetric> Diagnostics,
    ImmutableSortedDictionary<string, int> ClaimVerdicts,
    ImmutableArray<IntentStability> Stability,
    ImmutableArray<MisleadingDescriptionCase> MisleadingDescription,
    UsageSummary Usage,
    ImmutableArray<RunEvaluation> Runs);
