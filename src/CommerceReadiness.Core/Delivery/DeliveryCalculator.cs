using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Time;

namespace CommerceReadiness.Core.Delivery;

/// <summary>
/// Deterministic delivery estimate:
/// ship date = today if it is a business day and the order is placed before the warehouse cutoff, otherwise
/// the next business day; delivery date = ship date + transit business days. Business days are Monday to
/// Friday; public holidays are not modeled.
/// </summary>
public static class DeliveryCalculator
{
    /// <summary>Delivery date for a SKU, or null when it has no authoritative stock or no shipping rule.</summary>
    public static DateOnly? EstimateDelivery(StoreSnapshot store, string sku, City destination, DateTimeOffset now)
    {
        var inventory = store.GetInventory(sku);
        return inventory.Available <= 0 ? null : EstimateFromWarehouse(store, inventory.WarehouseId, destination, now);
    }

    public static DateOnly? EstimateFromWarehouse(StoreSnapshot store, string warehouseId, City destination, DateTimeOffset now)
    {
        var warehouse = store.Warehouses.FirstOrDefault(w => w.WarehouseId == warehouseId);
        var rule = store.ShippingRules.FirstOrDefault(r => r.WarehouseId == warehouseId && r.Destination == destination);
        return warehouse is null || rule is null ? null : Estimate(warehouse, rule, now);
    }

    public static DateOnly Estimate(Warehouse warehouse, ShippingRule rule, DateTimeOffset now)
    {
        var local = now.ToOffset(SimulationContext.TurkeyOffset);
        var shipDate = DateOnly.FromDateTime(local.DateTime);
        if (!IsBusinessDay(shipDate) || local.Hour >= warehouse.CutoffHour)
        {
            shipDate = NextBusinessDay(shipDate);
        }

        return AddBusinessDays(shipDate, rule.TransitBusinessDays);
    }

    /// <summary>"Within N days" means the delivery date is on or before today + N calendar days.</summary>
    public static DateOnly Deadline(DateTimeOffset now, int days) => SimulationContext.TodayOf(now).AddDays(days);

    public static bool IsBusinessDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    public static DateOnly NextBusinessDay(DateOnly date)
    {
        do
        {
            date = date.AddDays(1);
        }
        while (!IsBusinessDay(date));

        return date;
    }

    public static DateOnly AddBusinessDays(DateOnly date, int businessDays)
    {
        for (var i = 0; i < businessDays; i++)
        {
            date = NextBusinessDay(date);
        }

        return date;
    }
}
