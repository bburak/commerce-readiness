using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Payments;
using CommerceReadiness.Core.Shopping;

namespace CommerceReadiness.DemoStore.Generation;

/// <summary>
/// What a naive shopper would believe from the published storefront: listed price, catalog stock status,
/// the catalog's Istanbul delivery promise and published attributes. Used only to mark oracle-forbidden SKUs
/// that look valid in published data (traps); never used to compute expected results.
/// </summary>
public sealed class StorefrontFacts : ISkuFactSource
{
    private readonly StoreSnapshot _published;
    private readonly DateTimeOffset _now;

    public StorefrontFacts(StoreSnapshot published, DateTimeOffset now)
    {
        _published = published;
        _now = now;
        AllSkus = [.. published.Variants.Select(v => v.Sku).Order(StringComparer.Ordinal)];
    }

    public ImmutableArray<string> AllSkus { get; }

    public Category CategoryOf(string sku) => _published.ProductOf(sku).Category;

    public decimal? PriceOf(string sku) => _published.GetListing(sku).ListedPrice;

    public bool? IsInStock(string sku) => _published.GetListing(sku).StockStatus == StockStatus.InStock;

    public DateOnly? DeliveryDate(string sku, City destination)
    {
        var listing = _published.GetListing(sku);
        if (listing.StockStatus != StockStatus.InStock)
        {
            return null;
        }

        return destination == City.Istanbul
            ? listing.DeliveryPromiseIstanbul
            : DeliveryCalculator.EstimateFromWarehouse(_published, _published.GetInventory(sku).WarehouseId, destination, _now);
    }

    public string? AttributeOf(string sku, string key) =>
        _published.GetVariant(sku).Attributes.Find(key) ?? _published.ProductOf(sku).Attributes.Find(key);

    public ReturnRule? ReturnRuleFor(Category category, ReturnQuestion question) => _published.FindReturnRule(category, question);

    public int MaxInstallments(string sku) =>
        InstallmentCalculator.MaxInstallments(_published.InstallmentRules, CategoryOf(sku), _published.GetListing(sku).ListedPrice);
}
