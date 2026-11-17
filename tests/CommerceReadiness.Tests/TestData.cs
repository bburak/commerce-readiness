using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.DemoStore;

namespace CommerceReadiness.Tests;

internal static class TestData
{
    private static readonly Lazy<DemoStoreDataset> Seed42Lazy = new(() => DemoStoreBuilder.Build(42));

    private static readonly Lazy<EvaluationUnitSet> Seed42UnitsLazy =
        new(() => EvaluationUnitSet.FromClean(Seed42.Clean, Seed42.Context));

    public static DemoStoreDataset Seed42 => Seed42Lazy.Value;

    public static EvaluationUnitSet Seed42Units => Seed42UnitsLazy.Value;

    public static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "commerce-readiness-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CommerceReadiness.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    public static SortedDictionary<string, byte[]> ReadAllFiles(string root) =>
        new(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllBytes),
            StringComparer.Ordinal);
}
