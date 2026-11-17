using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Time;
using static CommerceReadiness.DemoStore.Generation.CatalogTemplates;

namespace CommerceReadiness.DemoStore.Generation;

public sealed record CleanStoreResult(StoreSnapshot Store, ImmutableArray<SemanticFact> Facts);

/// <summary>
/// Builds the clean, internally consistent, authoritative store. Key distributions (water resistance, stock-outs,
/// warehouses, noise cancellation...) use shuffled quotas so every seed contains the cases later stages rely on.
/// </summary>
public static class CleanStoreGenerator
{
    public const int BackpackCount = 25;
    public const int HeadphoneCount = 20;
    public const int SmallElectronicsCount = 25;
    public const int CompareAtPriceCount = 8;

    private static readonly int[] SneakerVariantCounts = [3, 4, 4, 4, 4, 4, 4, 3];

    public static int SneakerSkuCount => SneakerVariantCounts.Sum();

    public static CleanStoreResult Generate(DeterministicRandom rng, SimulationContext context, string storeVersion)
    {
        var draft = new StoreDraft();
        GenerateBackpacks(rng.Fork("backpacks"), draft);
        GenerateSneakers(rng.Fork("sneakers"), draft);
        GenerateHeadphones(rng.Fork("headphones"), draft);
        GenerateSmallElectronics(rng.Fork("small-electronics"), draft);

        var store = new StoreSnapshot(
            StoreKind.Clean,
            storeVersion,
            [.. draft.Products.OrderBy(p => p.ProductId, StringComparer.Ordinal)],
            [.. draft.Variants.OrderBy(v => v.Sku, StringComparer.Ordinal)],
            [],
            [.. draft.Prices.OrderBy(p => p.Sku, StringComparer.Ordinal)],
            [.. draft.Inventory.OrderBy(i => i.Sku, StringComparer.Ordinal)],
            StoreConfiguration.Warehouses,
            StoreConfiguration.ShippingRules,
            StoreConfiguration.InstallmentRules,
            StoreConfiguration.ReturnRules);

        var compareAt = PickCompareAtPrices(rng.Fork("compare-at"), store);
        var listings = store.Variants
            .Select(v => BuildListing(store, v.Sku, compareAt.GetValueOrDefault(v.Sku), context))
            .ToImmutableArray();

        var facts = draft.Facts
            .OrderBy(f => f.ProductId, StringComparer.Ordinal)
            .ThenBy(f => f.AttributeKey, StringComparer.Ordinal)
            .ToImmutableArray();

        return new CleanStoreResult(store with { Listings = listings }, facts);
    }

    /// <summary>In the clean store every storefront projection equals its authoritative source.</summary>
    private static CatalogListing BuildListing(StoreSnapshot store, string sku, decimal? compareAtPrice, SimulationContext context)
    {
        var inStock = store.GetInventory(sku).Available > 0;
        return new CatalogListing(
            sku,
            store.GetPrice(sku),
            compareAtPrice,
            inStock ? StockStatus.InStock : StockStatus.OutOfStock,
            DeliveryCalculator.EstimateDelivery(store, sku, City.Istanbul, context.Now));
    }

    private static Dictionary<string, decimal?> PickCompareAtPrices(DeterministicRandom rng, StoreSnapshot store)
    {
        var singleSku = store.Variants
            .Where(v => store.GetProduct(v.ProductId).Category != Category.Sneakers)
            .Select(v => v.Sku)
            .ToList();
        return rng.Sample(singleSku, CompareAtPriceCount)
            .ToDictionary(sku => sku, sku => (decimal?)StoreConfiguration.RoundPrice(store.GetPrice(sku) * 1.25m), StringComparer.Ordinal);
    }

