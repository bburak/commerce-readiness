using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Time;

namespace CommerceReadiness.Core.Shopping;

public sealed record SkuViolations(string Sku, ImmutableArray<ConstraintViolation> Violations);

public sealed record OracleResult(
    IntentOutcome Outcome,
    ImmutableArray<string> ValidSkus,
    ImmutableArray<SkuViolations> Forbidden,
    ImmutableArray<string> MissingInformation);

/// <summary>
/// Deterministic source of truth for shopper evaluation. It only ever reads the clean authoritative store,
/// so broken published data can never leak into expected results.
/// </summary>
public static class OracleShopper
{
    public static OracleResult Evaluate(StoreSnapshot clean, ShopperConstraints constraints, SimulationContext context)
    {
        if (clean.Kind != StoreKind.Clean)
        {
            throw new ArgumentException("The oracle only accepts the clean authoritative store.", nameof(clean));
        }

        var missing = ConstraintEvaluator.MissingInformation(constraints);
        if (missing.Length > 0)
        {
            return new OracleResult(IntentOutcome.InsufficientInformation, [], [], missing);
        }

        var facts = new AuthoritativeFacts(clean, context.Now);
        var valid = new List<string>();
        var forbidden = new List<SkuViolations>();
        foreach (var sku in facts.AllSkus.Where(s => facts.CategoryOf(s) == constraints.Category))
        {
            var violations = ConstraintEvaluator.Evaluate(constraints, sku, facts, context.Now);
            if (violations.IsEmpty)
            {
                valid.Add(sku);
            }
            else
            {
                forbidden.Add(new SkuViolations(sku, violations));
            }
        }

        var outcome = valid.Count > 0 ? IntentOutcome.ValidProductsExist : IntentOutcome.NoValidProduct;
        return new OracleResult(outcome, [.. valid], [.. forbidden], []);
    }
}
