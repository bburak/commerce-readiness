using System.Reflection;
using System.Text.RegularExpressions;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Core.Storage;
using CommerceReadiness.Retrieval;
using CommerceReadiness.Retrieval.Embeddings;

namespace CommerceReadiness.Tests;

/// <summary>Indexing and search see only published data; only the benchmark command reads hidden evaluation data.</summary>
public class RetrievalTrustBoundaryTests
{
    private static readonly string[] SearchArgs =
    [
        "search", "--query", "yağmurda kullanılabilecek laptop çantası", "--category", "Backpacks", "--max-price", "4000",
        "--in-stock", "--top", "5", "--mode", "Hybrid", "--embeddings", "hashing", "--index", "memory",
    ];

    [Fact]
    public void Retrieval_runtime_types_expose_no_ground_truth_evaluation_or_oracle_types()
    {
        var coreTypes = typeof(SearchDocument).Assembly.GetTypes()
            .Where(t => t.IsVisible && t.Namespace == "CommerceReadiness.Core.Retrieval");
        var runtimeTypes = typeof(RetrievalCli).Assembly.GetTypes()
            .Where(t => t.IsVisible && t.Namespace?.StartsWith("CommerceReadiness.Retrieval.Benchmark", StringComparison.Ordinal) != true);

        var offenders = coreTypes.Concat(runtimeTypes)
            .SelectMany(t => ExposedTypes(t).Select(exposed => $"{t.Name} exposes {exposed.FullName}"))
            .Where(x => x.Contains("CommerceReadiness.Core.Evaluation", StringComparison.Ordinal)
                        || x.Contains("CommerceReadiness.Core.Shopping", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.Contains(typeof(SearchDocumentBuilder), coreTypes);
        Assert.Empty(offenders);
    }

    [Fact]
    public void Retrieval_runtime_source_has_no_forbidden_dependencies()
    {
        var forbidden = new Regex(
            "\"clean\"|CleanFolder|\"eval\"|EvalFiles|ground-truth|retrieval-benchmark\\.json\"" +
            @"|using CommerceReadiness\.Core\.(Evaluation|Shopping)|Core\.Evaluation\.|Core\.Shopping\." +
            @"|\b(GroundTruth|InjectedDefect|DecoyCase|SemanticFact|AuthoritativeFacts|OracleShopper|RetrievalRelevanceOracle)\b");
        var root = TestData.RepoRoot();
        var benchmarkFolder = $"{Path.DirectorySeparatorChar}Benchmark{Path.DirectorySeparatorChar}";
        var files = new[] { Path.Combine(root, "src", "CommerceReadiness.Core", "Retrieval"), Path.Combine(root, "src", "CommerceReadiness.Retrieval") }
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains(benchmarkFolder, StringComparison.Ordinal))
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
    public void Core_has_no_ai_or_database_package_dependencies()
    {
        var references = typeof(SearchDocument).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        Assert.DoesNotContain(references, n =>
            n.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal)
            || n.StartsWith("OpenAI", StringComparison.Ordinal)
            || n.StartsWith("Npgsql", StringComparison.Ordinal)
            || n.StartsWith("Pgvector", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Index_and_search_work_from_the_published_folder_alone()
    {
        var full = TestData.NewSeed42DatasetDirectory();
        var publishedOnly = TestData.NewTempDirectory();
        TestData.CopyDirectory(Path.Combine(full, DatasetLayout.PublishedFolder), Path.Combine(publishedOnly, DatasetLayout.PublishedFolder));

        var indexResult = await RetrievalTestSupport.RunCliAsync("index", "--dataset", publishedOnly, "--embeddings", "hashing", "--index", "memory");
        var expected = await RetrievalTestSupport.RunCliAsync([.. SearchArgs, "--dataset", full]);
        var actual = await RetrievalTestSupport.RunCliAsync([.. SearchArgs, "--dataset", publishedOnly]);

        Assert.Equal(0, indexResult.Code);
        Assert.Contains("embedded 100", indexResult.Output, StringComparison.Ordinal);
        Assert.Equal(0, actual.Code);
        Assert.Equal(expected.Output, actual.Output);
        Assert.Contains("SKU-BP-", actual.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Poisoned_clean_and_eval_folders_do_not_affect_index_or_search()
    {
        var baseline = TestData.NewSeed42DatasetDirectory();
        var poisoned = TestData.NewTempDirectory();
        TestData.CopyDirectory(Path.Combine(baseline, DatasetLayout.PublishedFolder), Path.Combine(poisoned, DatasetLayout.PublishedFolder));
        foreach (var folder in new[] { DatasetLayout.CleanFolder, EvalFiles.Folder })
        {
            var directory = Directory.CreateDirectory(Path.Combine(poisoned, folder)).FullName;
            foreach (var name in new[] { StoreFileNames.Manifest, StoreFileNames.Catalog, EvalFiles.RetrievalBenchmarkFileName, EvalFiles.GroundTruthFileName })
            {
                File.WriteAllText(Path.Combine(directory, name), "{ this is not valid data");
            }
        }

        var expected = await RetrievalTestSupport.RunCliAsync([.. SearchArgs, "--dataset", baseline]);
        var actual = await RetrievalTestSupport.RunCliAsync([.. SearchArgs, "--dataset", poisoned]);
        var benchmark = await RetrievalTestSupport.RunCliAsync("benchmark", "--dataset", poisoned, "--embeddings", "hashing", "--index", "memory");

        Assert.Equal(0, actual.Code);
        Assert.Equal(expected.Output, actual.Output);
        Assert.NotEqual(0, benchmark.Code);
    }

    [Fact]
    public async Task Benchmark_without_hidden_data_fails_clearly()
    {
        var dataset = TestData.NewSeed42DatasetDirectory();
        Directory.Delete(Path.Combine(dataset, EvalFiles.Folder), recursive: true);

        var (code, _, error) = await RetrievalTestSupport.RunCliAsync("benchmark", "--dataset", dataset, "--embeddings", "hashing", "--index", "memory");

        Assert.Equal(2, code);
        Assert.Contains(EvalFiles.RetrievalBenchmarkFileName, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAI_embeddings_fail_clearly_without_an_api_key()
    {
        var settings = new RetrievalSettings(RetrievalSettings.DefaultConnectionString, null, "text-embedding-3-small", 1536);

        var exception = Assert.Throws<InvalidOperationException>(() => EmbeddingGeneratorFactory.Create(EmbeddingProvider.OpenAI, settings));
        var dataset = TestData.NewSeed42DatasetDirectory();
        var (code, _, error) = await RetrievalTestSupport.RunCliAsync("index", "--dataset", dataset, "--embeddings", "openai", "--index", "memory");

        Assert.Contains("OPENAI_API_KEY", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, code);
        Assert.Contains("OPENAI_API_KEY", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Published_store_loader_refuses_a_clean_folder()
    {
        var dataset = TestData.NewSeed42DatasetDirectory();
        var published = Path.Combine(dataset, DatasetLayout.PublishedFolder);
        Directory.Delete(published, recursive: true);
        TestData.CopyDirectory(Path.Combine(dataset, DatasetLayout.CleanFolder), published);

        Assert.Throws<InvalidDataException>(() => PublishedStore.Load(dataset));
    }

    private static IEnumerable<Type> ExposedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return type.GetMethods(flags).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .Concat(type.GetConstructors(flags).SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
            .Concat(type.GetProperties(flags).Select(p => p.PropertyType))
            .Concat(type.GetFields(flags).Select(f => f.FieldType))
            .SelectMany(Expand);
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