    private static void GenerateBackpacks(DeterministicRandom rng, StoreDraft draft)
    {
        var water = rng.Quota((WaterResistance.None, 8), (WaterResistance.WaterRepellent, 9), (WaterResistance.Waterproof, 8));
        var warehouses = rng.Quota((StoreConfiguration.IstanbulWarehouse, 18), (StoreConfiguration.IzmirWarehouse, 7));
        var stockOut = rng.Quota((true, 5), (false, 20));

        for (var i = 0; i < BackpackCount; i++)
        {
            var number = (i + 1).ToString("000", CultureInfo.InvariantCulture);
            var productId = $"P-BP-{number}";
            var sku = $"SKU-BP-{number}";
            var brand = rng.Pick(BackpackBrands);
            var model = rng.Pick(BackpackModels);
            var capacity = rng.Next(15, 41);
            var laptop = rng.Pick(LaptopSizes);
            var material = rng.Pick(BackpackMaterials);

            var waterBonus = water[i] switch
            {
                WaterResistance.Waterproof => 1100,
                WaterResistance.WaterRepellent => 400,
                _ => 0,
            };
            var laptopBonus = InvariantFormat.ParseDecimal(laptop) >= 15.6m ? 250 : 0;
            var price = StoreConfiguration.RoundPrice(700 + (capacity * 35) + waterBonus + laptopBonus + rng.Next(0, 600));

            var attributes = Attributes.Create(
                (AttributeKeys.Brand, brand),
                (AttributeKeys.Material, material),
                (AttributeKeys.WaterResistance, water[i].ToString()),
                (AttributeKeys.LaptopSizeIn, laptop),
                (AttributeKeys.CapacityLiters, InvariantFormat.Number(capacity)));

            string[] claimKeys =
            [
                AttributeKeys.WaterResistance,
                .. rng.Sample([AttributeKeys.Material, AttributeKeys.LaptopSizeIn, AttributeKeys.CapacityLiters], 2),
            ];

            draft.AddProduct(productId, Category.Backpacks, $"{brand} {model} {capacity}L Sırt Çantası", rng.Pick(BackpackIntros), attributes, claimKeys);
            draft.AddSku(sku, productId, Attributes.Empty, price, warehouses[i], stockOut[i] ? 0 : rng.Next(1, 41));
        }
    }

    private static void GenerateSneakers(DeterministicRandom rng, StoreDraft draft)
    {
        var models = rng.Sample(SneakerModels, SneakerVariantCounts.Length);
        var warehouses = rng.Quota((StoreConfiguration.IstanbulWarehouse, 6), (StoreConfiguration.IzmirWarehouse, 2));
        var stockOut = new Queue<bool>(rng.Quota((true, 6), (false, SneakerSkuCount - 6)));

        for (var p = 0; p < SneakerVariantCounts.Length; p++)
        {
            var number = (p + 1).ToString("000", CultureInfo.InvariantCulture);
            var productId = $"P-SN-{number}";
            var brand = rng.Pick(SneakerBrands);
            var material = rng.Pick(SneakerMaterials);
            var targetGroup = rng.Pick(TargetGroups);
            var price = StoreConfiguration.RoundPrice(1500 + rng.Next(0, 3500));

            var attributes = Attributes.Create(
                (AttributeKeys.Brand, brand),
                (AttributeKeys.Material, material),
                (AttributeKeys.TargetGroup, targetGroup));
            draft.AddProduct(
                productId,
                Category.Sneakers,
                $"{brand} {models[p]} Spor Ayakkabı",
                rng.Pick(SneakerIntros),
                attributes,
                [AttributeKeys.Material, AttributeKeys.TargetGroup]);

            var (minSize, maxSize) = SizeRange(targetGroup);
            var colors = rng.Sample(SneakerColors, 2);
            var combinations = colors
                .SelectMany(color => Enumerable.Range(minSize, maxSize - minSize + 1).Select(size => (size, color)))
                .ToList();
            var chosen = rng.Sample(combinations, SneakerVariantCounts[p])
                .OrderBy(c => c.color.Code, StringComparer.Ordinal)
                .ThenBy(c => c.size);

            foreach (var (size, color) in chosen)
            {
                var sku = $"SKU-SN-{number}-{InvariantFormat.Number(size)}-{color.Code}";
                var variantAttributes = Attributes.Create(
                    (AttributeKeys.ShoeSize, InvariantFormat.Number(size)),
                    (AttributeKeys.Color, color.Name));
                draft.AddSku(sku, productId, variantAttributes, price, warehouses[p], stockOut.Dequeue() ? 0 : rng.Next(1, 16));
            }
        }
    }

