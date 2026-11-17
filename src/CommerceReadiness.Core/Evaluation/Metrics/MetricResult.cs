using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Evaluation.Metrics;

/// <summary>
/// NotEvaluable means the observed snapshot no longer holds enough information to evaluate the unit. It stays in
/// the denominator and counts as not passing, so removing data can never improve a score.
/// </summary>
public enum UnitStatus
{
    Passed,
    Failed,
    NotEvaluable,
}

public sealed record MetricUnit(string UnitId, UnitStatus Status, string? Detail = null)
{
    public bool Passed => Status == UnitStatus.Passed;

    public static MetricUnit Of(string unitId, bool passed, string? detail = null) =>
        new(unitId, passed ? UnitStatus.Passed : UnitStatus.Failed, detail);

    public static MetricUnit NotEvaluable(string unitId, string detail) => new(unitId, UnitStatus.NotEvaluable, detail);
}

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
        new(Name, Definition, Numerator, Denominator, Value,
            [.. Units.Where(u => u.Status == UnitStatus.Failed).Select(u => u.UnitId)],
            [.. Units.Where(u => u.Status == UnitStatus.NotEvaluable).Select(u => u.UnitId)]);
}

/// <remarks>Denominator - Numerator = FailedUnits + NotEvaluableUnits.</remarks>
public sealed record MetricSummary(
    string Name,
    string Definition,
    int Numerator,
    int Denominator,
    double? Value,
    ImmutableArray<string> FailedUnits,
    ImmutableArray<string> NotEvaluableUnits)
{
    public int NotPassing => Denominator - Numerator;

    public IEnumerable<string> NotPassingUnits() => FailedUnits.Concat(NotEvaluableUnits);
}

public sealed record StoreMetrics(
    StoreKind Store,
    MetricSummary CatalogCompleteness,
    MetricSummary SemanticCoverage,
    MetricSummary DataConsistency)
{
    public IEnumerable<string> AllNotPassingUnits() =>
        CatalogCompleteness.NotPassingUnits().Concat(SemanticCoverage.NotPassingUnits()).Concat(DataConsistency.NotPassingUnits());
}

/// <summary>
/// Defects and metric failures are counted separately: one defect can make several units fail
/// (for example a stale OUT_OF_STOCK row fails both its stock and its delivery unit).
/// </summary>
public sealed record DefectCoverage(
    int InjectedDefects,
    int DefectsWithExpectedImpact,
    int DefectsDetectedByFailingUnits,
    int CatalogCompletenessFailures,
    int SemanticCoverageFailures,
    int DataConsistencyFailures)
{
    public static DefectCoverage Compute(IReadOnlyCollection<InjectedDefect> defects, StoreMetrics observed)
    {
        var notPassing = observed.AllNotPassingUnits().ToHashSet(StringComparer.Ordinal);
        return new DefectCoverage(
            defects.Count,
            defects.Count(d => d.ExpectedImpacts.Length > 0),
            defects.Count(d => d.ExpectedImpacts.Any(notPassing.Contains)),
            observed.CatalogCompleteness.NotPassing,
            observed.SemanticCoverage.NotPassing,
            observed.DataConsistency.NotPassing);
    }
}

public sealed record MetricsReport(
    string StoreVersion,
    DateTimeOffset SimulationClock,
    ImmutableArray<StoreMetrics> Stores,
    DefectCoverage PublishedDefectCoverage);
