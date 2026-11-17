using CommerceReadiness.Core.SourceOfTruth;

namespace CommerceReadiness.Core.Evaluation.ShopperContract;

// Output contract for the future LLM shopper (Milestone 3). Nothing produces it yet; it is fixed now because
// deterministic evaluation (Milestone 4) depends on structured output. Evaluation will never use an LLM as judge.
//
// Unsupported Claim: a claim that has no evidence (EvidenceCallId is null or unknown) or contradicts the facts
// of the evidence record it references.
//
// Confident Wrong Recommendation: a recommended SKU that violates at least one mandatory intent constraint
// (per the oracle) while Uncertain is false.
//
// Abstention: Abstained is true and Recommendations is empty because the oracle outcome is NoValidProduct or
// InsufficientInformation. Abstaining when valid products exist is a missed answer, not a correct abstention.

public enum ClaimType
{
    InStock,
    DeliveryBy,
    Price,
    InstallmentCount,
    ReturnEligible,
    WaterResistance,
    LaptopCompatibility,
    ShoeSize,
    BatteryHours,
    NoiseCancellation,
}

/// <param name="Value">Invariant-formatted claimed value, e.g. "true", "2026-11-18", "1299.90".</param>
/// <param name="EvidenceCallId">The tool call whose result supports this claim; null means unsupported.</param>
public sealed record Claim(ClaimType Type, string Value, string? EvidenceCallId);

public sealed record Recommendation(string Sku, ImmutableArray<Claim> Claims, bool Uncertain);

public sealed record ShopperResult(
    string IntentId,
    ImmutableArray<Recommendation> Recommendations,
    bool Abstained,
    ImmutableArray<string> MissingInformation);

/// <summary>A recorded tool call result the shopper can cite as evidence.</summary>
public sealed record EvidenceRecord(
    string CallId,
    string Tool,
    DataSource Source,
    ImmutableSortedDictionary<string, string> Facts);
