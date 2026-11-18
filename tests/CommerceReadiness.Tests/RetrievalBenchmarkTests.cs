using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Evaluation.Retrieval;
using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.DemoStore;
using CommerceReadiness.Retrieval;
using CommerceReadiness.Retrieval.Benchmark;
using CommerceReadiness.Retrieval.Embeddings;

namespace CommerceReadiness.Tests;

public class RetrievalBenchmarkTests
{
    private static readonly RetrievalBenchmarkFile Benchmark = TestData.Seed42.RetrievalBenchmark;

    [Fact]
    public void Metric_formulas_match_handcrafted_rankings()
    {
        var score = RetrievalMetrics.Score("c1", ["a", "b", "c", "d", "e", "f"], ["a", "c", "x"], 5);
        var empty = RetrievalMetrics.Score("c2", [], ["a"], 5);
        var many = RetrievalMetrics.Score("c3", ["a", "b"], ["a", "b", "c", "d", "e", "f", "g", "h"], 5);

        Assert.Equal(new CaseRetrievalScore("c1", 3, 5, 2, 0.6667, 0.4, true, 1.0), score);
        Assert.Equal(new CaseRetrievalScore("c2", 1, 0, 0, 0.0, null, false, 1.0), empty);
        Assert.Equal(new CaseRetrievalScore("c3", 8, 2, 2, 0.25, 1.0, true, 0.625), many);

        var aggregate = RetrievalMetrics.Aggregate([score, empty, many]);
        Assert.Equal(new RetrievalAggregate(3, Math.Round((0.6667 + 0 + 0.25) / 3, 4), 0.7, 2, 0.6667, 0.875), aggregate);
        Assert.Throws<ArgumentException>(() => RetrievalMetrics.Score("c4", ["a"], [], 5));
        Assert.Null(RetrievalMetrics.Aggregate([]).RecallAtK);
    }

    [Fact]
    public void Benchmark_generation_is_deterministic()
    {
        Assert.Equal(JsonDefaults.Serialize(Benchmark), JsonDefaults.Serialize(DemoStoreBuilder.Build(42).RetrievalBenchmark));
        Assert.Equal(RetrievalBenchmarkFile.CurrentSchemaVersion, Benchmark.SchemaVersion);
        Assert.Equal(TestData.Seed42.Clean.StoreVersion, Benchmark.StoreVersion);
    }

    [Fact]
    public void Relevance_is_computed_by_the_oracle_from_clean_truth()
    {
        foreach (var benchmarkCase in Benchmark.Cases.Where(c => c.Outcome != RetrievalOutcome.InsufficientDiscoveryInformation))
        {
            var recomputed = RetrievalRelevanceOracle.RelevantSkus(TestData.Seed42.Clean, benchmarkCase.RelevanceConstraints, TestData.Seed42.Context.Now);
            Assert.Equal(recomputed.ToArray(), benchmarkCase.RelevantSkus.ToArray());
            Assert.Null(benchmarkCase.RelevanceConstraints.DeliverTo);
            Assert.Null(benchmarkCase.RelevanceConstraints.DeliverWithinDays);
            Assert.Null(benchmarkCase.RelevanceConstraints.InstallmentCount);
            Assert.Null(benchmarkCase.RelevanceConstraints.ReturnRequirement);
            Assert.True(benchmarkCase.Filters.InStockOnly);
            Assert.True(benchmarkCase.RelevantSkus.Length <= benchmarkCase.HardFilterPoolSize);
        }
    }

    [Fact]
    public void Shopper_derived_cases_project_intents_through_the_filter_policy()
    {
        var shopperCases = Benchmark.Cases.Where(c => c.Group == RetrievalCaseGroup.ShopperDerived).ToList();
        var intents = TestData.Seed42.Intents.Intents.ToDictionary(i => i.Id, StringComparer.Ordinal);

        Assert.Equal(20, shopperCases.Count);
        Assert.All(shopperCases, c =>
        {
            var intent = intents[c.SourceIntentId!];
            Assert.Equal(intent.Text, c.QueryText);
            Assert.Equal(RetrievalPolicy.HardFilters(intent.Constraints), c.Filters);
        });

        RetrievalOutcome Outcome(string id) => shopperCases.Single(c => c.CaseId == id).Outcome;
        Assert.Equal(RetrievalOutcome.RelevantProductsExist, Outcome("I17"));
        Assert.Equal(RetrievalOutcome.RelevantProductsExist, Outcome("I18"));
        Assert.Equal(RetrievalOutcome.NoRelevantProduct, Outcome("I15"));
        Assert.Equal(RetrievalOutcome.NoRelevantProduct, Outcome("I16"));
        Assert.Equal(RetrievalOutcome.InsufficientDiscoveryInformation, Outcome("I19"));
        Assert.Equal(RetrievalOutcome.InsufficientDiscoveryInformation, Outcome("I20"));
        Assert.Empty(shopperCases.Single(c => c.CaseId == "I19").RelevantSkus);
    }

