using CommerceReadiness.Core.Shopping;

namespace CommerceReadiness.Core.Evaluation.Shopping;

/// <summary>
/// Trace-based decision grounding for a mandatory constraint on one recommended SKU.
/// Proves authoritative tool evidence was obtained that satisfies the constraint for that entity/context;
/// it does not claim access to the model's hidden causal reasoning.
/// </summary>
public static class ConstraintEvidence
{
    /// <summary>
    /// Whether the successful tool trace holds AllowedTools evidence for <paramref name="sku"/> whose canonical
    /// value satisfies <paramref name="required"/>. Evidence for another SKU or the wrong city/question/field does
    /// not count.
    /// </summary>
    public static ConstraintGrounding Ground(
        string sku, RequiredClaim required, IReadOnlyDictionary<string, ToolInvocationTrace> trace)
    {
        var probe = new GroundedClaim(required.Type, required.Field, required.City, required.Question, "probe", []);
        var values = new List<(string EvidenceCallId, string Value)>();
        foreach (var entry in trace.Values)
        {
            if (!entry.Ok || entry.Data is null || !ClaimEvidence.AllowedTools(required.Type).Contains(entry.ToolName))
            {
                continue;
            }

            if (ClaimEvidence.EvidenceValue(sku, probe, entry) is { } value)
            {
                values.Add((entry.EvidenceCallId, value));
            }
        }

        if (values.Count == 0)
        {
            return new ConstraintGrounding(required.Constraint, false, "missing evidence");
        }

        var satisfying = values.Where(v => required.IsSatisfiedBy(v.Value)).ToList();
        if (satisfying.Count == 0)
        {
            return new ConstraintGrounding(required.Constraint, false, "evidence does not satisfy the constraint");
        }

        return new ConstraintGrounding(required.Constraint, true, $"trace {satisfying[0].EvidenceCallId}");
    }
}