    private static void GenerateHeadphones(DeterministicRandom rng, StoreDraft draft)
    {
        var noiseCancellation = rng.Quota((true, 12), (false, 8));
        var microphone = rng.Quota((true, 15), (false, 5));
        var warehouses = rng.Quota((StoreConfiguration.IstanbulWarehouse, 14), (StoreConfiguration.IzmirWarehouse, 6));
        var stockOut = rng.Quota((true, 4), (false, 16));

        for (var i = 0; i < HeadphoneCount; i++)
        {
            var number = (i + 1).ToString("000", CultureInfo.InvariantCulture);
            var productId = $"P-HP-{number}";
            var brand = rng.Pick(HeadphoneBrands);
            var model = rng.Pick(HeadphoneModels);
            var form = rng.Pick(HeadphoneForms);
            var battery = rng.Next(8, 51);
            var connectivity = rng.Pick(Connectivity);
            var price = StoreConfiguration.RoundPrice(
                600 + (battery * 25) + (noiseCancellation[i] ? 1600 : 0) + (microphone[i] ? 250 : 0) + rng.Next(0, 700));

            var attributes = Attributes.Create(
                (AttributeKeys.Brand, brand),
                (AttributeKeys.Connectivity, connectivity),
                (AttributeKeys.BatteryHours, InvariantFormat.Number(battery)),
                (AttributeKeys.Microphone, InvariantFormat.Bool(microphone[i])),
                (AttributeKeys.NoiseCancellation, InvariantFormat.Bool(noiseCancellation[i])));

            var claimKeys = rng.Sample(
                [AttributeKeys.BatteryHours, AttributeKeys.NoiseCancellation, AttributeKeys.Microphone, AttributeKeys.Connectivity],
                rng.Next(2, 4));

            draft.AddProduct(productId, Category.Headphones, $"{brand} {model} Kablosuz {form} Kulaklık", rng.Pick(HeadphoneIntros), attributes, claimKeys);
            draft.AddSku($"SKU-HP-{number}", productId, Attributes.Empty, price, warehouses[i], stockOut[i] ? 0 : rng.Next(1, 31));
        }
    }

    private static void GenerateSmallElectronics(DeterministicRandom rng, StoreDraft draft)
    {
        var types = rng.Quota(SmallElectronicsTypes.Select(t => (t, SmallElectronicsCount / SmallElectronicsTypes.Length)).ToArray());
        var warehouses = rng.Quota((StoreConfiguration.IstanbulWarehouse, 17), (StoreConfiguration.IzmirWarehouse, 8));
        var stockOut = rng.Quota((true, 5), (false, 20));

        for (var i = 0; i < SmallElectronicsCount; i++)
        {
            var number = (i + 1).ToString("000", CultureInfo.InvariantCulture);
            var productId = $"P-SE-{number}";
            var type = types[i];
            var brand = rng.Pick(SmallElectronicsBrands);
            var model = $"{rng.Pick(ModelPrefixes)}-{InvariantFormat.Number(rng.Next(100, 1000))}";
            var warranty = rng.Pick(WarrantyOptions);
            var price = StoreConfiguration.RoundPrice(type.MinPrice + rng.Next(0, type.MaxPrice - type.MinPrice));

            var attributes = Attributes.Create(
                (AttributeKeys.Brand, brand),
                (AttributeKeys.ProductType, type.Name),
                (AttributeKeys.Model, model),
                (AttributeKeys.WarrantyMonths, InvariantFormat.Number(warranty)),
                (AttributeKeys.PowerSource, type.PowerSource(rng)));

            var claimKeys = rng.Sample([AttributeKeys.WarrantyMonths, AttributeKeys.PowerSource, AttributeKeys.Model], 2);

            draft.AddProduct(productId, Category.SmallElectronics, $"{brand} {model} {type.Name}", rng.Pick(SmallElectronicsIntros), attributes, claimKeys);
            draft.AddSku($"SKU-SE-{number}", productId, Attributes.Empty, price, warehouses[i], stockOut[i] ? 0 : rng.Next(1, 51));
        }
    }

    private sealed class StoreDraft
    {
        public List<Product> Products { get; } = [];
        public List<ProductVariant> Variants { get; } = [];
        public List<PriceEntry> Prices { get; } = [];
        public List<InventoryRecord> Inventory { get; } = [];
        public List<SemanticFact> Facts { get; } = [];

        public void AddProduct(
            string productId,
            Category category,
            string title,
            string intro,
            ImmutableSortedDictionary<string, string> attributes,
            IEnumerable<string> claimKeys)
        {
            var phrases = new List<string>();
            foreach (var key in claimKeys)
            {
                var value = attributes[key];
                var phrase = DescriptionPhrases.For(key, value);
                phrases.Add(phrase);
                Facts.Add(new SemanticFact(StoreKind.Clean, productId, key, value, phrase, SupportsAuthoritativeValue: true));
            }

            Products.Add(new Product(productId, category, title, string.Join(" ", phrases.Prepend(intro)), attributes));
        }

        public void AddSku(string sku, string productId, ImmutableSortedDictionary<string, string> attributes, decimal price, string warehouseId, int available)
        {
            Variants.Add(new ProductVariant(sku, productId, attributes));
            Prices.Add(new PriceEntry(sku, price));
            Inventory.Add(new InventoryRecord(sku, warehouseId, available));
        }
    }
}
