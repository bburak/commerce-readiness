using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Storage;

namespace CommerceReadiness.Analyzer;

/// <summary>Loads the observable store of a dataset. It only ever reads the dataset's published/ folder.</summary>
public static class PublishedDatasetLoader
{
    public static LoadedStore Load(string datasetDirectory)
    {
        var directory = Path.Combine(datasetDirectory, DatasetLayout.PublishedFolder);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Published store folder not found: '{directory}'.");
        }

        var loaded = StoreFiles.Read(directory);
        if (loaded.Manifest.Kind == StoreKind.Clean)
        {
            throw new InvalidDataException($"'{directory}' declares kind Clean; the analyzer only reads observable stores.");
        }

        return loaded;
    }
}
