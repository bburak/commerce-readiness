using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.SourceOfTruth;
using CommerceReadiness.Core.Storage;
using CommerceReadiness.Core.Time;

namespace CommerceReadiness.DemoStore.Generation;

/// <summary>
/// Selects explicit non-defect cases from the clean store. They are left untouched in the published store
/// and reserved so no defect lands on them.
/// </summary>
public static class DecoyGenerator
{
    public static ImmutableArray<DecoyCase> Generate(
        StoreSnapshot clean,
        ImmutableArray<SemanticFact> cleanFacts,
        Reservations reservations,
        DeterministicRandom rng,
        SimulationContext context)
    {
        var decoys = new List<DecoyCase>();

        var outOfStock = clean.Listings.Where(l => l.StockStatus == StockStatus.OutOfStock).Select(l => l.Sku);
        foreach (var sku in reservations.TakeFree(rng.Fork("out-of-stock"), outOfStock, Reservations.Sku, 2, "out-of-stock decoys"))
        {
            decoys.Add(new DecoyCase(
                $"DCY-OOS-{sku}", "ConsistentOutOfStock", DefectType.InventoryConflict,
                clean.GetVariant(sku).ProductId, sku, null, PublishedFields.StockStatus,
                StockStatusText.OutOfStock, "available=0", DataSource.Inventory,
                "Catalog says OUT_OF_STOCK and inventory has 0 units: consistent, not an InventoryConflict."));
        }

        var repellentProducts = cleanFacts
            .Where(f => f.AttributeKey == AttributeKeys.WaterResistance && f.AssertedValue == nameof(WaterResistance.WaterRepellent))
            .Select(f => f.ProductId);
        foreach (var productId in reservations.TakeFree(rng.Fork("water-repellent"), repellentProducts, Reservations.Product, 1, "water-repellent decoy"))
        {
            var fact = cleanFacts.Single(f => f.ProductId == productId && f.AttributeKey == AttributeKeys.WaterResistance);
            decoys.Add(new DecoyCase(
                $"DCY-WRP-{productId}", "WaterRepellentDescriptionMatches", DefectType.MisleadingDescription,
                productId, SingleSku(clean, productId), null, AttributeKeys.WaterResistance,
                fact.Phrase, nameof(WaterResistance.WaterRepellent), DataSource.Catalog,
                "\"Su itici kumaş\" describes water repellency, which matches the structured value: not misleading."));
        }

        var discounted = clean.Listings.Where(l => l.CompareAtPrice is not null).Select(l => l.Sku);
        foreach (var sku in reservations.TakeFree(rng.Fork("compare-at"), discounted, Reservations.Sku, 1, "discount decoy"))
        {
            var listing = clean.GetListing(sku);
            decoys.Add(new DecoyCase(
                $"DCY-CMP-{sku}", "DiscountedPriceMatchesPricing", DefectType.PriceConflict,
                clean.GetVariant(sku).ProductId, sku, null, PublishedFields.ListedPrice,
                $"listedPrice={InvariantFormat.Money(listing.ListedPrice)}; compareAtPrice={InvariantFormat.Money(listing.CompareAtPrice!.Value)}",
                InvariantFormat.Money(clean.GetPrice(sku)), DataSource.Pricing,
                "The strike-through compareAtPrice differs, but the listed price equals the Pricing source: not a PriceConflict."));
        }

        var noAnc = clean.Products
            .Where(p => p.Category == Category.Headphones && p.Attributes.Find(AttributeKeys.NoiseCancellation) == "false")
            .Select(p => p.ProductId);
        foreach (var productId in reservations.TakeFree(rng.Fork("no-anc"), noAnc, Reservations.Product, 1, "explicit-false decoy"))
        {
            decoys.Add(new DecoyCase(
                $"DCY-ANC-{productId}", "ExplicitFalseIsPresent", DefectType.MissingRequiredAttribute,
                productId, SingleSku(clean, productId), null, AttributeKeys.NoiseCancellation,
                "false", "false", DataSource.Catalog,
                "noiseCancellation=false is an explicit structured value, not a missing attribute."));
        }

        var izmirInStock = clean.Inventory
            .Where(i => i.WarehouseId == StoreConfiguration.IzmirWarehouse && i.Available > 0)
            .Select(i => i.Sku);
        foreach (var sku in reservations.TakeFree(rng.Fork("after-cutoff"), izmirInStock, Reservations.Sku, 1, "after-cutoff decoy"))
        {
            var promise = clean.GetListing(sku).DeliveryPromiseIstanbul!.Value;
            var calculated = DeliveryCalculator.EstimateDelivery(clean, sku, City.Istanbul, context.Now)!.Value;
            decoys.Add(new DecoyCase(
                $"DCY-CUT-{sku}", "AfterCutoffDeliveryPromise", DefectType.DeliveryConflict,
                clean.GetVariant(sku).ProductId, sku, null, PublishedFields.DeliveryPromiseIstanbul,
                InvariantFormat.Date(promise), InvariantFormat.Date(calculated), DataSource.Shipping,
                "The later promise is correct: the Izmir warehouse cutoff (09:00) has passed at 10:00, so it ships the next business day."));
        }

        var conditionalCell = ReturnRule.CellIdFor(Category.Headphones, ReturnQuestion.OpenedReturn);
        reservations.Reserve(Reservations.Policy(conditionalCell));
        var conditionalRule = clean.FindReturnRule(Category.Headphones, ReturnQuestion.OpenedReturn)!;
        decoys.Add(new DecoyCase(
            $"DCY-CND-{conditionalCell.Replace(':', '-')}", "ExplicitConditionalReturn", DefectType.AmbiguousReturnRule,
            null, null, conditionalCell, PublishedFields.ReturnAnswer,
            $"{conditionalRule.Answer}: {conditionalRule.ConditionTr}", conditionalRule.Answer.ToString(), DataSource.ReturnPolicy,
            "A Conditional answer with an explicit condition is a clear policy, not an ambiguous one."));

        var multiColorProducts = clean.Products
            .Where(p => p.Category == Category.Sneakers
                        && clean.VariantsOf(p.ProductId).Select(v => v.Attributes.Find(AttributeKeys.Color)).Distinct().Count() > 1
                        && clean.VariantsOf(p.ProductId).All(v => reservations.IsFree(Reservations.Sku(v.Sku))))
            .Select(p => p.ProductId)
            .ToList();
        var variantProductId = rng.Fork("variant-price").Pick(multiColorProducts);
        var variants = clean.VariantsOf(variantProductId).ToList();
        var first = variants[0];
        var sibling = variants.First(v => v.Attributes.Find(AttributeKeys.Color) != first.Attributes.Find(AttributeKeys.Color));
        reservations.Reserve(Reservations.Sku(first.Sku));
        reservations.Reserve(Reservations.Sku(sibling.Sku));
        decoys.Add(new DecoyCase(
            $"DCY-VPR-{first.Sku}", "SameProductDifferentColorSamePrice", DefectType.PriceConflict,
            variantProductId, first.Sku, null, PublishedFields.ListedPrice,
            InvariantFormat.Money(clean.GetListing(first.Sku).ListedPrice), InvariantFormat.Money(clean.GetPrice(first.Sku)), DataSource.Pricing,
            $"Shares its price with sibling {sibling.Sku} in another color; both match Pricing, which is expected for variants."));

        return [.. decoys.OrderBy(d => d.DecoyId, StringComparer.Ordinal)];
    }

    internal static string? SingleSku(StoreSnapshot store, string productId)
    {
        var skus = store.VariantsOf(productId).Select(v => v.Sku).ToList();
        return skus.Count == 1 ? skus[0] : null;
    }
}
