using CommerceReadiness.Core.Evaluation.Metrics;
using CommerceReadiness.Core.Evaluation.Retrieval;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Shopping;

namespace CommerceReadiness.Core.Evaluation;

/// <summary>The hidden evaluation folder of a dataset. Only generators and evaluators may touch it.</summary>
public static class EvalFiles
{
    public const string Folder = "eval";
    public const string GroundTruthFileName = "ground-truth.json";
    public const string IntentsFileName = "intents.json";
    public const string MetricsFileName = "metrics.json";
    public const string RetrievalBenchmarkFileName = "retrieval-benchmark.json";

    public static GroundTruth ReadGroundTruth(string evalDirectory) =>
        JsonDefaults.Deserialize<GroundTruth>(File.ReadAllText(Path.Combine(evalDirectory, GroundTruthFileName)));

    public static IntentsFile ReadIntents(string evalDirectory) =>
        JsonDefaults.Deserialize<IntentsFile>(File.ReadAllText(Path.Combine(evalDirectory, IntentsFileName)));

    public static MetricsReport ReadMetrics(string evalDirectory) =>
        JsonDefaults.Deserialize<MetricsReport>(File.ReadAllText(Path.Combine(evalDirectory, MetricsFileName)));

    /// <exception cref="NotSupportedException">The benchmark file declares a schema version this build cannot read.</exception>
    public static RetrievalBenchmarkFile ReadRetrievalBenchmark(string evalDirectory)
    {
        var file = JsonDefaults.Deserialize<RetrievalBenchmarkFile>(File.ReadAllText(Path.Combine(evalDirectory, RetrievalBenchmarkFileName)));
        return file.SchemaVersion == RetrievalBenchmarkFile.CurrentSchemaVersion
            ? file
            : throw new NotSupportedException(
                $"Unsupported retrieval benchmark schema version {file.SchemaVersion}; this build supports {RetrievalBenchmarkFile.CurrentSchemaVersion}.");
    }
}
