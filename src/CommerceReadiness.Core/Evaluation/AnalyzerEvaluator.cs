using CommerceReadiness.Core.Analysis;
using CommerceReadiness.Core.Catalog;

namespace CommerceReadiness.Core.Evaluation;

/// <summary>
/// Semantic identity of a case: what kind of issue, on which entity (SKU, product or policy cell), in which field.
/// Findings, defects and decoys are all reduced to this key; human-readable text and IDs never take part in matching.
/// </summary>
public sealed record CaseKey(DefectType Type, string Target, string Field);

public sealed record FindingMatch(string FindingId, string DefectId);

/// <param name="DuplicateOfDefectId">Set when the finding repeats a defect already matched by another finding.</param>
/// <param name="DecoyId">Set when the finding flags a known decoy.</param>
public sealed record FalsePositiveFinding(string FindingId, FindingType Type, string? DuplicateOfDefectId, string? DecoyId);

public sealed record UnmatchedDefect(string DefectId, DefectType Type);

public sealed record DefectTypeEvaluation(
    DefectType Type,
    bool Supported,
    int Injected,
    int Detected,
    int Findings,
    int FalsePositives);

/// <summary>
/// Precision = true-positive findings / findings.
/// Deterministic recall = detected defects of supported types / injected defects of supported types.
/// Overall recall = detected defects / all injected defects (unsupported semantic defects included).
/// Decoy false-positive rate = decoys flagged by at least one finding / decoys.
/// Rates are null when their denominator is 0.
/// </summary>
public sealed record AnalyzerEvaluation(
    string StoreVersion,
    string AnalyzerVersion,
    ImmutableArray<DefectType> SupportedDefectTypes,
    int FindingCount,
    int TruePositiveCount,
    int FalsePositiveCount,
    int DuplicateFindingCount,
    int InjectedDefectCount,
    int SupportedDefectCount,
    int DetectedDefectCount,
    int DetectedSupportedDefectCount,
    int FalseNegativeCount,
    int DeferredDefectCount,
    int DecoyCount,
    int DecoyHitCount,
    double? Precision,
    double? DeterministicRecall,
    double? OverallRecall,
    double? DecoyFalsePositiveRate,
    ImmutableArray<DefectTypeEvaluation> ByType,
    ImmutableArray<FindingMatch> Matches,
    ImmutableArray<FalsePositiveFinding> FalsePositives,
    ImmutableArray<UnmatchedDefect> FalseNegatives,
    ImmutableArray<UnmatchedDefect> DeferredDefects,
    ImmutableArray<string> DecoyHits);

/// <summary>
/// Scores analyzer findings against ground truth. This is the only place findings and injected defects meet;
/// the analyzer itself never sees ground truth.
/// </summary>
public static class AnalyzerEvaluator
{
    public static ImmutableArray<DefectType> SupportedDefectTypes { get; } =
        [.. ReadinessAnalyzer.SupportedFindingTypes.Select(DefectTypeOf).Distinct().Order()];

