using System.Security.Cryptography;
using System.Text;
using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Retrieval;

/// <summary>
/// Builds the deterministic semantic document of every published SKU. The text describes the product (title,
/// category, brand, type, description, stable attributes, variant size and color) and deliberately excludes volatile
/// commerce facts: price, stock, delivery promises, installments and return eligibility.
/// </summary>
public static class SearchDocumentBuilder
{
    public static ImmutableArray<SearchDocument> Build(StoreSnapshot published)
    {
        if (published.Kind == StoreKind.Clean)
        {
            throw new ArgumentException("Search documents are built from observable stores only; the clean store is hidden truth.", nameof(published));
        }

        return [.. published.Variants
            .OrderBy(v => v.Sku, StringComparer.Ordinal)
            .Select(v => BuildDocument(published, v))];
    }

    public static string SemanticText(Product product, ProductVariant variant)
    {
        var lines = new List<string> { $"Ürün: {product.Title}", $"Kategori: {CategoryName(product.Category)}" };
        AddLine(lines, "Marka", product.Attributes.Find(AttributeKeys.Brand));
        AddLine(lines, "Ürün tipi", product.Attributes.Find(AttributeKeys.ProductType));
        AddLine(lines, "Açıklama", product.Description);

        var features = CategoryRequirements.DescriptionClaimKeys(product.Category)
            .Select(key => Feature(key, product.Attributes.Find(key)))
            .OfType<string>()
            .ToList();
        if (features.Count > 0)
        {
            lines.Add($"Özellikler: {string.Join("; ", features)}");
        }

        var size = variant.Attributes.Find(AttributeKeys.ShoeSize);
        var color = variant.Attributes.Find(AttributeKeys.Color);
        var variantParts = new[] { size is null ? null : $"{size} numara", color }.OfType<string>().ToList();
        if (variantParts.Count > 0)
        {
            lines.Add($"Varyant: {string.Join(", ", variantParts)}");
        }

        return string.Join("\n", lines);
    }

    private static SearchDocument BuildDocument(StoreSnapshot published, ProductVariant variant)
    {
        var product = published.GetProduct(variant.ProductId);
        var text = SemanticText(product, variant);
        return new SearchDocument(
            published.StoreVersion,
            variant.Sku,
            product.ProductId,
            product.Category,
            product.Title,
            product.Attributes.Find(AttributeKeys.Brand),
            text,
            ContentHash.Of(text),
            published.GetPrice(variant.Sku),
            published.GetInventory(variant.Sku).Available,
            Integer(variant.Attributes.Find(AttributeKeys.ShoeSize)),
            variant.Attributes.Find(AttributeKeys.Color),
            Number(product.Attributes.Find(AttributeKeys.LaptopSizeIn)),
            Number(product.Attributes.Find(AttributeKeys.CapacityLiters)),
            Integer(product.Attributes.Find(AttributeKeys.BatteryHours)),
            Integer(product.Attributes.Find(AttributeKeys.WarrantyMonths)));
    }

    private static void AddLine(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {value}");
        }
    }

    private static string? Feature(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return key switch
        {
            AttributeKeys.Material => $"Malzeme: {value}",
            AttributeKeys.WaterResistance => $"Su dayanımı: {WaterResistanceText(value)}",
            AttributeKeys.LaptopSizeIn => $"Laptop bölmesi: {value.Replace('.', ',')} inç",
            AttributeKeys.CapacityLiters => $"Hacim: {value} litre",
            AttributeKeys.TargetGroup => $"Hedef grup: {value}",
            AttributeKeys.Connectivity => $"Bağlantı: {value}",
            AttributeKeys.BatteryHours => $"Pil ömrü: {value} saat",
            AttributeKeys.Microphone => $"Mikrofon: {YesNo(value)}",
            AttributeKeys.NoiseCancellation => $"Aktif gürültü engelleme: {YesNo(value)}",
            AttributeKeys.Model => $"Model: {value}",
            AttributeKeys.WarrantyMonths => $"Garanti: {value} ay",
            AttributeKeys.PowerSource => $"Güç kaynağı: {value}",
            _ => $"{key}: {value}",
        };
    }

    private static string CategoryName(Category category) => category switch
    {
        Category.Backpacks => "Sırt çantası",
        Category.Sneakers => "Spor ayakkabı",
        Category.Headphones => "Kulaklık",
        Category.SmallElectronics => "Küçük elektronik",
        _ => category.ToString(),
    };

    private static string WaterResistanceText(string value) => value switch
    {
        nameof(WaterResistance.None) => "su geçirmez değil",
        nameof(WaterResistance.WaterRepellent) => "su itici",
        nameof(WaterResistance.Waterproof) => "tamamen su geçirmez",
        _ => value,
    };

    private static string YesNo(string value) => value == "true" ? "var" : value == "false" ? "yok" : value;

    private static decimal? Number(string? text) => InvariantFormat.TryParseDecimal(text, out var value) ? value : null;

    private static int? Integer(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}

public static class ContentHash
{
    /// <summary>Bump when the semantic document template changes, so every document is re-embedded.</summary>
    public const string TemplateVersion = "semantic-doc-v1";

    public static string Of(string semanticText) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{TemplateVersion}\n{semanticText}")));
}
