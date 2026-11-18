using System.Text.Json;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Shopping;

namespace CommerceReadiness.Core.Evaluation.Shopping;

public enum ClaimVerdict
{
    Supported,
    Malformed,
    MissingEvidence,
    UnknownEvidence,
    WrongSource,
    FailedEvidence,
    WrongEntity,
    Contradicted,
}

/// <summary>
/// Deterministic claim-evidence matrix: which tool may support which claim type, and what canonical value the cited
/// tool result states for the claim's SKU (and city or return question). No text is interpreted.
/// </summary>
public static class ClaimEvidence
{
    private static readonly ClaimVerdict[] FailurePriority =
        [ClaimVerdict.WrongEntity, ClaimVerdict.FailedEvidence, ClaimVerdict.WrongSource, ClaimVerdict.UnknownEvidence];

    public static ImmutableArray<string> AllowedTools(ClaimType type) => type switch
    {
        ClaimType.ProductAttribute => [ShopperToolNames.GetProductDetails],
        ClaimType.Price => [ShopperToolNames.SearchProducts, ShopperToolNames.GetProductDetails],
        ClaimType.Availability => [ShopperToolNames.GetInventory],
        ClaimType.Delivery => [ShopperToolNames.GetDeliveryEstimate],
        ClaimType.ReturnPolicy => [ShopperToolNames.GetReturnPolicy],
        ClaimType.PaymentOption => [ShopperToolNames.GetPaymentOptions],
        _ => [],
    };

    public static ClaimVerdict Verify(string sku, GroundedClaim claim, IReadOnlyDictionary<string, ToolInvocationTrace> trace)
    {
        if (!ClaimFormat.IsWellFormed(claim))
        {
            return ClaimVerdict.Malformed;
        }

        if (claim.EvidenceCallIds.IsDefaultOrEmpty)
        {
            return ClaimVerdict.MissingEvidence;
        }

        var verdicts = claim.EvidenceCallIds.Select(id => VerifyOne(sku, claim, id, trace)).ToList();
        if (verdicts.Contains(ClaimVerdict.Contradicted))
        {
            return ClaimVerdict.Contradicted;
        }

        return verdicts.Contains(ClaimVerdict.Supported)
            ? ClaimVerdict.Supported
            : FailurePriority.First(verdicts.Contains);
    }

    /// <summary>The canonical value a successful tool result states for the claim, or null if it says nothing about it.</summary>
    public static string? EvidenceValue(string sku, GroundedClaim claim, ToolInvocationTrace entry)
    {
        if (!entry.Ok || entry.Data is not { } data)
        {
            return null;
        }

        switch (entry.ToolName)
        {
            case ShopperToolNames.SearchProducts when claim.Type == ClaimType.Price:
                return Read<SearchProductsData>(data).Candidates.FirstOrDefault(c => c.Sku == sku) is { } candidate
                    ? InvariantFormat.Money(candidate.PriceTry)
                    : null;
            case ShopperToolNames.GetProductDetails:
                var details = Read<ProductDetailsData>(data).Products.FirstOrDefault(p => p.Sku == sku);
                return claim.Type == ClaimType.Price
                    ? details is null ? null : InvariantFormat.Money(details.PriceTry)
                    : details?.Attributes.FirstOrDefault(a => a.Field == claim.AttributeField)?.Value;
            case ShopperToolNames.GetInventory:
                return Read<InventoryData>(data).Items.FirstOrDefault(i => i.Sku == sku) is { } item
                    ? item.AvailableQuantity > 0 ? ClaimFormat.InStock : ClaimFormat.OutOfStock
                    : null;
            case ShopperToolNames.GetDeliveryEstimate:
                var delivery = Read<DeliveryEstimateData>(data);
                if (delivery.Sku != sku || delivery.City != claim.City)
                {
                    return null;
                }

                return delivery.EstimatedDeliveryDate is { } date ? InvariantFormat.Date(date) : "notDeliverable";
            case ShopperToolNames.GetReturnPolicy:
                var policy = Read<ReturnPolicyData>(data);
                return policy.Sku == sku && policy.Question == claim.ReturnQuestion ? ClaimFormat.Return(policy.Answer, policy.WindowDays) : null;
            case ShopperToolNames.GetPaymentOptions:
                var payment = Read<PaymentOptionsData>(data);
                return payment.Sku == sku ? InvariantFormat.Number(payment.MaxInstallments) : null;
            default:
                return null;
        }
    }

    public static T Read<T>(JsonElement data) =>
        data.Deserialize<T>(JsonDefaults.Options) ?? throw new JsonException($"Trace data is not a {typeof(T).Name}.");

    private static ClaimVerdict VerifyOne(string sku, GroundedClaim claim, string evidenceCallId, IReadOnlyDictionary<string, ToolInvocationTrace> trace)
    {
        if (!trace.TryGetValue(evidenceCallId, out var entry))
        {
            return ClaimVerdict.UnknownEvidence;
        }

        if (!AllowedTools(claim.Type).Contains(entry.ToolName))
        {
            return ClaimVerdict.WrongSource;
        }

        if (!entry.Ok)
        {
            return ClaimVerdict.FailedEvidence;
        }

        return EvidenceValue(sku, claim, entry) is not { } value ? ClaimVerdict.WrongEntity
            : ClaimFormat.Equivalent(claim, value) ? ClaimVerdict.Supported
            : ClaimVerdict.Contradicted;
    }
}