    public static AnalyzerEvaluation Evaluate(ReadinessAnalysis analysis, GroundTruth truth)
    {
        if (analysis.Summary.StoreVersion != truth.StoreVersion)
        {
            throw new ArgumentException(
                $"Analysis of '{analysis.Summary.StoreVersion}' cannot be evaluated against ground truth of '{truth.StoreVersion}'.",
                nameof(truth));
        }

        var supported = SupportedDefectTypes.ToHashSet();
        var defectsByKey = truth.Defects
            .OrderBy(d => d.DefectId, StringComparer.Ordinal)
            .GroupBy(KeyOf)
            .ToDictionary(g => g.Key, g => g.ToList());
        var decoysByKey = truth.Decoys
            .OrderBy(d => d.DecoyId, StringComparer.Ordinal)
            .GroupBy(KeyOf)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new Dictionary<string, string>(StringComparer.Ordinal);
        var matches = new List<FindingMatch>();
        var falsePositives = new List<FalsePositiveFinding>();
        var decoyHits = new SortedSet<string>(StringComparer.Ordinal);

        // One-to-one: a defect is matched by the first finding (in FindingId order) with its key; repeats are false positives.
        foreach (var finding in analysis.Findings.OrderBy(f => f.FindingId, StringComparer.Ordinal))
        {
            var key = KeyOf(finding);
            if (defectsByKey.TryGetValue(key, out var candidates))
            {
                var defect = candidates.FirstOrDefault(d => !matched.ContainsKey(d.DefectId));
                if (defect is not null)
                {
                    matched[defect.DefectId] = finding.FindingId;
                    matches.Add(new FindingMatch(finding.FindingId, defect.DefectId));
                }
                else
                {
                    falsePositives.Add(new FalsePositiveFinding(finding.FindingId, finding.Type, candidates[0].DefectId, null));
                }

                continue;
            }

            var decoy = decoysByKey.GetValueOrDefault(key);
            if (decoy is not null)
            {
                decoyHits.Add(decoy.DecoyId);
            }

            falsePositives.Add(new FalsePositiveFinding(finding.FindingId, finding.Type, null, decoy?.DecoyId));
        }

        var defects = truth.Defects.OrderBy(d => d.DefectId, StringComparer.Ordinal).ToList();
        var undetected = defects.Where(d => !matched.ContainsKey(d.DefectId)).ToList();
        var falseNegatives = undetected.Where(d => supported.Contains(d.Type)).Select(d => new UnmatchedDefect(d.DefectId, d.Type)).ToImmutableArray();
        var deferred = undetected.Where(d => !supported.Contains(d.Type)).Select(d => new UnmatchedDefect(d.DefectId, d.Type)).ToImmutableArray();

        var supportedDefects = defects.Count(d => supported.Contains(d.Type));
        var detectedSupported = defects.Count(d => supported.Contains(d.Type) && matched.ContainsKey(d.DefectId));
        var falsePositiveTypes = falsePositives.Select(f => DefectTypeOf(f.Type)).ToList();

        var byType = Enum.GetValues<DefectType>()
            .Select(type => new DefectTypeEvaluation(
                type,
                supported.Contains(type),
                defects.Count(d => d.Type == type),
                defects.Count(d => d.Type == type && matched.ContainsKey(d.DefectId)),
                analysis.Findings.Count(f => supported.Contains(type) && DefectTypeOf(f.Type) == type),
                falsePositiveTypes.Count(t => t == type)))
            .ToImmutableArray();

        return new AnalyzerEvaluation(
            truth.StoreVersion,
            analysis.Summary.AnalyzerVersion,
            SupportedDefectTypes,
            analysis.Findings.Length,
            matches.Count,
            falsePositives.Count,
            falsePositives.Count(f => f.DuplicateOfDefectId is not null),
            defects.Count,
            supportedDefects,
            matched.Count,
            detectedSupported,
            falseNegatives.Length,
            deferred.Length,
            truth.Decoys.Length,
            decoyHits.Count,
            Ratio(matches.Count, analysis.Findings.Length),
            Ratio(detectedSupported, supportedDefects),
            Ratio(matched.Count, defects.Count),
            Ratio(decoyHits.Count, truth.Decoys.Length),
            byType,
            [.. matches],
            [.. falsePositives],
            falseNegatives,
            deferred,
            [.. decoyHits]);
    }

    public static DefectType DefectTypeOf(FindingType type) => type switch
    {
        FindingType.MissingRequiredAttribute => DefectType.MissingRequiredAttribute,
        FindingType.InventoryConflict => DefectType.InventoryConflict,
        FindingType.PriceConflict => DefectType.PriceConflict,
        FindingType.DeliveryConflict => DefectType.DeliveryConflict,
        FindingType.AmbiguousReturnRule => DefectType.AmbiguousReturnRule,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static CaseKey KeyOf(Finding finding) =>
        KeyOf(DefectTypeOf(finding.Type), finding.ProductId, finding.Sku, finding.Policy, finding.Field);

    public static CaseKey KeyOf(InjectedDefect defect) =>
        KeyOf(defect.Type, defect.ProductId, defect.Sku, defect.Policy, defect.Field);

    public static CaseKey KeyOf(DecoyCase decoy) =>
        KeyOf(decoy.NearMissOf, decoy.ProductId, decoy.Sku, decoy.Policy, decoy.Field);

    /// <summary>
    /// Target rule: policy cell for return rules; SKU for variant-level missing attributes and for stock, price and
    /// delivery; product for product-level missing attributes and description claims.
    /// </summary>
    private static CaseKey KeyOf(DefectType type, string? productId, string? sku, string? policy, string field)
    {
        var target = type switch
        {
            DefectType.AmbiguousReturnRule => policy,
            DefectType.MissingRequiredAttribute => CategoryRequirements.IsVariantLevelKey(field) ? sku : productId,
            DefectType.MisleadingDescription => productId,
            _ => sku,
        };

        return new CaseKey(type, target ?? throw new InvalidOperationException($"{type} case on '{field}' has no target entity."), field);
    }

    private static double? Ratio(int numerator, int denominator) =>
        denominator == 0 ? null : Math.Round((double)numerator / denominator, 4);
}
