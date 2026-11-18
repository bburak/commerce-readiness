using CommerceReadiness.Core.Storage;

namespace CommerceReadiness.Analyzer;

/// <summary>Loads the observable store of a dataset. It only ever reads the dataset's published/ folder.</summary>
public static class PublishedDatasetLoader
{
    public static LoadedStore Load(string datasetDirectory) => PublishedStore.Load(datasetDirectory);
}
