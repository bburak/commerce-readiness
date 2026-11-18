using CommerceReadiness.Core.Analysis;
using CommerceReadiness.Core.Storage;

namespace CommerceReadiness.Analyzer;

/// <summary>
/// Analyze: published/ -> findings. Evaluation against ground truth happens only with --evaluate,
/// in <see cref="EvaluateCommand"/>.
/// </summary>
public static class AnalyzerCli
{
    public const string Rule = "───────────────────────────";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var dataset = Path.Combine("demo-store", "generated", "seed-42");
        string? outDirectory = null;
        var evaluate = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dataset" when i + 1 < args.Length:
                    dataset = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    outDirectory = args[++i];
                    break;
                case "--evaluate":
                    evaluate = true;
                    break;
                case "--help" or "-h":
                    output.WriteLine("Usage: CommerceReadiness.Analyzer [--dataset <dir>] [--out <dir>] [--evaluate]");
                    output.WriteLine("Reads only <dataset>/published/. --evaluate additionally scores findings against the synthetic ground truth.");
                    return 0;
                default:
                    error.WriteLine($"Unknown or incomplete argument '{args[i]}'. Use --help.");
                    return 1;
            }
        }

        try
        {
            var loaded = PublishedDatasetLoader.Load(dataset);
            var analysis = ReadinessAnalyzer.Analyze(loaded.Store, loaded.Manifest.AsOf);
            var directory = outDirectory ?? Path.Combine(dataset, DatasetLayout.AnalysisFolder);
            AnalysisWriter.Write(analysis, directory);
            PrintAnalysis(analysis, directory, output);

            if (evaluate)
            {
                EvaluateCommand.Run(dataset, analysis, directory, output);
            }
            else
            {
                output.WriteLine();
                output.WriteLine("Run with --evaluate to compare against the synthetic ground truth.");
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidDataException or ArgumentException or System.Text.Json.JsonException)
        {
            error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static void PrintAnalysis(ReadinessAnalysis analysis, string directory, TextWriter output)
    {
        var summary = analysis.Summary;
        output.WriteLine("Commerce Readiness Analysis");
        output.WriteLine(Rule);
        Row(output, "Store", summary.StoreVersion);
        Row(output, "As of", summary.AsOf.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
        Row(output, "Products analyzed", Number(summary.ProductsAnalyzed));
        Row(output, "SKUs analyzed", Number(summary.SkusAnalyzed));
        Row(output, "Findings", Number(summary.TotalFindings));
        foreach (var severity in summary.BySeverity)
        {
            Row(output, $"  {severity.Severity} severity", Number(severity.Count));
        }

        output.WriteLine();
        output.WriteLine("By type");
        foreach (var type in summary.ByType)
        {
            Row(output, $"  {Label(type.Type)}", Number(type.Count));
        }

        output.WriteLine();
        Row(output, "Not analyzed", string.Join(", ", summary.NotAnalyzed) + " (needs semantic analysis)");
        Row(output, "Written to", Path.GetFullPath(directory));
    }

    internal static void Row(TextWriter output, string label, string value) => output.WriteLine($"{label,-32}{value}");

    internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Label(FindingType type) => type switch
    {
        FindingType.MissingRequiredAttribute => "Missing required attributes",
        FindingType.InventoryConflict => "Inventory conflicts",
        FindingType.PriceConflict => "Price conflicts",
        FindingType.DeliveryConflict => "Delivery conflicts",
        FindingType.AmbiguousReturnRule => "Ambiguous return rules",
        _ => type.ToString(),
    };
}
