using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Evaluation.Metrics;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.DemoStore.Output;

namespace CommerceReadiness.Tests;

public class SerializationTests
{
    [Fact]
    public void Eval_json_files_round_trip()
    {
        var dataset = TestData.Seed42;
        AssertRoundTrip(dataset.GroundTruth);
        AssertRoundTrip(dataset.Intents);
        AssertRoundTrip(dataset.Metrics);
    }

    [Fact]
    public void Written_files_deserialize_back_to_the_generated_data()
    {
        var dataset = TestData.Seed42;
        var directory = TestData.NewTempDirectory();
        DatasetWriter.Write(dataset, directory);

        var eval = Path.Combine(directory, DatasetWriter.EvalFolder);
        Assert.Equal(JsonDefaults.Serialize(dataset.GroundTruth), JsonDefaults.Serialize(DatasetReader.ReadGroundTruth(eval)));
        Assert.Equal(JsonDefaults.Serialize(dataset.Intents), JsonDefaults.Serialize(DatasetReader.ReadIntents(eval)));
        Assert.Equal(JsonDefaults.Serialize(dataset.Metrics), JsonDefaults.Serialize(DatasetReader.ReadMetrics(eval)));

        var clean = DatasetReader.ReadStore(Path.Combine(directory, DatasetWriter.CleanFolder), StoreKind.Clean, dataset.Clean.StoreVersion);
        var published = DatasetReader.ReadStore(Path.Combine(directory, DatasetWriter.PublishedFolder), StoreKind.Published, dataset.Published.StoreVersion);
        Assert.Equal(JsonDefaults.Serialize(dataset.Clean), JsonDefaults.Serialize(clean));
        Assert.Equal(JsonDefaults.Serialize(dataset.Published), JsonDefaults.Serialize(published));
    }

    [Fact]
    public void Intent_json_exposes_outcome_and_hidden_constraints()
    {
        var json = JsonDefaults.Serialize(TestData.Seed42.Intents);

        Assert.Contains("\"outcome\": \"NoValidProduct\"", json, StringComparison.Ordinal);
        Assert.Contains("\"outcome\": \"InsufficientInformation\"", json, StringComparison.Ordinal);
        Assert.Contains("\"constraints\":", json, StringComparison.Ordinal);
        Assert.Contains("İstanbul", json, StringComparison.Ordinal);
    }

    private static void AssertRoundTrip<T>(T value)
    {
        var json = JsonDefaults.Serialize(value);
        var copy = JsonDefaults.Deserialize<T>(json);
        Assert.Equal(json, JsonDefaults.Serialize(copy));
    }
}
