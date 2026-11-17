using CommerceReadiness.Analyzer;
using CommerceReadiness.Core.Analysis;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.DemoStore;

namespace CommerceReadiness.Tests;

public class AnalyzerDeterminismTests
{
    [Fact]
    public void Finding_ids_are_unique_and_stable()
    {
        var first = ReadinessAnalyzer.Analyze(TestData.Seed42.Published, TestData.Seed42.Context.Now);
        var second = ReadinessAnalyzer.Analyze(DemoStoreBuilder.Build(42).Published, TestData.Seed42.Context.Now);

        Assert.Equal(first.Findings.Length, first.Findings.Select(f => f.FindingId).Distinct().Count());
        Assert.Equal(first.Findings.Select(f => f.FindingId), second.Findings.Select(f => f.FindingId));
        Assert.Equal(first.Findings.Select(f => f.FindingId).Order(StringComparer.Ordinal), first.Findings.Select(f => f.FindingId));
    }

    [Fact]
    public void Same_dataset_produces_byte_identical_analysis_files()
    {
        var dataset = TestData.NewSeed42DatasetDirectory();
        var first = TestData.NewTempDirectory();
        var second = TestData.NewTempDirectory();

        Assert.Equal(0, AnalyzerCli.Run(["--dataset", dataset, "--out", first, "--evaluate"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(0, AnalyzerCli.Run(["--dataset", dataset, "--out", second, "--evaluate"], TextWriter.Null, TextWriter.Null));

        var firstFiles = TestData.ReadAllFiles(first);
        var secondFiles = TestData.ReadAllFiles(second);
        Assert.Equal(
            [AnalysisWriter.EvaluationFileName, AnalysisWriter.FindingsFileName, AnalysisWriter.SummaryFileName],
            firstFiles.Keys);
        Assert.All(firstFiles, file => Assert.True(file.Value.AsSpan().SequenceEqual(secondFiles[file.Key]), $"{file.Key} differs."));
    }

    public static TheoryData<int> ManySeeds() => [.. Enumerable.Range(1, 40)];

    [Theory]
    [MemberData(nameof(ManySeeds))]
    public void Any_seed_is_analyzed_generically_with_full_deterministic_recall_and_precision(int seed)
    {
        var dataset = DemoStoreBuilder.Build(seed);

        var analysis = ReadinessAnalyzer.Analyze(dataset.Published, dataset.Context.Now);
        var evaluation = AnalyzerEvaluator.Evaluate(analysis, dataset.GroundTruth);

        Assert.Equal(1.0, evaluation.Precision);
        Assert.Equal(1.0, evaluation.DeterministicRecall);
        Assert.Equal(0, evaluation.DecoyHitCount);
        Assert.Equal(0, evaluation.DuplicateFindingCount);
        Assert.Equal(evaluation.SupportedDefectCount, analysis.Findings.Length);
        Assert.Equal(
            Math.Round((double)evaluation.SupportedDefectCount / evaluation.InjectedDefectCount, 4),
            evaluation.OverallRecall);
    }

    [Fact]
    public void Analysis_json_round_trips()
    {
        var json = JsonDefaults.Serialize(TestData.Seed42Analysis);

        Assert.Equal(json, JsonDefaults.Serialize(JsonDefaults.Deserialize<ReadinessAnalysis>(json)));
        Assert.Equal(
            JsonDefaults.Serialize(TestData.Seed42Evaluation),
            JsonDefaults.Serialize(JsonDefaults.Deserialize<AnalyzerEvaluation>(JsonDefaults.Serialize(TestData.Seed42Evaluation))));
    }
}
