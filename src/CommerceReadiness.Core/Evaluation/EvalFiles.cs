using CommerceReadiness.Core.Evaluation.Metrics;
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

    public static GroundTruth ReadGroundTruth(string evalDirectory) =>
        JsonDefaults.Deserialize<GroundTruth>(File.ReadAllText(Path.Combine(evalDirectory, GroundTruthFileName)));

    public static IntentsFile ReadIntents(string evalDirectory) =>
        JsonDefaults.Deserialize<IntentsFile>(File.ReadAllText(Path.Combine(evalDirectory, IntentsFileName)));

    public static MetricsReport ReadMetrics(string evalDirectory) =>
        JsonDefaults.Deserialize<MetricsReport>(File.ReadAllText(Path.Combine(evalDirectory, MetricsFileName)));
}
