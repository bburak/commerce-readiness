using CommerceReadiness.Core.Analysis.Rules;
using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Analysis;

/// <summary>
/// Deterministic readiness analyzer. It sees only an observable store snapshot (normally the published store) and its
/// as-of time; it has no access to the clean store, ground truth or oracle results.
/// </summary>
public static class ReadinessAnalyzer
{
    public const string Version = "readiness-analyzer-v0";

    public static ImmutableArray<FindingType> SupportedFindingTypes { get; } = [.. Enum.GetValues<FindingType>()];

    /// <summary>Checks this baseline intentionally does not perform.</summary>
    public static ImmutableArray<string> NotAnalyzed { get; } = ["DescriptionSemanticConsistency"];

    public static ReadinessAnalysis Analyze(StoreSnapshot published, DateTimeOffset asOf)
    {
        if (published.Kind == StoreKind.Clean)
        {
            throw new ArgumentException(
                "The readiness analyzer only analyzes observable stores; the clean store is hidden ground truth.", nameof(published));
        }

        var findings = MissingRequiredAttributeRule.Evaluate(published)
            .Concat(InventoryConflictRule.Evaluate(published))
            .Concat(PriceConflictRule.Evaluate(published))
            .Concat(DeliveryConflictRule.Evaluate(published, asOf))
            .Concat(AmbiguousReturnRule.Evaluate(published))
            .OrderBy(f => f.FindingId, StringComparer.Ordinal)
            .ToImmutableArray();

        var duplicate = findings.GroupBy(f => f.FindingId, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Finding id '{duplicate.Key}' was produced more than once.");
        }

        var summary = new AnalysisSummary(
            Version,
            published.StoreVersion,
            asOf,
            published.Products.Length,
            published.Variants.Length,
            findings.Length,
            [.. Enum.GetValues<FindingSeverity>().Select(s => new FindingSeverityCount(s, findings.Count(f => f.Severity == s)))],
            [.. SupportedFindingTypes.Select(t => new FindingTypeCount(t, findings.Count(f => f.Type == t)))],
            NotAnalyzed);

        return new ReadinessAnalysis(summary, findings);
    }
}
