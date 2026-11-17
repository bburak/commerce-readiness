using CommerceReadiness.Core.Evaluation.Metrics;
using CommerceReadiness.Core.Time;
using CommerceReadiness.DemoStore;
using CommerceReadiness.DemoStore.Output;

var seed = SimulationContext.DefaultSeed;
string? output = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--seed" when i + 1 < args.Length:
            seed = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--out" when i + 1 < args.Length:
            output = args[++i];
            break;
        case "--help" or "-h":
            Console.WriteLine("Usage: CommerceReadiness.DemoStore [--seed <int>] [--out <directory>]");
            Console.WriteLine("Default output: demo-store/generated/seed-<seed>");
            return 0;
        default:
            Console.Error.WriteLine($"Unknown or incomplete argument '{args[i]}'. Use --help.");
            return 1;
    }
}

var directory = output ?? Path.Combine("demo-store", "generated", $"seed-{seed.ToString(CultureInfo.InvariantCulture)}");
var dataset = DemoStoreBuilder.Build(seed);
DatasetWriter.Write(dataset, directory);

var truth = dataset.GroundTruth;
Console.WriteLine($"Store version     : {truth.StoreVersion}");
Console.WriteLine($"Simulation clock  : {truth.SimulationClock.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)}");
Console.WriteLine($"Products / SKUs   : {dataset.Clean.Products.Length} / {dataset.Clean.Variants.Length}");
foreach (var group in dataset.Clean.Products.GroupBy(p => p.Category))
{
    var skus = dataset.Clean.Variants.Count(v => dataset.Clean.GetProduct(v.ProductId).Category == group.Key);
    Console.WriteLine($"  {group.Key,-17}: {group.Count()} products, {skus} SKUs");
}

Console.WriteLine($"Defects           : {truth.Defects.Length}");
foreach (var group in truth.Defects.GroupBy(d => d.Type).OrderBy(g => g.Key))
{
    Console.WriteLine($"  {group.Key,-25}: {group.Count()}");
}

Console.WriteLine($"Decoys            : {truth.Decoys.Length}");
Console.WriteLine($"Intents           : {dataset.Intents.Intents.Length}");
foreach (var group in dataset.Intents.Intents.GroupBy(i => i.Expected.Outcome).OrderBy(g => g.Key))
{
    Console.WriteLine($"  {group.Key,-25}: {group.Count()}");
}

Console.WriteLine("Metrics (numerator / denominator):");
foreach (var store in dataset.Metrics.Stores)
{
    Console.WriteLine($"  {store.Store}");
    foreach (var metric in new[] { store.CatalogCompleteness, store.SemanticCoverage, store.DataConsistency })
    {
        Console.WriteLine($"    {metric.Name,-20}: {metric.Numerator} / {metric.Denominator} = {Percent(metric)}" +
                          $"  (failed {metric.FailedUnits.Length}, not evaluable {metric.NotEvaluableUnits.Length})");
    }
}

var coverage = dataset.Metrics.PublishedDefectCoverage;
Console.WriteLine("Published defect coverage:");
Console.WriteLine($"  Unique injected defects         : {coverage.InjectedDefects}");
Console.WriteLine($"  Defects with expected impact    : {coverage.DefectsWithExpectedImpact}");
Console.WriteLine($"  Defects detected by failing units: {coverage.DefectsDetectedByFailingUnits}");
Console.WriteLine($"  CatalogCompleteness failures    : {coverage.CatalogCompletenessFailures}");
Console.WriteLine($"  SemanticCoverage failures       : {coverage.SemanticCoverageFailures}");
Console.WriteLine($"  DataConsistency failures        : {coverage.DataConsistencyFailures}");

Console.WriteLine($"Written to        : {Path.GetFullPath(directory)}");
return 0;

static string Percent(MetricSummary metric) =>
    metric.Value is { } value ? (value * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%" : "n/a";
