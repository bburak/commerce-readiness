using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Evaluation.Metrics;

public sealed record MetricUnit(string UnitId, bool Passed, string? Detail = null);

/// <summary>
/// A metric is always a count of passed evaluable units over a count of applicable units.
/// <see cref="Value"/> is null when no unit is applicable.
/// </summary>
public sealed record MetricResult(string Name, string Definition, ImmutableArray<MetricUnit> Units)
{
    public int Numerator => Units.Count(u => u.Passed);

    public int Denominator => Units.Length;

    public double? Value => Denominator == 0 ? null : Math.Round((double)Numerator / Denominator, 4);

    public MetricSummary ToSummary() =>
        new(Name, Definition, Numerator, Denominator, Value, [.. Units.Where(u => !u.Passed).Select(u => u.UnitId)]);
}

public sealed record MetricSummary(
    string Name,
    string Definition,
    int Numerator,
    int Denominator,
    double? Value,
    ImmutableArray<string> FailedUnits);

public sealed record StoreMetrics(
    StoreKind Store,
    MetricSummary CatalogCompleteness,
    MetricSummary SemanticCoverage,
    MetricSummary DataConsistency);

public sealed record MetricsReport(string StoreVersion, DateTimeOffset SimulationClock, ImmutableArray<StoreMetrics> Stores);
