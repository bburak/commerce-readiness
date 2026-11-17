namespace CommerceReadiness.Core.Domain;

/// <summary>
/// A complete, immutable state of the store, split by source. The same shape is used for the clean,
/// published and (future) repaired stores; only <see cref="Kind"/> tells them apart.
/// </summary>
public sealed record StoreSnapshot(
    StoreKind Kind,
    string StoreVersion,
    ImmutableArray<Product> Products,
    ImmutableArray<ProductVariant> Variants,
    ImmutableArray<CatalogListing> Listings,
    ImmutableArray<PriceEntry> Prices,
    ImmutableArray<InventoryRecord> Inventory,
    ImmutableArray<Warehouse> Warehouses,
    ImmutableArray<ShippingRule> ShippingRules,
    ImmutableArray<InstallmentRule> InstallmentRules,
    ImmutableArray<ReturnRule> ReturnRules)
{
    public Product GetProduct(string productId) => Products.First(p => p.ProductId == productId);

    public ProductVariant GetVariant(string sku) => Variants.First(v => v.Sku == sku);

    public Product ProductOf(string sku) => GetProduct(GetVariant(sku).ProductId);

    public IEnumerable<ProductVariant> VariantsOf(string productId) => Variants.Where(v => v.ProductId == productId);

    public CatalogListing GetListing(string sku) => Listings.First(l => l.Sku == sku);

    public decimal GetPrice(string sku) => Prices.First(p => p.Sku == sku).PriceTry;

    public InventoryRecord GetInventory(string sku) => Inventory.First(i => i.Sku == sku);

    public ReturnRule? FindReturnRule(Category category, ReturnQuestion question) =>
        ReturnRules.FirstOrDefault(r => r.Category == category && r.Question == question);
}
