using System.Globalization;
using System.Text.RegularExpressions;
using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Core.Time;
using CommerceReadiness.DemoStore;
using CommerceReadiness.DemoStore.Generation;
using CommerceReadiness.DemoStore.Output;

namespace CommerceReadiness.Tests;

public class DeterminismTests
{
    [Fact]
    public void Same_seed_produces_byte_identical_output()
    {
        var first = TestData.NewTempDirectory();
        var second = TestData.NewTempDirectory();

        DatasetWriter.Write(DemoStoreBuilder.Build(42), first);
        DatasetWriter.Write(DemoStoreBuilder.Build(42), second);

        var firstFiles = TestData.ReadAllFiles(first);
        var secondFiles = TestData.ReadAllFiles(second);
        Assert.Equal(firstFiles.Keys, secondFiles.Keys);
        Assert.Equal(15, firstFiles.Count);
        foreach (var (path, bytes) in firstFiles)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(secondFiles[path]), $"{path} differs between runs.");
        }
    }

    [Fact]
    public void Different_seed_produces_different_product_data()
    {
        var seed42 = DemoStoreBuilder.Build(42).Clean;
        var seed7 = DemoStoreBuilder.Build(7).Clean;

        Assert.NotEqual(seed42.Products.Select(p => p.Title), seed7.Products.Select(p => p.Title));
        Assert.NotEqual(seed42.Prices.Select(p => p.PriceTry), seed7.Prices.Select(p => p.PriceTry));
        Assert.NotEqual(seed42.StoreVersion, seed7.StoreVersion);
    }

    [Fact]
    public void Defect_and_decoy_ids_are_stable_and_unique_for_the_same_seed()
    {
        var first = DemoStoreBuilder.Build(42).GroundTruth;
        var second = DemoStoreBuilder.Build(42).GroundTruth;

        Assert.Equal(first.Defects.Select(d => d.DefectId), second.Defects.Select(d => d.DefectId));
        Assert.Equal(first.Decoys.Select(d => d.DecoyId), second.Decoys.Select(d => d.DecoyId));
        Assert.Equal(first.Defects.Length, first.Defects.Select(d => d.DefectId).Distinct().Count());
        Assert.Equal(first.Decoys.Length, first.Decoys.Select(d => d.DecoyId).Distinct().Count());
    }

    public static TheoryData<int> ManySeeds() => [.. Enumerable.Range(1, 40)];

    [Theory]
    [MemberData(nameof(ManySeeds))]
    public void Generation_succeeds_with_the_same_shape_for_any_seed(int seed)
    {
        var dataset = DemoStoreBuilder.Build(seed);

        Assert.Equal(100, dataset.Clean.Variants.Length);
        Assert.Equal(24, dataset.GroundTruth.Defects.Length);
        Assert.Equal(8, dataset.GroundTruth.Decoys.Length);
        var outcomes = dataset.Intents.Intents.GroupBy(i => i.Expected.Outcome).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(14, outcomes[IntentOutcome.ValidProductsExist]);
        Assert.Equal(4, outcomes[IntentOutcome.NoValidProduct]);
        Assert.Equal(2, outcomes[IntentOutcome.InsufficientInformation]);

        var clean = dataset.Metrics.Stores.Single(s => s.Store == StoreKind.Clean);
        var published = dataset.Metrics.Stores.Single(s => s.Store == StoreKind.Published);
        Assert.Equal(clean.DataConsistency.Denominator, published.DataConsistency.Denominator);
        Assert.Empty(clean.AllNotPassingUnits());
        Assert.Equal(
            dataset.GroundTruth.Defects.SelectMany(d => d.ExpectedImpacts).Order(StringComparer.Ordinal),
            published.AllNotPassingUnits().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Turkish_current_culture_does_not_change_output()
    {
        var invariantDirectory = TestData.NewTempDirectory();
        var turkishDirectory = TestData.NewTempDirectory();
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            DatasetWriter.Write(DemoStoreBuilder.Build(42), invariantDirectory);

            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            DatasetWriter.Write(DemoStoreBuilder.Build(42), turkishDirectory);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }

        var invariantFiles = TestData.ReadAllFiles(invariantDirectory);
        var turkishFiles = TestData.ReadAllFiles(turkishDirectory);
        foreach (var (path, bytes) in invariantFiles)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(turkishFiles[path]), $"{path} depends on the current culture.");
        }
    }

    [Fact]
    public void Source_code_never_reads_the_system_clock()
    {
        var pattern = new Regex(@"\bDateTime(Offset)?\.(Now|UtcNow|Today)\b");
        var offenders = Directory
            .EnumerateFiles(Path.Combine(TestData.RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => pattern.IsMatch(File.ReadAllText(f)))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Fixed_clock_makes_delivery_deterministic()
    {
        var store = TestData.Seed42.Clean;
        var clock = SimulationContext.DefaultClock;

        // Tuesday 10:00: Istanbul warehouse (cutoff 14:00) ships today; Izmir warehouse (cutoff 09:00) ships tomorrow.
        Assert.Equal(new DateOnly(2026, 11, 18), DeliveryCalculator.EstimateFromWarehouse(store, StoreConfiguration.IstanbulWarehouse, City.Istanbul, clock));
        Assert.Equal(new DateOnly(2026, 11, 19), DeliveryCalculator.EstimateFromWarehouse(store, StoreConfiguration.IstanbulWarehouse, City.Ankara, clock));
        Assert.Equal(new DateOnly(2026, 11, 20), DeliveryCalculator.EstimateFromWarehouse(store, StoreConfiguration.IzmirWarehouse, City.Istanbul, clock));

        // After the Istanbul cutoff on Friday the parcel ships Monday and arrives Tuesday.
        var fridayEvening = new DateTimeOffset(2026, 11, 20, 16, 0, 0, TimeSpan.FromHours(3));
        Assert.Equal(new DateOnly(2026, 11, 24), DeliveryCalculator.EstimateFromWarehouse(store, StoreConfiguration.IstanbulWarehouse, City.Istanbul, fridayEvening));

        var first = DemoStoreBuilder.Build(SimulationContext.Create(42));
        var second = DemoStoreBuilder.Build(SimulationContext.Create(42));
        Assert.Equal(
            first.Clean.Listings.Select(l => l.DeliveryPromiseIstanbul),
            second.Clean.Listings.Select(l => l.DeliveryPromiseIstanbul));
        Assert.Equal(JsonDefaults.Serialize(first.Intents), JsonDefaults.Serialize(second.Intents));
    }

    [Fact]
    public void Delivery_results_depend_only_on_the_supplied_clock()
    {
        var store = TestData.Seed42.Clean;
        var fridayEvening = new DateTimeOffset(2026, 11, 20, 16, 0, 0, TimeSpan.FromHours(3));
        var inStock = store.Inventory.Where(i => i.Available > 0).Select(i => i.Sku).ToList();

        var atDefault = inStock.Select(s => DeliveryCalculator.EstimateDelivery(store, s, City.Istanbul, SimulationContext.DefaultClock)).ToList();
        var atFriday = inStock.Select(s => DeliveryCalculator.EstimateDelivery(store, s, City.Istanbul, fridayEvening)).ToList();

        Assert.Equal(inStock.Select(s => store.GetListing(s).DeliveryPromiseIstanbul), atDefault);
        Assert.All(atDefault.Zip(atFriday), pair => Assert.True(pair.Second > pair.First));
    }
}
