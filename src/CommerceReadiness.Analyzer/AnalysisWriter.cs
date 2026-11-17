using CommerceReadiness.Core.Analysis;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Storage;

namespace CommerceReadiness.Analyzer;

public sealed record FindingsFile(string AnalyzerVersion, string StoreVersion, DateTimeOffset AsOf, ImmutableArray<Finding> Findings);

public static class AnalysisWriter
{
    public const string FindingsFileName = "findings.json";
    public const string SummaryFileName = "summary.json";
    public const string EvaluationFileName = "evaluation.json";

    public static void Write(ReadinessAnalysis analysis, string directory)
    {
        Directory.CreateDirectory(directory);
        var summary = analysis.Summary;
        TextFiles.Write(
            Path.Combine(directory, FindingsFileName),
            JsonDefaults.Serialize(new FindingsFile(summary.AnalyzerVersion, summary.StoreVersion, summary.AsOf, analysis.Findings)));
        TextFiles.Write(Path.Combine(directory, SummaryFileName), JsonDefaults.Serialize(summary));
    }
}
