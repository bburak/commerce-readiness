using System.Text;
using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.SourceOfTruth;

namespace CommerceReadiness.Core.Storage;

public sealed record PriceListFile(DataSource Source, string Currency, ImmutableArray<PriceEntry> Prices);

public sealed record InventoryFile(DataSource Source, ImmutableArray<InventoryRecord> Records);

public sealed record ShippingFile(DataSource Source, string BusinessDays, ImmutableArray<Warehouse> Warehouses, ImmutableArray<ShippingRule> Rules);

public sealed record PaymentsFile(DataSource Source, string Currency, ImmutableArray<InstallmentRule> InstallmentRules);

public sealed record ReturnsFile(DataSource Source, ImmutableArray<ReturnRule> Rules);

/// <summary>
/// store.json: identifies a store folder and the instant ("as of") its time-sensitive data refers to.
/// Lets a reader of one store folder work without any other folder of the dataset.
/// </summary>
public sealed record StoreManifest(int SchemaVersion, string StoreVersion, StoreKind Kind, DateTimeOffset AsOf)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record LoadedStore(StoreManifest Manifest, StoreSnapshot Store);

/// <summary>Folder names of a generated dataset.</summary>
public static class DatasetLayout
{
    public const string CleanFolder = "clean";
    public const string PublishedFolder = "published";
    public const string AnalysisFolder = "analysis";
}

public static class StoreFileNames
{
    public const string Manifest = "store.json";
    public const string Catalog = "catalog.csv";
    public const string PriceList = "price-list.json";
    public const string Inventory = "inventory.json";
    public const string Shipping = "shipping.json";
    public const string Payments = "payments.json";
    public const string Returns = "returns.json";
}

/// <summary>
/// Names of published storefront projection fields (catalog.csv columns and return-policy cells). These are storage
/// names, not domain concepts; generator, analyzer and evaluator use them to refer to the same observable field.
/// </summary>
public static class PublishedFields
{
    public const string StockStatus = "stockStatus";
    public const string ListedPrice = "listedPrice";
    public const string DeliveryPromiseIstanbul = "deliveryPromiseIstanbul";
    public const string ReturnAnswer = "answer";
}

public static class TextFiles
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void Write(string path, string text) => File.WriteAllText(path, text, Utf8NoBom);
}

/// <summary>Reads and writes one store folder (clean, published or repaired).</summary>
public static class StoreFiles
{
    public static void Write(StoreSnapshot store, DateTimeOffset asOf, string directory)
    {
        Directory.CreateDirectory(directory);
        var manifest = new StoreManifest(StoreManifest.CurrentSchemaVersion, store.StoreVersion, store.Kind, asOf);
        TextFiles.Write(Path.Combine(directory, StoreFileNames.Manifest), JsonDefaults.Serialize(manifest));
        TextFiles.Write(Path.Combine(directory, StoreFileNames.Catalog), CatalogCsv.Write(store));
        TextFiles.Write(Path.Combine(directory, StoreFileNames.PriceList), JsonDefaults.Serialize(new PriceListFile(DataSource.Pricing, "TRY", store.Prices)));
        TextFiles.Write(Path.Combine(directory, StoreFileNames.Inventory), JsonDefaults.Serialize(new InventoryFile(DataSource.Inventory, store.Inventory)));
        TextFiles.Write(Path.Combine(directory, StoreFileNames.Shipping), JsonDefaults.Serialize(
            new ShippingFile(DataSource.Shipping, "Monday-Friday; public holidays not modeled", store.Warehouses, store.ShippingRules)));
        TextFiles.Write(Path.Combine(directory, StoreFileNames.Payments), JsonDefaults.Serialize(new PaymentsFile(DataSource.Payments, "TRY", store.InstallmentRules)));
        TextFiles.Write(Path.Combine(directory, StoreFileNames.Returns), JsonDefaults.Serialize(new ReturnsFile(DataSource.ReturnPolicy, store.ReturnRules)));
    }

    /// <exception cref="FileNotFoundException">The folder has no store.json manifest.</exception>
    /// <exception cref="NotSupportedException">The manifest declares a schema version this build cannot read.</exception>
    public static LoadedStore Read(string directory)
    {
        var manifestPath = Path.Combine(directory, StoreFileNames.Manifest);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Store manifest '{StoreFileNames.Manifest}' not found in '{directory}'.", manifestPath);
        }

        var manifest = JsonDefaults.Deserialize<StoreManifest>(File.ReadAllText(manifestPath));
        if (manifest.SchemaVersion != StoreManifest.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Unsupported store schema version {manifest.SchemaVersion}; this build supports {StoreManifest.CurrentSchemaVersion}.");
        }

        var (products, variants, listings) = CatalogCsv.Read(Read(directory, StoreFileNames.Catalog));
        var prices = JsonDefaults.Deserialize<PriceListFile>(Read(directory, StoreFileNames.PriceList));
        var inventory = JsonDefaults.Deserialize<InventoryFile>(Read(directory, StoreFileNames.Inventory));
        var shipping = JsonDefaults.Deserialize<ShippingFile>(Read(directory, StoreFileNames.Shipping));
        var payments = JsonDefaults.Deserialize<PaymentsFile>(Read(directory, StoreFileNames.Payments));
        var returns = JsonDefaults.Deserialize<ReturnsFile>(Read(directory, StoreFileNames.Returns));

        var store = new StoreSnapshot(
            manifest.Kind, manifest.StoreVersion, products, variants, listings,
            prices.Prices, inventory.Records, shipping.Warehouses, shipping.Rules, payments.InstallmentRules, returns.Rules);
        return new LoadedStore(manifest, store);
    }

    private static string Read(string directory, string file) => File.ReadAllText(Path.Combine(directory, file));
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
        "productId", "sku", "category", "title",
        PublishedFields.ListedPrice, "compareAtPrice", PublishedFields.StockStatus, PublishedFields.DeliveryPromiseIstanbul,
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
                InvariantFormat.ParseDecimal(r[PublishedFields.ListedPrice]),
                r["compareAtPrice"].Length > 0 ? InvariantFormat.ParseDecimal(r["compareAtPrice"]) : null,
                StockStatusText.Parse(r[PublishedFields.StockStatus]),
                r[PublishedFields.DeliveryPromiseIstanbul].Length > 0 ? InvariantFormat.ParseDate(r[PublishedFields.DeliveryPromiseIstanbul]) : null))
            .OrderBy(l => l.Sku, StringComparer.Ordinal)
            .ToImmutableArray();

        return (products, variants, listings);
    }
}
