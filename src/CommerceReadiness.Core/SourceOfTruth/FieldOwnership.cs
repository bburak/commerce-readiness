namespace CommerceReadiness.Core.SourceOfTruth;

public enum DataSource
{
    Catalog,
    Pricing,
    Inventory,
    Shipping,
    ReturnPolicy,
    Payments,
}

/// <param name="Field">Logical field, e.g. "sku.stock".</param>
/// <param name="Owner">The only source allowed to decide this field.</param>
/// <param name="AuthoritativeFile">File holding the authoritative value in each store folder.</param>
/// <param name="StorefrontProjection">Non-authoritative copy shown on the storefront, if any.</param>
public sealed record FieldOwner(string Field, DataSource Owner, string AuthoritativeFile, string? StorefrontProjection);

/// <summary>
/// Explicit source-of-truth map. Each source is authoritative only for its own domain; no snapshot as a
/// whole (including the published one) is authoritative. When a storefront projection disagrees with its
/// owner, the owner wins.
/// </summary>
public static class FieldOwnership
{
    public static ImmutableArray<FieldOwner> All { get; } =
    [
        new("product.identity", DataSource.Catalog, "catalog.csv", null),
        new("product.title", DataSource.Catalog, "catalog.csv", null),
        new("product.description", DataSource.Catalog, "catalog.csv", null),
        new("product.attributes", DataSource.Catalog, "catalog.csv", null),
        new("sku.price", DataSource.Pricing, "price-list.json", "catalog.csv:listedPrice"),
        new("sku.stock", DataSource.Inventory, "inventory.json", "catalog.csv:stockStatus"),
        new("sku.delivery", DataSource.Shipping, "shipping.json", "catalog.csv:deliveryPromiseIstanbul"),
        new("policy.returns", DataSource.ReturnPolicy, "returns.json", null),
        new("payment.installments", DataSource.Payments, "payments.json", null),
    ];

    public static DataSource OwnerOf(string field) => All.First(f => f.Field == field).Owner;
}
