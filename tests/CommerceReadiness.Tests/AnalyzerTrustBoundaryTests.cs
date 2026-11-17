using System.Reflection;
using System.Text.RegularExpressions;
using CommerceReadiness.Analyzer;
using CommerceReadiness.Core.Analysis;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Storage;

namespace CommerceReadiness.Tests;

/// <summary>The analyzer may only see published data; ground truth is used by the separate evaluator only.</summary>
public class AnalyzerTrustBoundaryTests
{
    [Fact]
    public void Analyzer_rejects_the_clean_store()
    {
        Assert.Throws<ArgumentException>(() => ReadinessAnalyzer.Analyze(TestData.Seed42.Clean, TestData.Seed42.Context.Now));
    }

    [Fact]
    public void Analyzer_api_takes_only_an_observable_store_and_an_as_of_time()
    {
        var analyze = typeof(ReadinessAnalyzer).GetMethod(nameof(ReadinessAnalyzer.Analyze))!;

        Assert.Equal([typeof(StoreSnapshot), typeof(DateTimeOffset)], analyze.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void No_analysis_type_exposes_ground_truth_evaluation_or_oracle_types()
    {
        var analysisTypes = typeof(ReadinessAnalyzer).Assembly.GetTypes()
            .Where(t => t.IsVisible && t.Namespace?.StartsWith("CommerceReadiness.Core.Analysis", StringComparison.Ordinal) == true);
        var cliTypes = typeof(AnalyzerCli).Assembly.GetTypes()
            .Where(t => t.IsVisible && t.Namespace == "CommerceReadiness.Analyzer" && t != typeof(EvaluateCommand));
        Assert.Contains(typeof(ReadinessAnalyzer), analysisTypes);
        Assert.Contains(typeof(PublishedDatasetLoader), cliTypes);

        var offenders = analysisTypes.Concat(cliTypes)
            .SelectMany(t => ExposedTypes(t).Select(exposed => (Owner: t, Exposed: exposed)))
            .Where(x => IsForbidden(x.Exposed))
            .Select(x => $"{x.Owner.Name} exposes {x.Exposed.FullName}")
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Analyzer_source_has_no_forbidden_runtime_dependencies()
    {
        // Narrow on purpose: the word "clean" is allowed (the analyzer guards against StoreKind.Clean); only concrete
        // access to hidden folders, ground-truth files or evaluation/oracle types is forbidden.
        var forbidden = new Regex(
            "\"clean\"|CleanFolder|\"eval\"|EvalFiles|ground-truth|GroundTruthFileName" +
            @"|using CommerceReadiness\.Core\.(Evaluation|Shopping)|Core\.Evaluation\.|Core\.Shopping\." +
            @"|\b(GroundTruth|InjectedDefect|DecoyCase|SemanticFact|AuthoritativeFacts|OracleShopper|EvaluationUnitSet)\b");
        var root = TestData.RepoRoot();
        var files = new[] { Path.Combine(root, "src", "CommerceReadiness.Core", "Analysis"), Path.Combine(root, "src", "CommerceReadiness.Analyzer") }
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && Path.GetFileName(f) != "EvaluateCommand.cs")
            .ToList();

        Assert.NotEmpty(files);
        var offenders = files
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line)))
            .Where(x => forbidden.IsMatch(x.Text))
            .Select(x => $"{x.File}:{x.Line}: {x.Text.Trim()}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Analysis_succeeds_from_the_published_folder_alone()
    {
        var full = TestData.NewSeed42DatasetDirectory();
        var publishedOnly = TestData.NewTempDirectory();
        TestData.CopyDirectory(Path.Combine(full, DatasetLayout.PublishedFolder), Path.Combine(publishedOnly, DatasetLayout.PublishedFolder));

        var (fullCode, _, _) = Run("--dataset", full);
        var (code, _, error) = Run("--dataset", publishedOnly);

        Assert.Equal(0, fullCode);
        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.False(Directory.Exists(Path.Combine(publishedOnly, DatasetLayout.CleanFolder)));
        Assert.False(Directory.Exists(Path.Combine(publishedOnly, EvalFiles.Folder)));
        AssertSameAnalysisFiles(full, publishedOnly);
    }

    [Fact]
    public void Poisoned_clean_and_eval_folders_do_not_affect_normal_analysis()
    {
        var baseline = TestData.NewSeed42DatasetDirectory();
        var poisoned = TestData.NewTempDirectory();
        TestData.CopyDirectory(Path.Combine(baseline, DatasetLayout.PublishedFolder), Path.Combine(poisoned, DatasetLayout.PublishedFolder));
        foreach (var folder in new[] { DatasetLayout.CleanFolder, EvalFiles.Folder })
        {
            var directory = Directory.CreateDirectory(Path.Combine(poisoned, folder)).FullName;
            foreach (var name in new[] { StoreFileNames.Manifest, StoreFileNames.Catalog, StoreFileNames.Inventory, EvalFiles.GroundTruthFileName })
            {
                File.WriteAllText(Path.Combine(directory, name), "{ this is not valid data");
            }
        }

        Assert.Equal(0, Run("--dataset", baseline).Code);
        var (code, _, error) = Run("--dataset", poisoned);

        Assert.Equal(0, code);
        Assert.Empty(error);
        AssertSameAnalysisFiles(baseline, poisoned);

        var (evaluateCode, _, evaluateError) = Run("--dataset", poisoned, "--evaluate");
        Assert.NotEqual(0, evaluateCode);
        Assert.Contains("error:", evaluateError, StringComparison.Ordinal);
    }

    [Fact]
    public void Ground_truth_is_read_only_when_evaluate_is_requested()
    {
        var dataset = TestData.NewSeed42DatasetDirectory();
        var analysis = Path.Combine(dataset, DatasetLayout.AnalysisFolder);

        var (code, output, _) = Run("--dataset", dataset);
        Assert.Equal(0, code);
        Assert.False(File.Exists(Path.Combine(analysis, AnalysisWriter.EvaluationFileName)));
        Assert.DoesNotContain("Deterministic recall", output, StringComparison.Ordinal);
        Assert.Contains("Run with --evaluate", output, StringComparison.Ordinal);

        var (evaluateCode, evaluateOutput, _) = Run("--dataset", dataset, "--evaluate");
        Assert.Equal(0, evaluateCode);
        Assert.True(File.Exists(Path.Combine(analysis, AnalysisWriter.EvaluationFileName)));
        Assert.Contains("Deterministic recall", evaluateOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Normal_analysis_removes_a_stale_evaluation_from_an_earlier_run()
    {
        var dataset = TestData.NewSeed42DatasetDirectory();
        var analysis = Path.Combine(dataset, DatasetLayout.AnalysisFolder);
        var evaluation = Path.Combine(analysis, AnalysisWriter.EvaluationFileName);

        Assert.Equal(0, Run("--dataset", dataset, "--evaluate").Code);
        Assert.True(File.Exists(evaluation));

        Assert.Equal(0, Run("--dataset", dataset).Code);
        Assert.False(File.Exists(evaluation));
        Assert.True(File.Exists(Path.Combine(analysis, AnalysisWriter.FindingsFileName)));
        Assert.True(File.Exists(Path.Combine(analysis, AnalysisWriter.SummaryFileName)));

        Assert.Equal(0, Run("--dataset", dataset, "--evaluate").Code);
        Assert.True(File.Exists(evaluation));
    }

    [Fact]
    public void Evaluate_without_ground_truth_fails_clearly()
    {
        var dataset = TestData.NewSeed42DatasetDirectory();
        Directory.Delete(Path.Combine(dataset, EvalFiles.Folder), recursive: true);

        var (code, _, error) = Run("--dataset", dataset, "--evaluate");

        Assert.Equal(2, code);
        Assert.Contains(EvalFiles.GroundTruthFileName, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Loader_rejects_unsupported_schema_missing_manifest_and_clean_stores()
    {
        var dataset = TestData.NewSeed42DatasetDirectory();
        var published = Path.Combine(dataset, DatasetLayout.PublishedFolder);
        var manifest = Path.Combine(published, StoreFileNames.Manifest);
        var original = File.ReadAllText(manifest);

        File.WriteAllText(manifest, original.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal));
        var (schemaCode, _, schemaError) = Run("--dataset", dataset);
        Assert.Equal(2, schemaCode);
        Assert.Contains("Unsupported store schema version 2", schemaError, StringComparison.Ordinal);

        File.Delete(manifest);
        Assert.Throws<FileNotFoundException>(() => PublishedDatasetLoader.Load(dataset));

        Directory.Delete(published, recursive: true);
        TestData.CopyDirectory(Path.Combine(dataset, DatasetLayout.CleanFolder), published);
        Assert.Throws<InvalidDataException>(() => PublishedDatasetLoader.Load(dataset));
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = AnalyzerCli.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private static void AssertSameAnalysisFiles(string expectedDataset, string actualDataset)
    {
        foreach (var name in new[] { AnalysisWriter.FindingsFileName, AnalysisWriter.SummaryFileName })
        {
            var expected = File.ReadAllBytes(Path.Combine(expectedDataset, DatasetLayout.AnalysisFolder, name));
            var actual = File.ReadAllBytes(Path.Combine(actualDataset, DatasetLayout.AnalysisFolder, name));
            Assert.True(expected.AsSpan().SequenceEqual(actual), $"{name} differs.");
        }
    }

    private static bool IsForbidden(Type type) =>
        type.Namespace is { } ns
        && (ns.StartsWith("CommerceReadiness.Core.Evaluation", StringComparison.Ordinal)
            || ns.StartsWith("CommerceReadiness.Core.Shopping", StringComparison.Ordinal));

    private static IEnumerable<Type> ExposedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var direct = type.GetMethods(flags).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .Concat(type.GetConstructors(flags).SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
            .Concat(type.GetProperties(flags).Select(p => p.PropertyType))
            .Concat(type.GetFields(flags).Select(f => f.FieldType));
        return direct.SelectMany(Expand);
    }

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var inner in Expand(element))
            {
                yield return inner;
            }
        }

        foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : [])
        {
            foreach (var inner in Expand(argument))
            {
                yield return inner;
            }
        }
    }
}
