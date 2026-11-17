using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Storage;

namespace CommerceReadiness.DemoStore.Output;

public static class DatasetWriter
{
    public static void Write(DemoStoreDataset dataset, string outputDirectory)
    {
        foreach (var folder in new[] { DatasetLayout.CleanFolder, DatasetLayout.PublishedFolder, EvalFiles.Folder })
        {
            var path = Path.Combine(outputDirectory, folder);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            Directory.CreateDirectory(path);
        }

        StoreFiles.Write(dataset.Clean, dataset.Context.Now, Path.Combine(outputDirectory, DatasetLayout.CleanFolder));
        StoreFiles.Write(dataset.Published, dataset.Context.Now, Path.Combine(outputDirectory, DatasetLayout.PublishedFolder));

        var eval = Path.Combine(outputDirectory, EvalFiles.Folder);
        TextFiles.Write(Path.Combine(eval, EvalFiles.GroundTruthFileName), JsonDefaults.Serialize(dataset.GroundTruth));
        TextFiles.Write(Path.Combine(eval, EvalFiles.IntentsFileName), JsonDefaults.Serialize(dataset.Intents));
        TextFiles.Write(Path.Combine(eval, EvalFiles.MetricsFileName), JsonDefaults.Serialize(dataset.Metrics));
    }
}
