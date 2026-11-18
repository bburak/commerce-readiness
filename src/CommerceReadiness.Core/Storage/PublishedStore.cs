using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Storage;

/// <summary>
/// Loads the observable store of a dataset for production consumers (analyzer, retrieval). It only ever reads the
/// dataset's published/ folder and refuses a folder that declares itself Clean.
/// </summary>
public static class PublishedStore
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
            throw new InvalidDataException($"'{directory}' declares kind Clean; only observable stores may be loaded.");
        }

        return loaded;
    }
}
