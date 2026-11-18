using CommerceReadiness.Core.Domain;
using CommerceReadiness.Shopper.Tools;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Evaluation.Shopping;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Core.Storage;
using CommerceReadiness.Retrieval;
using CommerceReadiness.Retrieval.Embeddings;
using CommerceReadiness.Shopper.Agent;
using Microsoft.Extensions.AI;

namespace CommerceReadiness.Shopper;

/// <summary>
/// ask uses only published/ plus tools. benchmark additionally loads clean/ and eval/intents.json for scoring.
/// rescore re-evaluates a frozen benchmark file without calling the model.
/// </summary>
public static class ShopperCli
{
    public const string RunsFolder = "shopper-runs";

    public static Task<int> RunAsync(
        string[] args, TextWriter output, TextWriter error, RetrievalSettings retrieval, ShopperSettings shopper, CancellationToken cancellationToken = default) =>
        RunAsync(args, output, error, retrieval, shopper, chat: null, TimeProvider.System, cancellationToken);

    /// <param name="chat">Optional override for offline tests; production passes null and uses OpenAI.</param>
    public static async Task<int> RunAsync(
        string[] args, TextWriter output, TextWriter error, RetrievalSettings retrieval, ShopperSettings shopper, IChatClient? chat, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            PrintUsage(output);
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var options = Options.Parse(args.Skip(1)) with { Chat = chat };
            return args[0] switch
            {
                "ask" => await AskAsync(options, output, retrieval, shopper, cancellationToken),
                "benchmark" => await BenchmarkAsync(options, output, retrieval, shopper, clock, cancellationToken),
                "rescore" => Rescore(options, output),
                _ => Unknown(error, args[0]),
            };
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidDataException or ArgumentException
                                       or InvalidOperationException or FormatException or System.Text.Json.JsonException
                                       or DirectoryNotFoundException)
        {
            error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int Unknown(TextWriter error, string command)
    {
        error.WriteLine($"Unknown command '{command}'. Use --help.");
        return 1;
    }

    private static async Task<int> AskAsync(
        Options options, TextWriter output, RetrievalSettings retrieval, ShopperSettings shopper, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Query))
        {
            throw new ArgumentException("--query is required for ask.");
        }

