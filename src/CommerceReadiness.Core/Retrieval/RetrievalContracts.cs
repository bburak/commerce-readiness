using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Retrieval;

/// <summary>
/// StructuredOnly: hard filters, neutral SKU ordering, no relevance ranking.
/// SemanticOnly: vector similarity over the whole index, no hard filters.
/// Hybrid: hard filters first, then vector similarity ordering.
/// </summary>
public enum SearchMode
{
    StructuredOnly,
    SemanticOnly,
    Hybrid,
}

/// <summary>
/// Exact commerce constraints that are filtered deterministically and never delegated to embeddings.
/// Principle: exact numeric or categorical meaning becomes a filter; fuzzy buyer meaning stays in the query text.
/// A minimum threshold is not met when the published attribute is missing.
/// </summary>
public sealed record StructuredFilters
{
    public Category? Category { get; init; }
    public decimal? MaxPriceTry { get; init; }
    public bool InStockOnly { get; init; }
    public int? ShoeSize { get; init; }
    public string? Color { get; init; }
    public decimal? MinimumLaptopSizeIn { get; init; }
    public decimal? MinimumCapacityLiters { get; init; }
    public int? MinimumBatteryHours { get; init; }
    public int? MinimumWarrantyMonths { get; init; }

    public bool Matches(SearchDocument document) =>
        (Category is not { } category || document.Category == category)
        && (MaxPriceTry is not { } maxPrice || document.PriceTry <= maxPrice)
        && (!InStockOnly || document.AvailableQuantity > 0)
        && (ShoeSize is not { } size || document.ShoeSize == size)
        && (Color is not { } color || document.Color == color)
        && (MinimumLaptopSizeIn is not { } laptop || document.LaptopSizeIn >= laptop)
        && (MinimumCapacityLiters is not { } capacity || document.CapacityLiters >= capacity)
        && (MinimumBatteryHours is not { } battery || document.BatteryHours >= battery)
        && (MinimumWarrantyMonths is not { } warranty || document.WarrantyMonths >= warranty);
}

public sealed record SearchRequest(string StoreVersion, string? QueryText, StructuredFilters Filters, SearchMode Mode, int Top);

/// <summary>
/// One searchable document per published SKU. <see cref="SemanticText"/> is what gets embedded; the structured fields
/// are filter inputs read from authoritative published sources (Pricing for price, Inventory for quantity) and are
/// never embedded.
/// </summary>
public sealed record SearchDocument(
    string StoreVersion,
    string Sku,
    string ProductId,
    Category Category,
    string Title,
    string? Brand,
    string SemanticText,
    string ContentHash,
    decimal PriceTry,
    int AvailableQuantity,
    int? ShoeSize,
    string? Color,
    decimal? LaptopSizeIn,
    decimal? CapacityLiters,
    int? BatteryHours,
    int? WarrantyMonths);

/// <param name="SimilarityScore">1 - cosine distance; null for StructuredOnly.</param>
public sealed record SearchResult(
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
