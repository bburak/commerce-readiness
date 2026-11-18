using System.Text.Json;
using CommerceReadiness.Core.Shopping;

namespace CommerceReadiness.Core.Evaluation.Shopping;

/// <summary>
/// Provenance diagnostic for model-controlled <c>search_products</c> depth. Derived from frozen tool traces;
/// not a shopper correctness metric.
/// </summary>
public sealed record SearchTopDiagnostic(int TotalSearchCalls, ImmutableSortedDictionary<int, int> SearchTopDistribution)
{
    /// <summary>
    /// Counts every <c>search_products</c> invocation. Uses the requested <c>top</c> when present; when omitted,
    /// counts <paramref name="defaultSearchTop"/> (the server-applied default).
    /// </summary>
    public static SearchTopDiagnostic FromRuns(IReadOnlyList<ShopperRunRecord> runs, int defaultSearchTop)
    {
        var counts = new SortedDictionary<int, int>();
        var total = 0;
        foreach (var entry in runs.SelectMany(r => r.Run.Trace)
                     .Where(t => t.ToolName == ShopperToolNames.SearchProducts))
        {
            total++;
            int top;
            try
            {
                top = ClaimEvidence.Read<SearchProductsArguments>(entry.Arguments).Top ?? defaultSearchTop;
            }
            catch (JsonException)
            {
                top = defaultSearchTop;
            }

            counts[top] = counts.GetValueOrDefault(top) + 1;
        }

        return new SearchTopDiagnostic(total, counts.ToImmutableSortedDictionary());
    }
}
