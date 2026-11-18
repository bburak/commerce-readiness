using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.SourceOfTruth;
using CommerceReadiness.Core.Storage;

namespace CommerceReadiness.Core.Analysis.Rules;

/// <summary>
/// A published Istanbul delivery promise differs from the deterministic Shipping calculation (warehouse cutoff,
/// transit business days, fixed as-of time). Only SKUs with authoritative stock are checked: a promise on a SKU with
/// 0 units is part of an InventoryConflict, and a missing promise is not a delivery defect.
/// </summary>
public static class DeliveryConflictRule
{
    public static IEnumerable<Finding> Evaluate(StoreSnapshot published, DateTimeOffset asOf)
    {
        foreach (var listing in published.Listings.OrderBy(l => l.Sku, StringComparer.Ordinal))
        {
            var inventory = published.Inventory.FirstOrDefault(i => i.Sku == listing.Sku);
            if (listing.DeliveryPromiseIstanbul is not { } promise || inventory is null || inventory.Available <= 0)
            {
                continue;
            }

            var warehouse = published.Warehouses.FirstOrDefault(w => w.WarehouseId == inventory.WarehouseId);
            var rule = published.ShippingRules.FirstOrDefault(r => r.WarehouseId == inventory.WarehouseId && r.Destination == City.Istanbul);
            if (warehouse is null || rule is null)
            {
                continue;
            }

            var calculated = DeliveryCalculator.Estimate(warehouse, rule, asOf);
            if (promise == calculated)
            {
                continue;
            }

            yield return new Finding(
                $"FND-DLV-{listing.Sku}",
                FindingType.DeliveryConflict,
                FindingSeverities.For(FindingType.DeliveryConflict),
                published.GetVariant(listing.Sku).ProductId,
                listing.Sku,
                null,
                PublishedFields.DeliveryPromiseIstanbul,
                "Published Istanbul delivery promise conflicts with the shipping calculation.",
                [
                    new FindingEvidence(DataSource.Catalog, PublishedFields.DeliveryPromiseIstanbul, InvariantFormat.Date(promise)),
                    new FindingEvidence(DataSource.Inventory, "warehouseId", inventory.WarehouseId),
                    new FindingEvidence(DataSource.Shipping, "cutoffHour", InvariantFormat.Number(warehouse.CutoffHour)),
                    new FindingEvidence(DataSource.Shipping, "transitBusinessDays", InvariantFormat.Number(rule.TransitBusinessDays)),
                    new FindingEvidence(DataSource.Shipping, "calculatedDelivery", InvariantFormat.Date(calculated)),
                ],
                $"Set the Istanbul delivery promise to {InvariantFormat.Date(calculated)}.");
        }
    }
}
