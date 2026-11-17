namespace CommerceReadiness.Core.Domain;

/// <summary>Product identity, description and product-level attributes. Owned by the Catalog source.</summary>
public sealed record Product(
    string ProductId,
    Category Category,
    string Title,
    string Description,
    ImmutableSortedDictionary<string, string> Attributes);

/// <summary>
/// A purchasable SKU. Single-SKU categories have exactly one variant per product and no variant attributes;
/// Sneakers carry size and color at this level.
/// </summary>
public sealed record ProductVariant(
    string Sku,
    string ProductId,
    ImmutableSortedDictionary<string, string> Attributes);

/// <summary>
/// The storefront row of a SKU as published in catalog.csv. Price, stock status and delivery promise are
/// projections of the Pricing, Inventory and Shipping sources and are never authoritative themselves.
/// </summary>
public sealed record CatalogListing(
    string Sku,
    decimal ListedPrice,
    decimal? CompareAtPrice,
    StockStatus StockStatus,
    DateOnly? DeliveryPromiseIstanbul);

/// <summary>Authoritative price of a SKU. Owned by the Pricing source.</summary>
public sealed record PriceEntry(string Sku, decimal PriceTry);

/// <summary>Authoritative physical stock of a SKU. Owned by the Inventory source. One warehouse per SKU in V0.</summary>
public sealed record InventoryRecord(string Sku, string WarehouseId, int Available);
