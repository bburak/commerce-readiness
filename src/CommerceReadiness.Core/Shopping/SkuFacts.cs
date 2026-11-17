using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Payments;

namespace CommerceReadiness.Core.Shopping;

/// <summary>The facts constraint evaluation needs about a SKU, independent of where they come from.</summary>
public interface ISkuFactSource
{
    ImmutableArray<string> AllSkus { get; }

    Category CategoryOf(string sku);

    decimal? PriceOf(string sku);

    bool? IsInStock(string sku);

    DateOnly? DeliveryDate(string sku, City destination);

    string? AttributeOf(string sku, string key);

    ReturnRule? ReturnRuleFor(Category category, ReturnQuestion question);

    int MaxInstallments(string sku);
}

/// <summary>
/// Facts read only from the authoritative owner of each field in the clean store:
/// Pricing for price, Inventory for stock, Shipping rules for delivery, Catalog for attributes,
/// ReturnPolicy for returns and Payments for installments.
/// </summary>
public sealed class AuthoritativeFacts : ISkuFactSource
{
    private readonly StoreSnapshot _clean;
    private readonly DateTimeOffset _now;

    public AuthoritativeFacts(StoreSnapshot clean, DateTimeOffset now)
    {
        if (clean.Kind != StoreKind.Clean)
        {
            throw new ArgumentException("Authoritative facts can only be read from the clean store.", nameof(clean));
        }

        _clean = clean;
        _now = now;
        AllSkus = [.. clean.Variants.Select(v => v.Sku).Order(StringComparer.Ordinal)];
    }

    public ImmutableArray<string> AllSkus { get; }

    public Category CategoryOf(string sku) => _clean.ProductOf(sku).Category;

    public decimal? PriceOf(string sku) => _clean.GetPrice(sku);

    public bool? IsInStock(string sku) => _clean.GetInventory(sku).Available > 0;

    public DateOnly? DeliveryDate(string sku, City destination) =>
        DeliveryCalculator.EstimateDelivery(_clean, sku, destination, _now);

    public string? AttributeOf(string sku, string key) =>
        _clean.GetVariant(sku).Attributes.Find(key) ?? _clean.ProductOf(sku).Attributes.Find(key);

    public ReturnRule? ReturnRuleFor(Category category, ReturnQuestion question) =>
        _clean.FindReturnRule(category, question);

    public int MaxInstallments(string sku) =>
        InstallmentCalculator.MaxInstallments(_clean.InstallmentRules, CategoryOf(sku), _clean.GetPrice(sku));

    public string WarehouseOf(string sku) => _clean.GetInventory(sku).WarehouseId;
}
