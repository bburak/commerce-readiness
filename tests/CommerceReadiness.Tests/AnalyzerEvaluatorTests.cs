using CommerceReadiness.Core.Analysis;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Storage;

namespace CommerceReadiness.Tests;

public class AnalyzerEvaluatorTests
{
    private static readonly ReadinessAnalysis Analysis = TestData.Seed42Analysis;
    private static readonly GroundTruth Truth = TestData.Seed42.GroundTruth;

    [Fact]
    public void Seed_42_evaluation_reports_correct_true_and_false_positives_and_negatives()
    {
        var evaluation = TestData.Seed42Evaluation;

        Assert.Equal(20, evaluation.FindingCount);
        Assert.Equal(20, evaluation.TruePositiveCount);
        Assert.Equal(0, evaluation.FalsePositiveCount);
        Assert.Equal(0, evaluation.DuplicateFindingCount);
        Assert.Equal(0, evaluation.FalseNegativeCount);
        Assert.Equal(24, evaluation.InjectedDefectCount);
        Assert.Equal(20, evaluation.SupportedDefectCount);
        Assert.Equal(4, evaluation.DeferredDefectCount);
        Assert.Equal(1.0, evaluation.Precision);
        Assert.Equal(1.0, evaluation.DeterministicRecall);
        Assert.Equal(0.8333, evaluation.OverallRecall);
        Assert.Equal(0.0, evaluation.DecoyFalsePositiveRate);
        Assert.Equal(evaluation.FindingCount, evaluation.Matches.Length);
    }

    [Fact]
    public void Deterministic_recall_covers_only_supported_types_and_overall_recall_covers_all()
    {
        var evaluation = TestData.Seed42Evaluation;
        var supported = AnalyzerEvaluator.SupportedDefectTypes;

        Assert.DoesNotContain(DefectType.MisleadingDescription, supported);
        Assert.Equal(Truth.Defects.Count(d => supported.Contains(d.Type)), evaluation.SupportedDefectCount);
        Assert.Equal(
            Math.Round((double)evaluation.DetectedSupportedDefectCount / evaluation.SupportedDefectCount, 4),
            evaluation.DeterministicRecall);
        Assert.Equal(Math.Round((double)evaluation.DetectedDefectCount / Truth.Defects.Length, 4), evaluation.OverallRecall);
        Assert.All(evaluation.DeferredDefects, d => Assert.Equal(DefectType.MisleadingDescription, d.Type));
        Assert.Empty(evaluation.FalseNegatives);
    }

    [Fact]
    public void An_unmatched_finding_is_a_false_positive()
    {
        var bogus = PriceFinding(UntouchedSku(), "FND-PRC-BOGUS");

        var evaluation = AnalyzerEvaluator.Evaluate(Analysis with { Findings = [.. Analysis.Findings, bogus] }, Truth);

        Assert.Equal(1, evaluation.FalsePositiveCount);
        Assert.Equal(20, evaluation.TruePositiveCount);
        Assert.Equal(Math.Round(20.0 / 21, 4), evaluation.Precision);
        var falsePositive = Assert.Single(evaluation.FalsePositives);
        Assert.Null(falsePositive.DuplicateOfDefectId);
        Assert.Null(falsePositive.DecoyId);
    }

    [Fact]
    public void A_missing_finding_is_a_false_negative()
    {
        var dropped = Analysis.Findings[0];
        var evaluation = AnalyzerEvaluator.Evaluate(Analysis with { Findings = Analysis.Findings.RemoveAt(0) }, Truth);

        Assert.Equal(1, evaluation.FalseNegativeCount);
        Assert.Equal(Math.Round(19.0 / 20, 4), evaluation.DeterministicRecall);
        Assert.Equal(Math.Round(19.0 / 24, 4), evaluation.OverallRecall);
        Assert.Equal(1.0, evaluation.Precision);
        Assert.Equal(TestData.Seed42Evaluation.Matches.Single(m => m.FindingId == dropped.FindingId).DefectId, evaluation.FalseNegatives.Single().DefectId);
    }

