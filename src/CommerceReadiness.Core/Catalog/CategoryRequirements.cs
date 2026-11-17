using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Catalog;

public static class AttributeKeys
{
    public const string Brand = "brand";
    public const string Price = "price";
    public const string Availability = "availability";
    public const string Material = "material";
    public const string WaterResistance = "waterResistance";
    public const string LaptopSizeIn = "laptopSizeIn";
    public const string CapacityLiters = "capacityLiters";
    public const string ShoeSize = "shoeSize";
    public const string Color = "color";
    public const string TargetGroup = "genderOrTargetGroup";
    public const string Connectivity = "connectivity";
    public const string BatteryHours = "batteryHours";
    public const string Microphone = "microphone";
    public const string NoiseCancellation = "noiseCancellation";
    public const string Model = "model";
    public const string WarrantyMonths = "warrantyMonths";
    public const string PowerSource = "powerSource";

    /// <summary>Not a required attribute; used to tell small-electronics product types apart.</summary>
    public const string ProductType = "productType";
}

/// <summary>Where a required attribute lives: on the product, on the variant (SKU), or on the storefront listing.</summary>
public enum AttributeLevel
{
    Product,
    Variant,
    Listing,
}

public enum AttributeKind
{
    Text,
    Number,
    Boolean,
    Enum,
    Money,
    Availability,
}

public sealed record AttributeRequirement(string Key, AttributeLevel Level, AttributeKind Kind);

/// <summary>
/// Required attributes per category. These drive Catalog Completeness and Semantic Coverage:
/// every SKU is evaluated against every requirement of its category.
/// </summary>
public static class CategoryRequirements
{
    private static readonly AttributeRequirement Brand = new(AttributeKeys.Brand, AttributeLevel.Product, AttributeKind.Text);
    private static readonly AttributeRequirement Price = new(AttributeKeys.Price, AttributeLevel.Listing, AttributeKind.Money);
    private static readonly AttributeRequirement Availability = new(AttributeKeys.Availability, AttributeLevel.Listing, AttributeKind.Availability);

    private static readonly ImmutableArray<AttributeRequirement> Backpacks =
    [
        Brand, Price, Availability,
        new(AttributeKeys.Material, AttributeLevel.Product, AttributeKind.Text),
        new(AttributeKeys.WaterResistance, AttributeLevel.Product, AttributeKind.Enum),
        new(AttributeKeys.LaptopSizeIn, AttributeLevel.Product, AttributeKind.Number),
        new(AttributeKeys.CapacityLiters, AttributeLevel.Product, AttributeKind.Number),
    ];

    private static readonly ImmutableArray<AttributeRequirement> Sneakers =
    [
        Brand, Price, Availability,
        new(AttributeKeys.ShoeSize, AttributeLevel.Variant, AttributeKind.Number),
        new(AttributeKeys.Material, AttributeLevel.Product, AttributeKind.Text),
        new(AttributeKeys.Color, AttributeLevel.Variant, AttributeKind.Text),
        new(AttributeKeys.TargetGroup, AttributeLevel.Product, AttributeKind.Text),
    ];

    private static readonly ImmutableArray<AttributeRequirement> Headphones =
    [
        Brand, Price, Availability,
        new(AttributeKeys.Connectivity, AttributeLevel.Product, AttributeKind.Text),
        new(AttributeKeys.BatteryHours, AttributeLevel.Product, AttributeKind.Number),
        new(AttributeKeys.Microphone, AttributeLevel.Product, AttributeKind.Boolean),
        new(AttributeKeys.NoiseCancellation, AttributeLevel.Product, AttributeKind.Boolean),
    ];

    private static readonly ImmutableArray<AttributeRequirement> SmallElectronics =
    [
        Brand, Price, Availability,
        new(AttributeKeys.Model, AttributeLevel.Product, AttributeKind.Text),
        new(AttributeKeys.WarrantyMonths, AttributeLevel.Product, AttributeKind.Number),
        new(AttributeKeys.PowerSource, AttributeLevel.Product, AttributeKind.Text),
    ];

    public static ImmutableArray<AttributeRequirement> For(Category category) => category switch
    {
        Category.Backpacks => Backpacks,
        Category.Sneakers => Sneakers,
        Category.Headphones => Headphones,
        Category.SmallElectronics => SmallElectronics,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static IEnumerable<string> VariantLevelKeys(Category category) =>
        For(category).Where(r => r.Level == AttributeLevel.Variant).Select(r => r.Key);
}

public static class StructuredValues
{
    /// <summary>The structured value of a required attribute for a SKU, or null when it is missing.</summary>
    public static string? Get(StoreSnapshot store, string sku, AttributeRequirement requirement)
    {
        switch (requirement.Level)
        {
            case AttributeLevel.Listing:
                var listing = store.GetListing(sku);
                return requirement.Key == AttributeKeys.Price
                    ? InvariantFormat.Money(listing.ListedPrice)
                    : listing.StockStatus.ToCatalogText();
            case AttributeLevel.Variant:
                return Normalize(store.GetVariant(sku).Attributes.Find(requirement.Key));
            default:
                return Normalize(store.ProductOf(sku).Attributes.Find(requirement.Key));
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