        shopper = WithModel(shopper, options.Model);
        await using var host = await ShopperHost.OpenAsync(options.Dataset, retrieval, options.Embeddings, options.Index, cancellationToken);
        using var chat = CreateChat(retrieval, shopper, options.Chat);
        var run = await ShopperAgent.RunAsync(chat, host.Tools, shopper, options.Query, cancellationToken);
        PrintRun(output, run, options.ShowTools);
        return run.Status == ShopperRunStatus.Completed ? 0 : 3;
    }

    private static async Task<int> BenchmarkAsync(
        Options options, TextWriter output, RetrievalSettings retrieval, ShopperSettings shopper, TimeProvider clock, CancellationToken cancellationToken)
    {
        shopper = WithModel(shopper, options.Model);
        var intents = EvalFiles.ReadIntents(Path.Combine(options.Dataset, EvalFiles.Folder));
        var clean = StoreFiles.Read(Path.Combine(options.Dataset, DatasetLayout.CleanFolder));
        if (clean.Manifest.Kind != StoreKind.Clean)
        {
            throw new InvalidDataException("benchmark needs the clean authoritative store.");
        }

        var groundTruth = EvalFiles.ReadGroundTruth(Path.Combine(options.Dataset, EvalFiles.Folder));
        await using var host = await ShopperHost.OpenAsync(options.Dataset, retrieval, options.Embeddings, options.Index, cancellationToken);
        using var chat = CreateChat(retrieval, shopper, options.Chat);

        var records = new List<ShopperRunRecord>();
        foreach (var intent in intents.Intents)
        {
            for (var runIndex = 1; runIndex <= options.Runs; runIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.WriteLine($"[{intent.Id} run {runIndex}/{options.Runs}] {Truncate(intent.Text, 72)}");
                var run = await ShopperAgent.RunAsync(chat, host.Tools, shopper, intent.Text, cancellationToken);
                records.Add(new ShopperRunRecord(intent.Id, runIndex, run));
                if (options.ShowTools)
                {
                    output.Write(ShopperResultRenderer.RenderTrace(run));
                }

                output.WriteLine($"  -> {run.Status}" + (run.Result is { } result ? $" {result.Outcome}" : $" ({run.StatusDetail})"));
            }
        }

        var report = ShopperEvaluator.Evaluate(clean.Store, host.Published.Store, intents, groundTruth, records);
        var searchTop = SearchTopDiagnostic.FromRuns(records, CommerceTools.DefaultTop);
        var file = new ShopperBenchmarkFile(
            SchemaVersion: 1,
            StoreVersion: host.Published.Store.StoreVersion,
            PromptVersion: shopper.PromptVersion,
            ModelId: shopper.Model,
            ReasoningEffort: shopper.ReasoningEffort,
            EmbeddingProvider: host.Embeddings.Profile.Provider.ToString(),
            EmbeddingModel: host.Embeddings.Profile.Model,
            EmbeddingDimensions: host.Embeddings.Profile.Dimensions,
            RetrievalMode: "Hybrid",
            DefaultSearchTop: CommerceTools.DefaultTop,
            MaxSearchTop: CommerceTools.MaxTop,
            TotalSearchCalls: searchTop.TotalSearchCalls,
            SearchTopDistribution: searchTop.SearchTopDistribution,
            RunsPerIntent: options.Runs,
            RunTimestampUtc: clock.GetUtcNow(),
            Report: report,
            Runs: [.. records]);

        var directory = options.Out ?? Path.Combine(options.Dataset, RunsFolder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"benchmark-{Sanitize(shopper.Model)}-{shopper.PromptVersion}.json");
        TextFiles.Write(path, JsonDefaults.Serialize(file));
        PrintReport(output, report, searchTop, path);
        return 0;
    }

    /// <summary>Re-runs <see cref="ShopperEvaluator"/> on a frozen benchmark JSON; does not call OpenAI or tools.</summary>
    private static int Rescore(Options options, TextWriter output)
    {
        var path = options.File
            ?? throw new ArgumentException("--file is required for rescore (path to a frozen benchmark JSON).");
        var file = ReadBenchmarkFile(path);
        var intents = EvalFiles.ReadIntents(Path.Combine(options.Dataset, EvalFiles.Folder));
        var clean = StoreFiles.Read(Path.Combine(options.Dataset, DatasetLayout.CleanFolder));
        var published = StoreFiles.Read(Path.Combine(options.Dataset, DatasetLayout.PublishedFolder));
        if (clean.Manifest.Kind != StoreKind.Clean)
        {
            throw new InvalidDataException("rescore needs the clean authoritative store.");
        }

        var groundTruth = EvalFiles.ReadGroundTruth(Path.Combine(options.Dataset, EvalFiles.Folder));
        var report = ShopperEvaluator.Evaluate(clean.Store, published.Store, intents, groundTruth, file.Runs);
        var searchTop = SearchTopDiagnostic.FromRuns(file.Runs, CommerceTools.DefaultTop);
        var updated = file with
        {
            Report = report,
            DefaultSearchTop = CommerceTools.DefaultTop,
            MaxSearchTop = CommerceTools.MaxTop,
            TotalSearchCalls = searchTop.TotalSearchCalls,
            SearchTopDistribution = searchTop.SearchTopDistribution,
        };
        TextFiles.Write(path, JsonDefaults.Serialize(updated));
        PrintReport(output, report, searchTop, path);
        return 0;
    }

    /// <summary>Loads a benchmark file, migrating legacy <c>defaultTop</c> provenance to the current schema.</summary>
    private static ShopperBenchmarkFile ReadBenchmarkFile(string path)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)) as System.Text.Json.Nodes.JsonObject
            ?? throw new InvalidDataException("benchmark file must be a JSON object.");
        if (node.ContainsKey("defaultTop") && !node.ContainsKey("defaultSearchTop"))
        {
            node["defaultSearchTop"] = node["defaultTop"]!.DeepClone();
            node.Remove("defaultTop");
        }

        node["defaultSearchTop"] ??= CommerceTools.DefaultTop;
        node["maxSearchTop"] ??= CommerceTools.MaxTop;
        node["totalSearchCalls"] ??= 0;
        node["searchTopDistribution"] ??= new System.Text.Json.Nodes.JsonObject();
        return System.Text.Json.JsonSerializer.Deserialize<ShopperBenchmarkFile>(node, JsonDefaults.Options)
            ?? throw new InvalidDataException("Could not deserialize ShopperBenchmarkFile.");
    }

    private static IChatClient CreateChat(RetrievalSettings retrieval, ShopperSettings shopper, IChatClient? overrideChat) =>
        overrideChat ?? ShopperChatClientFactory.CreateOpenAi(retrieval.OpenAiApiKey, shopper);

    private static ShopperSettings WithModel(ShopperSettings settings, string? model) =>
        model is null ? settings : ShopperConfiguration.Validate(settings with { Model = model });

    private static void PrintRun(TextWriter output, ShopperRun run, bool showTools)
    {
        output.WriteLine($"status={run.Status} model={run.ModelId ?? "-"} durationMs={run.DurationMs}");
        if (run.Usage is { } usage)
        {
            output.WriteLine($"tokens in={usage.InputTokens?.ToString(CultureInfo.InvariantCulture) ?? "-"} out={usage.OutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        }

        if (showTools)
        {
            output.WriteLine("tools:");
            output.Write(ShopperResultRenderer.RenderTrace(run));
        }

        output.WriteLine("result:");
        output.Write(ShopperResultRenderer.Render(run));
        if (run.Result is { } result)
        {
            output.WriteLine("json:");
            output.WriteLine(JsonDefaults.Serialize(result).TrimEnd());
        }
    }

    private static void PrintReport(TextWriter output, ShopperEvaluationReport report, SearchTopDiagnostic searchTop, string path)
    {
        output.WriteLine("───────────────────────────");
        output.WriteLine($"Shopper evaluation  attempted={report.AttemptedRuns} intents={report.Intents}");
        foreach (var metric in report.Primary)
        {
            output.WriteLine($"  {metric.Name,-36} {Format(metric)}");
        }

        output.WriteLine(
            $"search-top  totalSearchCalls={searchTop.TotalSearchCalls}  defaultSearchTop={CommerceTools.DefaultTop}  maxSearchTop={CommerceTools.MaxTop}");
        foreach (var (top, count) in searchTop.SearchTopDistribution)
        {
            output.WriteLine($"  top={top,-3} {count}");
        }

        output.WriteLine("diagnostics:");
        foreach (var metric in report.Diagnostics)
        {
            output.WriteLine($"  {metric.Name,-36} {Format(metric)}");
        }

        output.WriteLine("stability:");
        foreach (var intent in report.Stability)
        {
            output.WriteLine($"  {intent.IntentId}  {string.Join(" / ", intent.Outcomes)}");
        }

        if (!report.MisleadingDescription.IsDefaultOrEmpty)
        {
            output.WriteLine("misleading-description:");
            foreach (var msd in report.MisleadingDescription)
            {
                output.WriteLine(
                    $"  {msd.Sku} exposed={msd.RunsExposed} details={msd.RunsWithDetailsFetched} confirmed={msd.ConfirmedRecommendations} " +
                    $"confirmedInvalid={msd.ConfirmedInvalidRecommendations} misleadingClaims={msd.MisleadingAttributeClaims}");
            }
        }

        output.WriteLine($"Written to {path}");
    }

    private static string Format(ShopperMetric metric) =>
        metric.Value is { } value
            ? $"{value:0.0000}  ({metric.Numerator}/{metric.Denominator})"
            : $"n/a  (0/{metric.Denominator})";

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string Sanitize(string model) =>
        new string([.. model.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-')]);

    private static void PrintUsage(TextWriter output)
    {
        output.WriteLine("Usage: CommerceReadiness.Shopper <command> [options]");
        output.WriteLine("  ask        --query <text> [--dataset <dir>] [--model <id>] [--embeddings openai|hashing]");
        output.WriteLine("             [--index pgvector|memory] [--show-tools]");
        output.WriteLine("  benchmark  --dataset <dir> [--runs 3] [--model <id>] [--embeddings ...] [--index ...] [--out <dir>] [--show-tools]");
        output.WriteLine("  rescore    --file <benchmark.json> [--dataset <dir>]");
        output.WriteLine("ask reads only <dataset>/published/. benchmark also reads clean/ and eval/ for scoring.");
        output.WriteLine("rescore re-evaluates a frozen benchmark JSON without calling OpenAI.");
    }

    private sealed record Options(
        string Dataset,
        string? Out,
        string? File,
        string? Query,
        string? Model,
        int Runs,
        EmbeddingProvider Embeddings,
        string Index,
        bool ShowTools)
    {
        public IChatClient? Chat { get; init; }

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

                if (key is "--show-tools")
                {
                    flags.Add(key);
                    continue;
                }

                values[key] = enumerator.MoveNext() ? enumerator.Current : throw new ArgumentException($"Missing value for '{key}'.");
            }

            string? Get(string key) => values.GetValueOrDefault(key);
            var index = Get("--index") ?? "pgvector";
            if (index is not ("pgvector" or "memory"))
            {
                throw new ArgumentException($"Unknown index '{index}'; use pgvector or memory.");
            }

            var runs = Get("--runs") is { } text
                ? int.Parse(text, CultureInfo.InvariantCulture)
                : 3;
            if (runs is < 1 or > 10)
            {
                throw new ArgumentException("--runs must be between 1 and 10.");
            }

            return new Options(
                Get("--dataset") ?? Path.Combine("demo-store", "generated", "seed-42"),
                Get("--out"),
                Get("--file"),
                Get("--query"),
                Get("--model"),
                runs,
                Enum.Parse<EmbeddingProvider>(Get("--embeddings") ?? "openai", ignoreCase: true),
                index,
                flags.Contains("--show-tools"));
        }
    }
}

/// <summary>
/// Compact evaluated shopper benchmark file. Secrets and raw provider payloads are never written.
/// Retrieval depth is model-controlled per <c>search_products</c> call (default/max recorded here; actual
/// requested <c>top</c> is preserved in each tool trace). Milestone 2 retrieval benchmarks used fixed K=5;
/// those depths must not be treated as the same experimental configuration.
/// </summary>
public sealed record ShopperBenchmarkFile(
    int SchemaVersion,
    string StoreVersion,
    string PromptVersion,
    string ModelId,
    string? ReasoningEffort,
    string EmbeddingProvider,
    string EmbeddingModel,
    int EmbeddingDimensions,
    string RetrievalMode,
    int DefaultSearchTop,
    int MaxSearchTop,
    int TotalSearchCalls,
    ImmutableSortedDictionary<int, int> SearchTopDistribution,
    int RunsPerIntent,
    DateTimeOffset RunTimestampUtc,
    ShopperEvaluationReport Report,
    ImmutableArray<ShopperRunRecord> Runs);