    [Fact]
    public void Duplicate_findings_for_one_defect_count_once_and_the_rest_are_false_positives()
    {
        var original = Analysis.Findings.First(f => f.Type == FindingType.InventoryConflict);
        var duplicate = original with { FindingId = original.FindingId + "-DUP" };

        var evaluation = AnalyzerEvaluator.Evaluate(Analysis with { Findings = [.. Analysis.Findings, duplicate] }, Truth);

        Assert.Equal(20, evaluation.TruePositiveCount);
        Assert.Equal(20, evaluation.DetectedSupportedDefectCount);
        Assert.Equal(1, evaluation.FalsePositiveCount);
        Assert.Equal(1, evaluation.DuplicateFindingCount);
        Assert.Equal(Math.Round(20.0 / 21, 4), evaluation.Precision);
        var falsePositive = Assert.Single(evaluation.FalsePositives);
        Assert.Equal(duplicate.FindingId, falsePositive.FindingId);
        Assert.Equal(TestData.Seed42Evaluation.Matches.Single(m => m.FindingId == original.FindingId).DefectId, falsePositive.DuplicateOfDefectId);
        Assert.Contains(evaluation.Matches, m => m.FindingId == original.FindingId);
    }

    [Fact]
    public void Findings_on_a_decoy_are_false_positives_and_the_decoy_counts_once()
    {
        var decoy = Truth.Decoys.First(d => d.NearMissOf == DefectType.InventoryConflict);
        Finding OnDecoy(string id) => new(
            id, FindingType.InventoryConflict, FindingSeverity.High, decoy.ProductId, decoy.Sku, null,
            PublishedFields.StockStatus, "test", [], "test");

        var evaluation = AnalyzerEvaluator.Evaluate(
            Analysis with { Findings = [.. Analysis.Findings, OnDecoy("FND-X-1"), OnDecoy("FND-X-2")] }, Truth);

        Assert.Equal(2, evaluation.FalsePositiveCount);
        Assert.Equal(1, evaluation.DecoyHitCount);
        Assert.Equal(Math.Round(1.0 / Truth.Decoys.Length, 4), evaluation.DecoyFalsePositiveRate);
        Assert.Equal([decoy.DecoyId], evaluation.DecoyHits.ToArray());
        Assert.All(evaluation.FalsePositives, f => Assert.Equal(decoy.DecoyId, f.DecoyId));
    }

    [Fact]
    public void Evaluation_refuses_ground_truth_of_another_store()
    {
        var other = TestData.Seed42.GroundTruth with { StoreVersion = "demostore-v0-seed-7" };

        Assert.Throws<ArgumentException>(() => AnalyzerEvaluator.Evaluate(Analysis, other));
    }

    [Fact]
    public void Matching_uses_semantic_identity_not_text_or_ids()
    {
        var renamed = Analysis.Findings
            .Select((f, i) => f with { FindingId = $"FND-{i:000}", Summary = "renamed", SuggestedFix = "renamed" })
            .ToImmutableArray();

        var evaluation = AnalyzerEvaluator.Evaluate(Analysis with { Findings = renamed }, Truth);

        Assert.Equal(20, evaluation.TruePositiveCount);
        Assert.Equal(0, evaluation.FalsePositiveCount);
    }

    private static string UntouchedSku()
    {
        var used = Truth.Defects.Select(d => d.Sku).Concat(Truth.Decoys.Select(d => d.Sku)).ToHashSet(StringComparer.Ordinal);
        return TestData.Seed42.Published.Listings.First(l => !used.Contains(l.Sku)).Sku;
    }

    private static Finding PriceFinding(string sku, string id) => new(
        id, FindingType.PriceConflict, FindingSeverity.High, TestData.Seed42.Published.GetVariant(sku).ProductId, sku, null,
        PublishedFields.ListedPrice, "test", [], "test");
}
