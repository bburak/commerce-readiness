using System.Text.Json;
using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Shopping;

/// <summary>The complete, read-only shopper tool set. No tool places orders, reserves stock or takes payments.</summary>
public static class ShopperToolNames
{
    public const string SearchProducts = "search_products";
    public const string GetProductDetails = "get_product_details";
    public const string GetInventory = "get_inventory";
    public const string GetDeliveryEstimate = "get_delivery_estimate";
    public const string GetReturnPolicy = "get_return_policy";
    public const string GetPaymentOptions = "get_payment_options";

    public static readonly ImmutableArray<string> All =
        [SearchProducts, GetProductDetails, GetInventory, GetDeliveryEstimate, GetReturnPolicy, GetPaymentOptions];
}

public static class ToolErrorCodes
{
    public const string InvalidArgument = "InvalidArgument";
    public const string UnknownSku = "UnknownSku";
    public const string NoReturnRule = "NoReturnRule";
    public const string UnexpectedToolFailure = "UnexpectedToolFailure";
    public const string ToolBudgetExceeded = "ToolBudgetExceeded";
}

// Tool arguments exactly as the model supplied them; enum-like values stay strings so the application can reject
// unsupported values with a typed tool error.

public sealed record SearchProductsArguments(
    string? Query,
    string? Category,
    decimal? MaxPriceTry,
    bool? InStockOnly,
    int? ShoeSize,
    string? Color,
    decimal? MinimumLaptopSizeIn,
    decimal? MinimumCapacityLiters,
    int? MinimumBatteryHours,
    int? MinimumWarrantyMonths,
    int? Top);

public sealed record SkuListArguments(ImmutableArray<string> Skus);

public sealed record DeliveryEstimateArguments(string? Sku, string? City, int? WithinDays);

public sealed record ReturnPolicyArguments(string? Sku, string? Question);

public sealed record PaymentOptionsArguments(string? Sku);

/// <param name="SimilarityScore">Discovery relevance only; never evidence for a product attribute.</param>
public sealed record ProductCandidate(
    int Rank,
    string Sku,
    string ProductId,
    string Title,
    Category Category,
    decimal PriceTry,
    int AvailableQuantity,
    int? ShoeSize,
    string? Color,
    double? SimilarityScore);

public sealed record SearchProductsData(ImmutableArray<ProductCandidate> Candidates);

public sealed record ProductAttributeValue(ProductAttributeField Field, string Value);

/// <summary>Structured published catalog facts. The free-text description is deliberately not part of it.</summary>
public sealed record ProductDetails(
    string Sku,
    string ProductId,
    string Title,
    Category Category,
    decimal PriceTry,
    ImmutableArray<ProductAttributeValue> Attributes);

public sealed record ProductDetailsData(ImmutableArray<ProductDetails> Products);

public sealed record InventoryItem(string Sku, int AvailableQuantity, string WarehouseId);

public sealed record InventoryData(ImmutableArray<InventoryItem> Items);

/// <param name="Deadline">Today + WithinDays calendar days, when WithinDays was given.</param>
public sealed record DeliveryEstimateData(
    string Sku,
    City City,
    DateOnly OrderDate,
    bool CanEstimate,
    DateOnly? EstimatedDeliveryDate,
    string? WarehouseId,
    int? CutoffHour,
    int? TransitBusinessDays,
    DateOnly? Deadline,
    bool? MeetsDeadline);

public sealed record ReturnPolicyData(
    string Sku,
    Category Category,
    ReturnQuestion Question,
    ReturnAnswer Answer,
    int? WindowDays,
    string? ConditionTr);

public sealed record PaymentOptionsData(string Sku, Category Category, decimal PriceTry, string Currency, int MaxInstallments);

/// <summary>What the model sees for every tool call. The evidence id is issued by the application, never by the model.</summary>
public sealed record ToolResultEnvelope<T>(string EvidenceCallId, string ToolName, bool Ok, string? ErrorCode, string? ErrorMessage, T? Data);

/// <summary>Machine-readable record of one tool call; part of the evaluation evidence.</summary>
public sealed record ToolInvocationTrace(
    int Sequence,
    string EvidenceCallId,
    string ToolName,
    JsonElement Arguments,
    bool Ok,
    string? ErrorCode,
    string? ErrorMessage,
    JsonElement? Data,
    long DurationMs);

public enum ShopperRunStatus
{
    Completed,
    InvalidResult,
    ToolBudgetExceeded,
    ToolExecutionError,
    ProviderError,
}

public sealed record ShopperUsage(long? InputTokens, long? OutputTokens, long? TotalTokens);

/// <summary>One single-turn shopper run: the structured result (if any) plus its tool trace.</summary>
public sealed record ShopperRun(
    ShopperRunStatus Status,
    string? StatusDetail,
    ShopperResult? Result,
    ImmutableArray<ToolInvocationTrace> Trace,
    ShopperUsage? Usage,
    string? ModelId,
    long DurationMs);
