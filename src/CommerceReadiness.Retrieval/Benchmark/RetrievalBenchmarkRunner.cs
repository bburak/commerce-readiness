using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Evaluation.Retrieval;
using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Retrieval.Embeddings;
using CommerceReadiness.Retrieval.Index;

namespace CommerceReadiness.Retrieval.Benchmark;

/// <param name="RunTimestampUtc">Only set for real-provider runs (observed runs); the hashing baseline stays byte-identical.</param>
public sealed record BenchmarkProvenance(
    string StoreVersion,
    int BenchmarkSchemaVersion,
    EmbeddingProvider EmbeddingProvider,
    string EmbeddingModel,
    int EmbeddingDimensions,
    int K,
    string Index,
    DateTimeOffset? RunTimestampUtc);

/// <param name="Recall">Null for cases that are not scored (no relevant product).</param>
public sealed record ModeCaseResult(SearchMode Mode, ImmutableArray<string> TopSkus, double? Recall, double? Precision, bool? Hit);

public sealed record BenchmarkCaseReport(
    string CaseId,
    RetrievalCaseGroup Group,
    RetrievalOutcome Outcome,
    int RelevantCount,
    int HardFilterPoolSize,
    bool SemanticDiscriminating,
    ImmutableArray<ModeCaseResult> Modes);

public sealed record NegativeCaseSummary(int Cases, int EmptyResults, double? AverageReturned);

public sealed record ModeSummary(
    SearchMode Mode,
    RetrievalAggregate ShopperDerived,
    RetrievalAggregate DiscoveryOnly,
    RetrievalAggregate Combined,
    RetrievalAggregate SemanticDiscriminating,
    NegativeCaseSummary NoRelevantProduct);

public sealed record RetrievalBenchmarkReport(
    BenchmarkProvenance Provenance,
    int ScoredCases,
    int SemanticDiscriminatingCases,
    int NoRelevantProductCases,
    int ExcludedInsufficientCases,
    ImmutableArray<ModeSummary> Modes,
    ImmutableArray<BenchmarkCaseReport> Cases);

/// <summary>
/// Evaluation path: the only retrieval code that reads hidden benchmark data. It runs the same cases through all
/// three search modes over the same index and scores them against relevance computed from the clean store.
/// The result measures published-data readiness plus retrieval strategy, not isolated embedding-model quality.
/// </summary>
public static class RetrievalBenchmarkRunner
{
    public static RetrievalBenchmarkFile LoadBenchmark(string datasetDirectory)
    {
        var evalDirectory = Path.Combine(datasetDirectory, EvalFiles.Folder);
        var path = Path.Combine(evalDirectory, EvalFiles.RetrievalBenchmarkFileName);
        return File.Exists(path)
            ? EvalFiles.ReadRetrievalBenchmark(evalDirectory)
            : throw new FileNotFoundException($"benchmark needs '{path}'.", path);
    }

    public static async Task<RetrievalBenchmarkReport> RunAsync(
        RetrievalBenchmarkFile benchmark,
        StoreSnapshot published,
        EmbeddingSetup embeddings,
        IProductSearchIndex index,
        int k,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        if (benchmark.StoreVersion != published.StoreVersion)
        {
            throw new ArgumentException($"Benchmark is for '{benchmark.StoreVersion}' but the index holds '{published.StoreVersion}'.");
        }

        var modes = Enum.GetValues<SearchMode>();
        var evaluated = benchmark.Cases.Where(c => c.Outcome != RetrievalOutcome.InsufficientDiscoveryInformation).ToList();
        var caseReports = new List<BenchmarkCaseReport>();
        var scores = modes.ToDictionary(m => m, _ => new List<(RetrievalBenchmarkCase Case, CaseRetrievalScore Score)>());
        var returnedOnNegative = modes.ToDictionary(m => m, _ => new List<int>());

        foreach (var benchmarkCase in evaluated)
        {
            var modeResults = new List<ModeCaseResult>();
            foreach (var mode in modes)
            {
                var request = new SearchRequest(published.StoreVersion, benchmarkCase.QueryText, benchmarkCase.Filters, mode, k);
                var results = await ProductSearchService.SearchAsync(request, embeddings, index, cancellationToken);
                ImmutableArray<string> top = [.. results.Select(r => r.Sku)];

                if (benchmarkCase.Outcome == RetrievalOutcome.RelevantProductsExist)
                {
                    var score = RetrievalMetrics.Score(benchmarkCase.CaseId, top, benchmarkCase.RelevantSkus, k);
                    scores[mode].Add((benchmarkCase, score));
                    modeResults.Add(new ModeCaseResult(mode, top, score.Recall, score.Precision, score.Hit));
                }
                else
                {
                    returnedOnNegative[mode].Add(top.Length);
                    modeResults.Add(new ModeCaseResult(mode, top, null, null, null));
                }
            }

            caseReports.Add(new BenchmarkCaseReport(
                benchmarkCase.CaseId,
                benchmarkCase.Group,
                benchmarkCase.Outcome,
                benchmarkCase.RelevantSkus.Length,
                benchmarkCase.HardFilterPoolSize,
                benchmarkCase.IsSemanticDiscriminating,
                [.. modeResults]));
        }

        var summaries = modes.Select(mode =>
        {
            var all = scores[mode];
            RetrievalAggregate Of(Func<RetrievalBenchmarkCase, bool> include) =>
                RetrievalMetrics.Aggregate([.. all.Where(x => include(x.Case)).Select(x => x.Score)]);
            var negative = returnedOnNegative[mode];
            return new ModeSummary(
                mode,
                Of(c => c.Group == RetrievalCaseGroup.ShopperDerived),
                Of(c => c.Group == RetrievalCaseGroup.DiscoveryOnly),
                Of(_ => true),
                Of(c => c.IsSemanticDiscriminating),
                new NegativeCaseSummary(
                    negative.Count,
                    negative.Count(n => n == 0),
                    negative.Count == 0 ? null : Math.Round(negative.Average(), 4)));
        });

        var provenance = new BenchmarkProvenance(
            published.StoreVersion,
            benchmark.SchemaVersion,
            embeddings.Profile.Provider,
            embeddings.Profile.Model,
            embeddings.Profile.Dimensions,
            k,
            index.Name,
            embeddings.Profile.Provider == EmbeddingProvider.Hashing ? null : clock.GetUtcNow());

        return new RetrievalBenchmarkReport(
            provenance,
            evaluated.Count(c => c.Outcome == RetrievalOutcome.RelevantProductsExist),
            evaluated.Count(c => c.IsSemanticDiscriminating),
            evaluated.Count(c => c.Outcome == RetrievalOutcome.NoRelevantProduct),
            benchmark.Cases.Length - evaluated.Count,
            [.. summaries],
            [.. caseReports]);
    }
}
