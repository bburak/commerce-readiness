using CommerceReadiness.Core.Analysis;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Storage;

namespace CommerceReadiness.Analyzer;

/// <summary>
/// The only part of the CLI that reads hidden ground truth, and only after analysis has finished.
/// </summary>
public static class EvaluateCommand
{
    public static void Run(string datasetDirectory, ReadinessAnalysis analysis, string outDirectory, TextWriter output)
    {
        var evalDirectory = Path.Combine(datasetDirectory, EvalFiles.Folder);
        var groundTruthPath = Path.Combine(evalDirectory, EvalFiles.GroundTruthFileName);
        if (!File.Exists(groundTruthPath))
        {
            throw new FileNotFoundException($"--evaluate needs ground truth at '{groundTruthPath}'.", groundTruthPath);
        }

        var evaluation = AnalyzerEvaluator.Evaluate(analysis, EvalFiles.ReadGroundTruth(evalDirectory));
        TextFiles.Write(Path.Combine(outDirectory, AnalysisWriter.EvaluationFileName), JsonDefaults.Serialize(evaluation));

        output.WriteLine();
        output.WriteLine("Evaluation (synthetic ground truth)");
        output.WriteLine(AnalyzerCli.Rule);
        AnalyzerCli.Row(output, "Deterministic defects", AnalyzerCli.Number(evaluation.SupportedDefectCount));
        AnalyzerCli.Row(output, "Detected", AnalyzerCli.Number(evaluation.DetectedSupportedDefectCount));
        AnalyzerCli.Row(output, "False positives", AnalyzerCli.Number(evaluation.FalsePositiveCount));
        AnalyzerCli.Row(output, "  Duplicate findings", AnalyzerCli.Number(evaluation.DuplicateFindingCount));
        AnalyzerCli.Row(output, "False negatives", AnalyzerCli.Number(evaluation.FalseNegativeCount));
        AnalyzerCli.Row(output, "Decoys", AnalyzerCli.Number(evaluation.DecoyCount));
        AnalyzerCli.Row(output, "Decoys flagged", AnalyzerCli.Number(evaluation.DecoyHitCount));
        output.WriteLine();
        AnalyzerCli.Row(output, "Deterministic recall", Percent(evaluation.DeterministicRecall));
        AnalyzerCli.Row(output, "Overall defect recall", Percent(evaluation.OverallRecall));
        AnalyzerCli.Row(output, "Precision", Percent(evaluation.Precision));

        var deferred = evaluation.ByType.Where(t => !t.Supported && t.Injected > 0).ToList();
        if (deferred.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Deferred (not supported by the deterministic baseline)");
            foreach (var type in deferred)
            {
                AnalyzerCli.Row(output, $"  {type.Type}", AnalyzerCli.Number(type.Injected));
            }
        }
    }

    private static string Percent(double? value) =>
        value is { } v ? (v * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "n/a";
}
