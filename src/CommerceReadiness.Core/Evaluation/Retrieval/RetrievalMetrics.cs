namespace CommerceReadiness.Core.Evaluation.Retrieval;

/// <param name="Precision">Null when nothing was returned.</param>
/// <param name="MaxAchievableRecall">min(K, relevant) / relevant: Recall@K cannot exceed this when more than K SKUs are relevant.</param>
public sealed record CaseRetrievalScore(
    string CaseId,
    int Relevant,
    int Returned,
    int RelevantInTopK,
    double Recall,
    double? Precision,
    bool Hit,
    double MaxAchievableRecall);

/// <summary>Macro averages over cases; null when there is no case to average.</summary>
public sealed record RetrievalAggregate(
    int Cases,
    double? RecallAtK,
    double? PrecisionAtK,
    int CasesWithResults,
    double? HitRateAtK,
    double? MaxAchievableRecallAtK);

/// <summary>
/// Recall@K = relevant SKUs in the top K / all relevant SKUs.
/// Precision@K = relevant SKUs in the top K / SKUs returned (at most K).
/// HitRate@K = share of cases with at least one relevant SKU in the top K.
/// </summary>
public static class RetrievalMetrics
{
    public static CaseRetrievalScore Score(string caseId, IReadOnlyList<string> rankedSkus, IReadOnlyCollection<string> relevantSkus, int k)
    {
        if (relevantSkus.Count == 0)
        {
            throw new ArgumentException("Only cases with relevant SKUs are scored.", nameof(relevantSkus));
        }

        var relevant = relevantSkus.ToHashSet(StringComparer.Ordinal);
        var top = rankedSkus.Take(k).ToList();
        var hits = top.Count(relevant.Contains);
        return new CaseRetrievalScore(
            caseId,
            relevant.Count,
            top.Count,
            hits,
            Round((double)hits / relevant.Count),
            top.Count == 0 ? null : Round((double)hits / top.Count),
            hits > 0,
            Round((double)Math.Min(k, relevant.Count) / relevant.Count));
    }

    public static RetrievalAggregate Aggregate(IReadOnlyCollection<CaseRetrievalScore> scores)
    {
        var precisions = scores.Where(s => s.Precision is not null).Select(s => s.Precision!.Value).ToList();
        return new RetrievalAggregate(
            scores.Count,
            Mean(scores.Select(s => s.Recall)),
            Mean(precisions),
            precisions.Count,
            Mean(scores.Select(s => s.Hit ? 1.0 : 0.0)),
            Mean(scores.Select(s => s.MaxAchievableRecall)));
    }

    private static double? Mean(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Round(list.Average());
    }

    private static double Round(double value) => Math.Round(value, 4);
}
