using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.SourceOfTruth;
using CommerceReadiness.Core.Storage;

namespace CommerceReadiness.Core.Analysis.Rules;

/// <summary>
/// A return-policy cell without an explicit answer. Yes, No and Conditional are explicit and never flagged;
/// the condition text is not interpreted.
/// </summary>
public static class AmbiguousReturnRule
{
    public static IEnumerable<Finding> Evaluate(StoreSnapshot published)
    {
        foreach (var rule in published.ReturnRules.Where(r => r.Answer == ReturnAnswer.Unspecified).OrderBy(r => r.CellId, StringComparer.Ordinal))
        {
            yield return new Finding(
                $"FND-RET-{rule.CellId.Replace(':', '-')}",
                FindingType.AmbiguousReturnRule,
                FindingSeverities.For(FindingType.AmbiguousReturnRule),
                null,
                null,
                rule.CellId,
                PublishedFields.ReturnAnswer,
                $"Return policy gives no explicit answer for {rule.Category} / {rule.Question}.",
                [
                    new FindingEvidence(DataSource.ReturnPolicy, PublishedFields.ReturnAnswer, rule.Answer.ToString()),
                    new FindingEvidence(DataSource.ReturnPolicy, "conditionTr", rule.ConditionTr),
                ],
                $"State an explicit Yes, No or Conditional answer for {rule.Category} / {rule.Question}.");
        }
    }
}
