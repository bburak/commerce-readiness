using CommerceReadiness.Core.Evaluation.Retrieval;

namespace CommerceReadiness.Retrieval.Benchmark;

public static class BenchmarkReportPrinter
{
    public static void Print(RetrievalBenchmarkReport report, string path, TextWriter output)
    {
        var p = report.Provenance;
        output.WriteLine("Retrieval Benchmark");
        output.WriteLine(RetrievalCli.Rule);
        output.WriteLine($"Store {p.StoreVersion} | {p.EmbeddingProvider} {p.EmbeddingModel} ({p.EmbeddingDimensions} dims) | K={p.K} | index {p.Index}");
        if (p.RunTimestampUtc is { } timestamp)
        {
            output.WriteLine($"Observed run at {timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}");
        }

        output.WriteLine($"Scored cases {report.ScoredCases} (semantic-discriminating {report.SemanticDiscriminatingCases}); " +
                         $"no-relevant {report.NoRelevantProductCases} reported separately; insufficient {report.ExcludedInsufficientCases} excluded");
        output.WriteLine();
        output.WriteLine($"{"Mode",-15}{"Group",-25}{"Cases",6}{$"Recall@{p.K}",11}{$"Precision@{p.K}",14}{$"HitRate@{p.K}",12}");
        foreach (var mode in report.Modes)
        {
            Row(output, mode.Mode.ToString(), "Shopper-derived", mode.ShopperDerived);
            Row(output, string.Empty, "Discovery-only", mode.DiscoveryOnly);
            Row(output, string.Empty, "Combined", mode.Combined);
            Row(output, string.Empty, "Semantic-discriminating", mode.SemanticDiscriminating);
        }

        output.WriteLine();
        output.WriteLine($"Max achievable Recall@{p.K} (combined): {Format(report.Modes[0].Combined.MaxAchievableRecallAtK)}");
        output.WriteLine("No-relevant cases (not scored):");
        foreach (var mode in report.Modes)
        {
            var n = mode.NoRelevantProduct;
            output.WriteLine($"  {mode.Mode,-15} empty results {n.EmptyResults}/{n.Cases}, average returned {Format(n.AverageReturned)}");
        }

        output.WriteLine($"Written to {Path.GetFullPath(path)}");
    }

    private static void Row(TextWriter output, string mode, string group, RetrievalAggregate a) =>
        output.WriteLine($"{mode,-15}{group,-25}{a.Cases,6}{Format(a.RecallAtK),11}{Format(a.PrecisionAtK),14}{Format(a.HitRateAtK),12}");

    private static string Format(double? value) => value is { } v ? v.ToString("0.000", CultureInfo.InvariantCulture) : "n/a";
}
