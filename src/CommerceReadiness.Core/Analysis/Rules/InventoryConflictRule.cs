using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.SourceOfTruth;
using CommerceReadiness.Core.Storage;

namespace CommerceReadiness.Core.Analysis.Rules;

/// <summary>The catalog stock projection disagrees with the authoritative Inventory quantity.</summary>
public static class InventoryConflictRule
{
    public static IEnumerable<Finding> Evaluate(StoreSnapshot published)
    {
        foreach (var listing in published.Listings.OrderBy(l => l.Sku, StringComparer.Ordinal))
        {
            var inventory = published.Inventory.FirstOrDefault(i => i.Sku == listing.Sku);
            if (inventory is null)
            {
                continue;
            }

            var catalogInStock = listing.StockStatus == StockStatus.InStock;
            if (catalogInStock == (inventory.Available > 0))
            {
                continue;
            }

            var available = InvariantFormat.Number(inventory.Available);
            yield return new Finding(
                $"FND-INV-{listing.Sku}",
                FindingType.InventoryConflict,
                FindingSeverities.For(FindingType.InventoryConflict),
                published.GetVariant(listing.Sku).ProductId,
                listing.Sku,
                null,
                PublishedFields.StockStatus,
                "Published stock status conflicts with inventory.",
                [
                    new FindingEvidence(DataSource.Catalog, PublishedFields.StockStatus, listing.StockStatus.ToCatalogText()),
                    new FindingEvidence(DataSource.Inventory, "available", available),
                    new FindingEvidence(DataSource.Inventory, "warehouseId", inventory.WarehouseId),
                ],
                catalogInStock
                    ? "Update the published stock status to OUT_OF_STOCK and remove any delivery promise."
                    : $"Update the published stock status to IN_STOCK ({available} units in inventory) and publish a delivery promise.");
        }
    }
}
