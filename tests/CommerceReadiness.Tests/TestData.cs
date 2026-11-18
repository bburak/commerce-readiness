using CommerceReadiness.Core.Analysis;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.DemoStore;
using CommerceReadiness.DemoStore.Output;

namespace CommerceReadiness.Tests;

internal static class TestData
{
    private static readonly Lazy<DemoStoreDataset> Seed42Lazy = new(() => DemoStoreBuilder.Build(42));

    private static readonly Lazy<EvaluationUnitSet> Seed42UnitsLazy =
        new(() => EvaluationUnitSet.FromClean(Seed42.Clean, Seed42.Context));

    private static readonly Lazy<ReadinessAnalysis> Seed42AnalysisLazy =
        new(() => ReadinessAnalyzer.Analyze(Seed42.Published, Seed42.Context.Now));

    private static readonly Lazy<AnalyzerEvaluation> Seed42EvaluationLazy =
        new(() => AnalyzerEvaluator.Evaluate(Seed42Analysis, Seed42.GroundTruth));

    public static DemoStoreDataset Seed42 => Seed42Lazy.Value;

    public static EvaluationUnitSet Seed42Units => Seed42UnitsLazy.Value;

    public static ReadinessAnalysis Seed42Analysis => Seed42AnalysisLazy.Value;

    public static AnalyzerEvaluation Seed42Evaluation => Seed42EvaluationLazy.Value;

    /// <summary>A fresh on-disk copy of the full seed-42 dataset (clean, published and eval).</summary>
    public static string NewSeed42DatasetDirectory()
    {
        var directory = NewTempDirectory();
        DatasetWriter.Write(Seed42, directory);
        return directory;
    }

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

    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }
}
