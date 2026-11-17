using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Evaluation.Metrics;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Core.Storage;
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

        var eval = Path.Combine(directory, EvalFiles.Folder);
        Assert.Equal(JsonDefaults.Serialize(dataset.GroundTruth), JsonDefaults.Serialize(EvalFiles.ReadGroundTruth(eval)));
        Assert.Equal(JsonDefaults.Serialize(dataset.Intents), JsonDefaults.Serialize(EvalFiles.ReadIntents(eval)));
        Assert.Equal(JsonDefaults.Serialize(dataset.Metrics), JsonDefaults.Serialize(EvalFiles.ReadMetrics(eval)));

        var clean = StoreFiles.Read(Path.Combine(directory, DatasetLayout.CleanFolder));
        var published = StoreFiles.Read(Path.Combine(directory, DatasetLayout.PublishedFolder));
        Assert.Equal(JsonDefaults.Serialize(dataset.Clean), JsonDefaults.Serialize(clean.Store));
        Assert.Equal(JsonDefaults.Serialize(dataset.Published), JsonDefaults.Serialize(published.Store));
    }

    [Fact]
    public void Store_manifests_identify_each_store_folder()
    {
        var dataset = TestData.Seed42;
        var directory = TestData.NewTempDirectory();
        DatasetWriter.Write(dataset, directory);

        var clean = StoreFiles.Read(Path.Combine(directory, DatasetLayout.CleanFolder)).Manifest;
        var published = StoreFiles.Read(Path.Combine(directory, DatasetLayout.PublishedFolder)).Manifest;

        Assert.Equal(new StoreManifest(1, "demostore-v0-seed-42", StoreKind.Clean, dataset.Context.Now), clean);
        Assert.Equal(new StoreManifest(1, "demostore-v0-seed-42", StoreKind.Published, dataset.Context.Now), published);
    }

    [Fact]
    public void Unsupported_schema_versions_and_missing_manifests_are_rejected()
    {
        var directory = TestData.NewTempDirectory();
        DatasetWriter.Write(TestData.Seed42, directory);
        var published = Path.Combine(directory, DatasetLayout.PublishedFolder);
        var manifestPath = Path.Combine(published, StoreFileNames.Manifest);

        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal));
        var unsupported = Assert.Throws<NotSupportedException>(() => StoreFiles.Read(published));
        Assert.Contains("schema version 2", unsupported.Message, StringComparison.Ordinal);

        File.Delete(manifestPath);
        Assert.Throws<FileNotFoundException>(() => StoreFiles.Read(published));
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
