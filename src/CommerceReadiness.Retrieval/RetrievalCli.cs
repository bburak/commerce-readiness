using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Storage;
using CommerceReadiness.Retrieval.Benchmark;
using CommerceReadiness.Retrieval.Embeddings;
using CommerceReadiness.Retrieval.Index;
using Npgsql;

namespace CommerceReadiness.Retrieval;

/// <summary>
/// db-init, index and search use only the dataset's published/ folder. benchmark additionally reads the hidden
/// eval/retrieval-benchmark.json, and only that command does.
/// </summary>
public static class RetrievalCli
{
    public const string Rule = "───────────────────────────";
    public const string BenchmarkFolder = "retrieval";

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, RetrievalSettings settings, TimeProvider clock)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage(output);
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var options = Options.Parse(args.Skip(1));
            switch (args[0])
            {
                case "db-init":
                    await using (var index = new PgVectorProductSearchIndex(settings.ConnectionString))
                    {
                        await index.InitializeAsync();
                    }

                    output.WriteLine("Retrieval database initialized (vector extension and product_search_documents table).");
                    return 0;
                case "index":
                    return await IndexAsync(options, output, settings);
                case "search":
                    return await SearchAsync(options, output, settings);
                case "benchmark":
                    return await BenchmarkAsync(options, output, settings, clock);
                default:
                    error.WriteLine($"Unknown command '{args[0]}'. Use --help.");
                    return 1;
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidDataException or ArgumentException
                                       or InvalidOperationException or FormatException or NpgsqlException
                                       or System.Text.Json.JsonException)
        {
            error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> IndexAsync(Options options, TextWriter output, RetrievalSettings settings)
    {
        var published = PublishedStore.Load(options.Dataset).Store;
        var embeddings = EmbeddingGeneratorFactory.Create(options.Embeddings, settings);
        await using var index = await OpenIndexAsync(options, settings, initialize: true);
        var report = await ProductIndexer.IndexAsync(published, embeddings, index);
        output.WriteLine($"Indexed {published.StoreVersion} into {index.Name} ({embeddings.Profile.Model}, {embeddings.Profile.Dimensions} dims)");
        output.WriteLine($"  documents {report.Documents}, embedded {report.Embedded}, reused {report.Reused}, deleted {report.Deleted}");
        return 0;
    }

    private static async Task<int> SearchAsync(Options options, TextWriter output, RetrievalSettings settings)
    {
        var published = PublishedStore.Load(options.Dataset).Store;
        var embeddings = EmbeddingGeneratorFactory.Create(options.Embeddings, settings);
        await using var index = await OpenIndexAsync(options, settings, initialize: false);
        if (index is InMemoryIndexHandle)
        {
            await ProductIndexer.IndexAsync(published, embeddings, index);
        }

        var request = new SearchRequest(published.StoreVersion, options.Query, options.Filters, options.Mode, options.Top);
        var results = await ProductSearchService.SearchAsync(request, embeddings, index);

        output.WriteLine($"{options.Mode} search over {published.StoreVersion} ({index.Name}, {embeddings.Profile.Model})");
        output.WriteLine(Rule);
        foreach (var r in results)
        {
            var score = r.SimilarityScore is { } s ? s.ToString("0.0000", CultureInfo.InvariantCulture) : "-";
            output.WriteLine(
                $"{r.Rank,2}. {r.Sku,-20} {InvariantFormat.Money(r.PriceTry),10} TRY  qty {r.AvailableQuantity,3}  score {score,6}  {r.Title}");
        }

        if (results.Count == 0)
        {
            output.WriteLine("No candidates.");
        }

        return 0;
    }

    private static async Task<int> BenchmarkAsync(Options options, TextWriter output, RetrievalSettings settings, TimeProvider clock)
    {
        var published = PublishedStore.Load(options.Dataset).Store;
        var benchmark = RetrievalBenchmarkRunner.LoadBenchmark(options.Dataset);
        var embeddings = EmbeddingGeneratorFactory.Create(options.Embeddings, settings);
        await using var index = await OpenIndexAsync(options, settings, initialize: true);
        await ProductIndexer.IndexAsync(published, embeddings, index);

        var report = await RetrievalBenchmarkRunner.RunAsync(benchmark, published, embeddings, index, options.Top, clock);
        var directory = options.Out ?? Path.Combine(options.Dataset, BenchmarkFolder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"benchmark-{embeddings.Profile.Model}.json");
        TextFiles.Write(path, JsonDefaults.Serialize(report));
        BenchmarkReportPrinter.Print(report, path, output);
        return 0;
    }

    private static async Task<IndexHandle> OpenIndexAsync(Options options, RetrievalSettings settings, bool initialize)
    {
        if (options.Index == "memory")
        {
            return new InMemoryIndexHandle();
        }

        var index = new PgVectorProductSearchIndex(settings.ConnectionString);
        if (initialize)
        {
            await index.InitializeAsync();
        }

        return new PgVectorIndexHandle(index);
    }

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("Usage: CommerceReadiness.Retrieval <command> [options]");
        output.WriteLine("  db-init");
        output.WriteLine("  index      --dataset <dir> [--embeddings openai|hashing] [--index pgvector|memory]");
        output.WriteLine("  search     --query <text> [--mode StructuredOnly|SemanticOnly|Hybrid] [--top 5] [--category <c>] [--max-price <n>]");
        output.WriteLine("             [--in-stock] [--size <n>] [--color <c>] [--min-laptop <n>] [--min-capacity <n>] [--min-battery <n>]");
        output.WriteLine("             [--min-warranty <n>] [--dataset <dir>] [--embeddings ...] [--index ...]");
        output.WriteLine("  benchmark  --dataset <dir> [--top 5] [--embeddings ...] [--index ...] [--out <dir>]");
        output.WriteLine("index and search read only <dataset>/published/; benchmark also reads eval/retrieval-benchmark.json.");
    }

    /// <summary>Holds an index for the lifetime of a command.</summary>
    private abstract class IndexHandle : IProductSearchIndex, IAsyncDisposable
    {
        protected abstract IProductSearchIndex Inner { get; }

        public string Name => Inner.Name;

        public Task<IReadOnlyDictionary<string, string>> GetContentHashesAsync(IndexPartition partition, CancellationToken cancellationToken = default) =>
            Inner.GetContentHashesAsync(partition, cancellationToken);

        public Task UpsertAsync(IndexPartition partition, IReadOnlyList<IndexEntry> entries, CancellationToken cancellationToken = default) =>
            Inner.UpsertAsync(partition, entries, cancellationToken);

        public Task DeleteAsync(IndexPartition partition, IReadOnlyCollection<string> skus, CancellationToken cancellationToken = default) =>
            Inner.DeleteAsync(partition, skus, cancellationToken);

        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            IndexPartition partition, StructuredFilters? filters, float[]? queryEmbedding, int top, CancellationToken cancellationToken = default) =>
            Inner.SearchAsync(partition, filters, queryEmbedding, top, cancellationToken);

        public abstract ValueTask DisposeAsync();
    }

    private sealed class InMemoryIndexHandle : IndexHandle
    {
        protected override IProductSearchIndex Inner { get; } = new InMemoryProductSearchIndex();

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PgVectorIndexHandle(PgVectorProductSearchIndex index) : IndexHandle
    {
        protected override IProductSearchIndex Inner => index;

        public override ValueTask DisposeAsync() => index.DisposeAsync();
    }

    private sealed record Options(
        string Dataset,
        string? Out,
        EmbeddingProvider Embeddings,
        string Index,
        string? Query,
        SearchMode Mode,
        int Top,
        StructuredFilters Filters)
    {
        public static Options Parse(IEnumerable<string> args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var flags = new HashSet<string>(StringComparer.Ordinal);
            using var enumerator = args.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var key = enumerator.Current;
                if (!key.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Unexpected argument '{key}'.");
                }

                if (key == "--in-stock")
                {
                    flags.Add(key);
                    continue;
                }

                values[key] = enumerator.MoveNext() ? enumerator.Current : throw new ArgumentException($"Missing value for '{key}'.");
            }

            string? Get(string key) => values.GetValueOrDefault(key);
            int? Int(string key) => Get(key) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : null;
            decimal? Dec(string key) => Get(key) is { } v ? InvariantFormat.ParseDecimal(v) : null;

            var index = Get("--index") ?? "pgvector";
            if (index is not ("pgvector" or "memory"))
            {
                throw new ArgumentException($"Unknown index '{index}'; use pgvector or memory.");
            }

            return new Options(
                Get("--dataset") ?? Path.Combine("demo-store", "generated", "seed-42"),
                Get("--out"),
                Enum.Parse<EmbeddingProvider>(Get("--embeddings") ?? "openai", ignoreCase: true),
                index,
                Get("--query"),
                Enum.Parse<SearchMode>(Get("--mode") ?? nameof(SearchMode.Hybrid), ignoreCase: true),
                Int("--top") ?? 5,
                new StructuredFilters
                {
                    Category = Get("--category") is { } category ? Enum.Parse<Category>(category, ignoreCase: true) : null,
                    MaxPriceTry = Dec("--max-price"),
                    InStockOnly = flags.Contains("--in-stock"),
                    ShoeSize = Int("--size"),
                    Color = Get("--color"),
                    MinimumLaptopSizeIn = Dec("--min-laptop"),
                    MinimumCapacityLiters = Dec("--min-capacity"),
                    MinimumBatteryHours = Int("--min-battery"),
                    MinimumWarrantyMonths = Int("--min-warranty"),
                });
        }
    }
}