    [Fact]
    public void Filter_policy_keeps_exact_constraints_as_filters_and_fuzzy_needs_semantic()
    {
        var constraints = new ShopperConstraints
        {
            Category = Core.Domain.Category.Backpacks, MaxPriceTry = 3000m, ShoeSize = 42, Color = "Siyah",
            MinimumLaptopSizeIn = 15.6m, MinimumCapacityLiters = 20m, MinimumBatteryHours = 10, MinimumWarrantyMonths = 24,
            MinimumWaterResistance = Core.Domain.WaterResistance.Waterproof, ProductType = "Powerbank", TargetGroup = "Kadın",
            NoiseCancellation = true, Microphone = true,
        };

        var filters = RetrievalPolicy.HardFilters(constraints);

        Assert.Equal(
            new StructuredFilters
            {
                Category = Core.Domain.Category.Backpacks, MaxPriceTry = 3000m, InStockOnly = true, ShoeSize = 42, Color = "Siyah",
                MinimumLaptopSizeIn = 15.6m, MinimumCapacityLiters = 20m, MinimumBatteryHours = 10, MinimumWarrantyMonths = 24,
            },
            filters);
        Assert.DoesNotContain(typeof(StructuredFilters).GetProperties(), p =>
            p.Name is "MinimumWaterResistance" or "ProductType" or "TargetGroup" or "NoiseCancellation" or "Microphone");
    }

    [Fact]
    public void Every_discovery_case_leaves_semantic_retrieval_a_job()
    {
        var discovery = Benchmark.Cases.Where(c => c.Group == RetrievalCaseGroup.DiscoveryOnly).ToList();

        Assert.Equal(10, discovery.Count);
        Assert.All(discovery, c =>
        {
            Assert.Null(c.SourceIntentId);
            Assert.Equal(RetrievalOutcome.RelevantProductsExist, c.Outcome);
            Assert.True(c.IsSemanticDiscriminating, c.CaseId);
        });
    }

    [Fact]
    public async Task Offline_hashing_benchmark_is_byte_identical_and_carries_provenance()
    {
        var dataset = TestData.NewSeed42DatasetDirectory();
        var first = TestData.NewTempDirectory();
        var second = TestData.NewTempDirectory();

        var run1 = await RetrievalTestSupport.RunCliAsync("benchmark", "--dataset", dataset, "--embeddings", "hashing", "--index", "memory", "--out", first);
        var run2 = await RetrievalTestSupport.RunCliAsync("benchmark", "--dataset", dataset, "--embeddings", "hashing", "--index", "memory", "--out", second);

        Assert.Equal(0, run1.Code);
        Assert.Equal(0, run2.Code);
        var file = $"benchmark-{HashingEmbeddingGenerator.ModelId}.json";
        Assert.Equal(File.ReadAllBytes(Path.Combine(first, file)), File.ReadAllBytes(Path.Combine(second, file)));

        var report = JsonDefaults.Deserialize<RetrievalBenchmarkReport>(File.ReadAllText(Path.Combine(first, file)));
        Assert.Equal(
            new BenchmarkProvenance("demostore-v0-seed-42", 1, EmbeddingProvider.Hashing, HashingEmbeddingGenerator.ModelId, 256, 5, "memory", null),
            report.Provenance);
        Assert.Equal(26, report.ScoredCases);
        Assert.Equal(2, report.NoRelevantProductCases);
        Assert.Equal(2, report.ExcludedInsufficientCases);
        Assert.Equal(Enum.GetValues<SearchMode>(), report.Modes.Select(m => m.Mode));
        Assert.All(report.Modes, m => Assert.Equal(16 + 10, m.Combined.Cases));
    }

    [Fact]
    public async Task Real_provider_runs_are_timestamped_as_observed_runs()
    {
        var observedAt = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        var labelledAsOpenAi = new EmbeddingSetup(
            new HashingEmbeddingGenerator(),
            new EmbeddingProfile(EmbeddingProvider.OpenAI, "fake-model", HashingEmbeddingGenerator.DefaultDimensions));
        var index = new Retrieval.Index.InMemoryProductSearchIndex();
        await ProductIndexer.IndexAsync(TestData.Seed42.Published, labelledAsOpenAi, index);

        var report = await RetrievalBenchmarkRunner.RunAsync(
            Benchmark, TestData.Seed42.Published, labelledAsOpenAi, index, 5, new FixedTimeProvider(observedAt));

        Assert.Equal(observedAt, report.Provenance.RunTimestampUtc);
        Assert.Equal(EmbeddingProvider.OpenAI, report.Provenance.EmbeddingProvider);
    }

    [Fact]
    public void Unsupported_benchmark_schema_versions_are_rejected()
    {
        var dataset = TestData.NewSeed42DatasetDirectory();
        var path = Path.Combine(dataset, EvalFiles.Folder, EvalFiles.RetrievalBenchmarkFileName);
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 9", StringComparison.Ordinal));

        var exception = Assert.Throws<NotSupportedException>(() => RetrievalBenchmarkRunner.LoadBenchmark(dataset));
        Assert.Contains("schema version 9", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Benchmark_refuses_an_index_of_another_store()
    {
        var other = Benchmark with { StoreVersion = "demostore-v0-seed-7" };
        var index = await RetrievalTestSupport.IndexedSeed42Async();

        await Assert.ThrowsAsync<ArgumentException>(() => RetrievalBenchmarkRunner.RunAsync(
            other, TestData.Seed42.Published, RetrievalTestSupport.Hashing(), index, 5, TimeProvider.System));
    }
}
