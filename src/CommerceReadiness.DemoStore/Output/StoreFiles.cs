using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.SourceOfTruth;

namespace CommerceReadiness.DemoStore.Output;

public sealed record PriceListFile(DataSource Source, string Currency, ImmutableArray<PriceEntry> Prices);

public sealed record InventoryFile(DataSource Source, ImmutableArray<InventoryRecord> Records);

public sealed record ShippingFile(DataSource Source, string BusinessDays, ImmutableArray<Warehouse> Warehouses, ImmutableArray<ShippingRule> Rules);

public sealed record PaymentsFile(DataSource Source, string Currency, ImmutableArray<InstallmentRule> InstallmentRules);

public sealed record ReturnsFile(DataSource Source, ImmutableArray<ReturnRule> Rules);

public static class StoreFileNames
{
    public const string Catalog = "catalog.csv";
    public const string PriceList = "price-list.json";
    public const string Inventory = "inventory.json";
    public const string Shipping = "shipping.json";
    public const string Payments = "payments.json";
    public const string Returns = "returns.json";
    public const string GroundTruth = "ground-truth.json";
    public const string Intents = "intents.json";
    public const string Metrics = "metrics.json";
}

/// <summary>
/// catalog.csv: one row per SKU. Product-level fields repeat on every variant row, like a real product feed.
/// Price, stock status and delivery promise columns are storefront projections, not authoritative values.
/// </summary>
public static class CatalogCsv
{
    private static readonly string[] AttributeColumns =
    [
        AttributeKeys.Brand, AttributeKeys.ProductType, AttributeKeys.Model, AttributeKeys.Material,
        AttributeKeys.WaterResistance, AttributeKeys.LaptopSizeIn, AttributeKeys.CapacityLiters,
        AttributeKeys.ShoeSize, AttributeKeys.Color, AttributeKeys.TargetGroup,
        AttributeKeys.Connectivity, AttributeKeys.BatteryHours, AttributeKeys.Microphone, AttributeKeys.NoiseCancellation,
        AttributeKeys.WarrantyMonths, AttributeKeys.PowerSource,
    ];

    private static readonly string[] Header =
    [
        "productId", "sku", "category", "title", "listedPrice", "compareAtPrice", "stockStatus", "deliveryPromiseIstanbul",
        .. AttributeColumns,
        "description",
    ];

    public static string Write(StoreSnapshot store)
    {
        var rows = store.Variants
            .OrderBy(v => v.Sku, StringComparer.Ordinal)
            .Select(variant =>
            {
                var product = store.GetProduct(variant.ProductId);
                var listing = store.GetListing(variant.Sku);
                IReadOnlyList<string> row =
                [
                    product.ProductId,
                    variant.Sku,
                    product.Category.ToString(),
                    product.Title,
                    InvariantFormat.Money(listing.ListedPrice),
                    listing.CompareAtPrice is { } compareAt ? InvariantFormat.Money(compareAt) : string.Empty,
                    listing.StockStatus.ToCatalogText(),
                    listing.DeliveryPromiseIstanbul is { } promise ? InvariantFormat.Date(promise) : string.Empty,
                    .. AttributeColumns.Select(key => variant.Attributes.Find(key) ?? product.Attributes.Find(key) ?? string.Empty),
                    product.Description,
                ];
                return row;
            });
        return Csv.Write(Header, rows);
    }

    public static (ImmutableArray<Product> Products, ImmutableArray<ProductVariant> Variants, ImmutableArray<CatalogListing> Listings) Read(string text)
    {
        var rows = Csv.Read(text);
        var products = rows
            .GroupBy(r => r["productId"], StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var category = Enum.Parse<Category>(first["category"]);
                var variantKeys = CategoryRequirements.VariantLevelKeys(category).ToHashSet(StringComparer.Ordinal);
                var attributes = AttributeColumns
                    .Where(key => !variantKeys.Contains(key) && first[key].Length > 0)
                    .Select(key => KeyValuePair.Create(key, first[key]));
                return new Product(group.Key, category, first["title"], first["description"], Attributes.Create(attributes));
            })
            .OrderBy(p => p.ProductId, StringComparer.Ordinal)
            .ToImmutableArray();

        var variants = rows
            .Select(r =>
            {
                var variantKeys = CategoryRequirements.VariantLevelKeys(Enum.Parse<Category>(r["category"]));
                var attributes = variantKeys.Where(key => r[key].Length > 0).Select(key => KeyValuePair.Create(key, r[key]));
                return new ProductVariant(r["sku"], r["productId"], Attributes.Create(attributes));
            })
            .OrderBy(v => v.Sku, StringComparer.Ordinal)
            .ToImmutableArray();

        var listings = rows
            .Select(r => new CatalogListing(
                r["sku"],
                InvariantFormat.ParseDecimal(r["listedPrice"]),
                r["compareAtPrice"].Length > 0 ? InvariantFormat.ParseDecimal(r["compareAtPrice"]) : null,
                StockStatusText.Parse(r["stockStatus"]),
                r["deliveryPromiseIstanbul"].Length > 0 ? InvariantFormat.ParseDate(r["deliveryPromiseIstanbul"]) : null))
            .OrderBy(l => l.Sku, StringComparer.Ordinal)
            .ToImmutableArray();

        return (products, variants, listings);
    }
}
