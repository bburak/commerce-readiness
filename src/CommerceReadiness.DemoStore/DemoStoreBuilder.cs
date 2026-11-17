using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Evaluation.Metrics;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Core.SourceOfTruth;
using CommerceReadiness.Core.Time;
using CommerceReadiness.DemoStore.Generation;

namespace CommerceReadiness.DemoStore;

public sealed record DemoStoreDataset(
    SimulationContext Context,
    StoreSnapshot Clean,
    StoreSnapshot Published,
    GroundTruth GroundTruth,
    IntentsFile Intents,
    MetricsReport Metrics);

/// <summary>
/// Clean store, then decoy selection, then defect injection into the published store, then oracle-labelled intents
/// and metrics. Each stage uses its own forked random stream.
/// </summary>
public static class DemoStoreBuilder
{
    public static DemoStoreDataset Build(int seed) => Build(SimulationContext.Create(seed));

    public static DemoStoreDataset Build(SimulationContext context)
    {
        var rng = new DeterministicRandom(context.Seed);
        var storeVersion = $"demostore-v0-seed-{InvariantFormat.Number(context.Seed)}";

        var (clean, cleanFacts) = CleanStoreGenerator.Generate(rng.Fork("clean"), context, storeVersion);

        var reservations = new Reservations();
        var decoys = DecoyGenerator.Generate(clean, cleanFacts, reservations, rng.Fork("decoys"), context);
        var (published, publishedFacts, defects) = DefectInjector.Inject(clean, cleanFacts, reservations, rng.Fork("defects"), context);

        var intents = IntentGenerator.Generate(clean, published, defects, rng.Fork("intents"), context);

        ImmutableArray<SemanticFact> facts = [.. cleanFacts, .. publishedFacts];
        var groundTruth = new GroundTruth(storeVersion, context.Seed, context.Now, FieldOwnership.All, defects, decoys, facts);

        var metrics = new MetricsReport(storeVersion, context.Now,
        [
            ReadinessMetrics.Compute(clean, clean, facts, context),
            ReadinessMetrics.Compute(published, clean, facts, context),
        ]);

        return new DemoStoreDataset(context, clean, published, groundTruth, new IntentsFile(storeVersion, context.Now, intents), metrics);
    }
}
