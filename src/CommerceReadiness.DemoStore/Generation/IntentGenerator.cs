using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Core.Time;
using static CommerceReadiness.DemoStore.Generation.TurkishText;

namespace CommerceReadiness.DemoStore.Generation;

/// <summary>
/// Produces Turkish shopper intents with hidden constraints. Expected results always come from the oracle.
/// Intents that should have valid products are anchored on a seeded in-stock target SKU, so they stay valid for
/// any seed; no-valid intents use combinations the generator can never produce; insufficient-information intents
/// omit something the oracle requires. Generation fails if the oracle outcome differs from the intended one.
/// </summary>
public static class IntentGenerator
{
    public static ImmutableArray<ShopperIntent> Generate(
        StoreSnapshot clean,
        StoreSnapshot published,
        ImmutableArray<InjectedDefect> defects,
        DeterministicRandom rng,
        SimulationContext context)
    {
        var anchors = new Anchors(new AuthoritativeFacts(clean, context.Now), rng);
        var storefront = new StorefrontFacts(published, context.Now);
        return [.. BuildDrafts(anchors, defects, published).Select(d => Finalize(d, clean, storefront, context))];
    }

    private static ShopperIntent Finalize(IntentDraft draft, StoreSnapshot clean, StorefrontFacts storefront, SimulationContext context)
    {
        var result = OracleShopper.Evaluate(clean, draft.Constraints, context);
        if (result.Outcome != draft.IntendedOutcome)
        {
            throw new InvalidOperationException($"Intent {draft.Id}: intended {draft.IntendedOutcome}, oracle returned {result.Outcome}.");
        }

        if (draft.AnchorSku is { } anchor && !result.ValidSkus.Contains(anchor))
        {
            throw new InvalidOperationException($"Intent {draft.Id}: anchor {anchor} is not a valid result.");
        }

        var forbidden = result.Forbidden
            .Select(f => new ForbiddenSku(
                f.Sku,
                f.Violations,
                ConstraintEvaluator.Evaluate(draft.Constraints, f.Sku, storefront, context.Now).IsEmpty))
            .ToImmutableArray();

        if (draft.TrapSku is { } trap && !forbidden.Any(f => f.Sku == trap && f.LooksValidInPublished))
        {
            throw new InvalidOperationException($"Intent {draft.Id}: planned trap {trap} does not look valid in published data.");
        }

        return new ShopperIntent(
            draft.Id,
            draft.Persona,
            draft.Text,
            draft.Constraints,
            new IntentExpectation(result.Outcome, result.ValidSkus, forbidden, result.MissingInformation));
    }

    private static IEnumerable<IntentDraft> BuildDrafts(Anchors a, ImmutableArray<InjectedDefect> defects, StoreSnapshot published)
    {
        const IntentOutcome valid = IntentOutcome.ValidProductsExist;
        const IntentOutcome none = IntentOutcome.NoValidProduct;
        const IntentOutcome insufficient = IntentOutcome.InsufficientInformation;

        {
            var sku = a.Pick("I01", Category.Backpacks, s =>
                a.Number(s, AttributeKeys.LaptopSizeIn) >= 15.6m
                && a.Water(s) >= WaterResistance.WaterRepellent
                && a.Warehouse(s) == StoreConfiguration.IstanbulWarehouse);
            var max = CeilTo(a.Price(sku), 500);
            var laptop = a.Attr(sku, AttributeKeys.LaptopSizeIn);
            yield return new("I01", Persona.UrgentDelivery, valid, sku,
                $"İstanbul'a iki gün içinde gelecek, {Money(max)} TL altında, {Decimal(laptop)} inç laptop sığan ve yağmurda kullanılabilecek bir sırt çantası arıyorum.",
                new ShopperConstraints
                {
                    Category = Category.Backpacks, MaxPriceTry = max, DeliverTo = City.Istanbul, DeliverWithinDays = 2,
                    MinimumLaptopSizeIn = InvariantFormat.ParseDecimal(laptop), MinimumWaterResistance = WaterResistance.WaterRepellent,
                });
        }

        {
            // Planted trap: a SKU the catalog still shows IN_STOCK although inventory has 0 units. The budget is set so
            // it fits both a cheap valid anchor and the trap's listed price.
            var trap = a.PickTrap("I02", defects
                .Where(d => d.Type == DefectType.InventoryConflict && d.PublishedValue == StockStatusText.InStock)
                .Select(d => d.Sku!)
                .Where(s => a.Facts.CategoryOf(s) != Category.Sneakers));
            var category = a.Facts.CategoryOf(trap);
            var sku = a.PickCheapest("I02", category, 6);
            var max = CeilTo(Math.Max(a.Price(sku), published.GetListing(trap).ListedPrice), 250);
            yield return new("I02", Persona.BudgetShopper, valid, sku,
                $"Bütçem kısıtlı: {Money(max)} TL'yi geçmeyen, stokta olan bir {BudgetNoun(category)} arıyorum.",
                new ShopperConstraints { Category = category, MaxPriceTry = max },
                trap);
        }

        {
            var sku = a.Pick("I03", Category.Sneakers, _ => true);
            var size = a.Attr(sku, AttributeKeys.ShoeSize);
            var color = a.Attr(sku, AttributeKeys.Color);
            var max = CeilTo(a.Price(sku), 500);
            yield return new("I03", Persona.SizeSensitive, valid, sku,
                $"{size} numara, {LowerColor(color)} renkte bir spor ayakkabı arıyorum. Bütçem {Money(max)} TL.",
                new ShopperConstraints { Category = Category.Sneakers, ShoeSize = int.Parse(size, CultureInfo.InvariantCulture), Color = color, MaxPriceTry = max });
        }

        {
            var sku = a.Pick("I04", Category.Sneakers, _ => true);
            var size = a.Attr(sku, AttributeKeys.ShoeSize);
            var group = a.Attr(sku, AttributeKeys.TargetGroup);
            yield return new("I04", Persona.SizeSensitive, valid, sku,
                $"Ankara'ya 3 gün içinde teslim edilecek, {size} numara bir {CatalogTemplates.LowerTargetGroup(group)} spor ayakkabısı istiyorum.",
                new ShopperConstraints
                {
                    Category = Category.Sneakers, ShoeSize = int.Parse(size, CultureInfo.InvariantCulture), TargetGroup = group,
                    DeliverTo = City.Ankara, DeliverWithinDays = 3,
                });
        }

        {
            var sku = a.Pick("I05", Category.Headphones, s =>
                a.Attr(s, AttributeKeys.NoiseCancellation) == "true" && a.Facts.MaxInstallments(s) >= 6);
            var max = CeilTo(a.Price(sku), 500);
            yield return new("I05", Persona.InstallmentSensitive, valid, sku,
                $"Kredi kartıma 6 taksitle alabileceğim, aktif gürültü engellemeli bir kulaklık arıyorum. En fazla {Money(max)} TL ödeyebilirim.",
                new ShopperConstraints { Category = Category.Headphones, NoiseCancellation = true, InstallmentCount = 6, MaxPriceTry = max });
        }

        {
            var sku = a.Pick("I06", Category.Headphones, s =>
                a.Attr(s, AttributeKeys.Microphone) == "true" && a.Number(s, AttributeKeys.BatteryHours) >= 20);
            var battery = (int)(Math.Floor(a.Number(sku, AttributeKeys.BatteryHours) / 10m) * 10m);
            var max = CeilTo(a.Price(sku), 1000);
            yield return new("I06", Persona.ComparisonShopper, valid, sku,
                $"Telefon görüşmeleri için mikrofonlu ve en az {InvariantFormat.Number(battery)} saat pil ömrü olan kulaklıkları karşılaştırmak istiyorum. Üst limitim {Money(max)} TL.",
                new ShopperConstraints { Category = Category.Headphones, Microphone = true, MinimumBatteryHours = battery, MaxPriceTry = max });
        }

        {
            var sku = a.Pick("I07", Category.Headphones, _ => true);
            var max = CeilTo(a.Price(sku), 500);
            yield return new("I07", Persona.ReturnSensitive, valid, sku,
                $"Açtıktan sonra beğenmezsem iade edebileceğim, {Money(max)} TL altında kablosuz bir kulaklık istiyorum.",
                new ShopperConstraints
                {
                    Category = Category.Headphones, MaxPriceTry = max,
                    ReturnRequirement = new ReturnRequirement(ReturnQuestion.OpenedReturn, AllowConditional: true),
                });
        }

        {
            var sku = a.Pick("I08", Category.SmallElectronics, s => a.Number(s, AttributeKeys.WarrantyMonths) >= 24);
            var type = CatalogTemplates.TypeByName(a.Attr(sku, AttributeKeys.ProductType));
            var max = CeilTo(a.Price(sku), 500);
            yield return new("I08", Persona.GiftShopper, valid, sku,
                $"Babama hediye olarak {Money(max)} TL altında, en az 24 ay garantili bir {type.Lower} arıyorum; İzmir'e 3 gün içinde ulaşması gerekiyor.",
                new ShopperConstraints
                {
                    Category = Category.SmallElectronics, ProductType = type.Name, MaxPriceTry = max, MinimumWarrantyMonths = 24,
                    DeliverTo = City.Izmir, DeliverWithinDays = 3,
                });
        }

        {
            var sku = a.Pick("I09", Category.SmallElectronics, s => a.Warehouse(s) == StoreConfiguration.IstanbulWarehouse);
            var type = CatalogTemplates.TypeByName(a.Attr(sku, AttributeKeys.ProductType));
            yield return new("I09", Persona.AvailabilitySensitive, valid, sku,
                $"Yarın İstanbul'da elimde olacak, stokta olan bir {type.Lower} lazım.",
                new ShopperConstraints { Category = Category.SmallElectronics, ProductType = type.Name, DeliverTo = City.Istanbul, DeliverWithinDays = 1 });
        }

        {
            var sku = a.Pick("I10", Category.SmallElectronics, s => a.Facts.MaxInstallments(s) >= 6);
            var type = CatalogTemplates.TypeByName(a.Attr(sku, AttributeKeys.ProductType));
            var max = CeilTo(a.Price(sku), 500);
            yield return new("I10", Persona.InstallmentSensitive, valid, sku,
                $"6 taksit seçeneği olan ve {Money(max)} TL'yi geçmeyen bir {type.Lower} almak istiyorum.",
                new ShopperConstraints { Category = Category.SmallElectronics, ProductType = type.Name, InstallmentCount = 6, MaxPriceTry = max });
        }

        {
            var sku = a.PickCheapest("I11", Category.Sneakers, 8);
            var size = a.Attr(sku, AttributeKeys.ShoeSize);
            var max = CeilTo(a.Price(sku), 250);
            yield return new("I11", Persona.BudgetShopper, valid, sku,
                $"{size} numara, {Money(max)} TL altındaki en uygun spor ayakkabı hangisi?",
                new ShopperConstraints { Category = Category.Sneakers, ShoeSize = int.Parse(size, CultureInfo.InvariantCulture), MaxPriceTry = max });
        }

        {
            var sku = a.Pick("I12", Category.Headphones, s => a.Warehouse(s) == StoreConfiguration.IstanbulWarehouse);
            var max = CeilTo(a.Price(sku), 500);
            yield return new("I12", Persona.UrgentDelivery, valid, sku,
                $"Yarın İstanbul'a teslim edilebilecek, {Money(max)} TL altında bir kulaklık arıyorum.",
                new ShopperConstraints { Category = Category.Headphones, MaxPriceTry = max, DeliverTo = City.Istanbul, DeliverWithinDays = 1 });
        }

        {
            var sku = a.Pick("I13", Category.Backpacks, s => a.Water(s) == WaterResistance.Waterproof);
            yield return new("I13", Persona.ReturnSensitive, valid, sku,
                "Tamamen su geçirmez bir sırt çantası istiyorum. İzmir'e 3 gün içinde gelmeli ve açtıktan sonra da en az 14 gün içinde iade edebilmeliyim.",
                new ShopperConstraints
                {
                    Category = Category.Backpacks, MinimumWaterResistance = WaterResistance.Waterproof,
                    DeliverTo = City.Izmir, DeliverWithinDays = 3,
                    ReturnRequirement = new ReturnRequirement(ReturnQuestion.OpenedReturn, AllowConditional: true, MinimumWindowDays: 14),
                });
        }

        {
            var sku = a.Pick("I14", Category.Backpacks, s => a.Number(s, AttributeKeys.LaptopSizeIn) >= 14);
            var capacity = Math.Floor(a.Number(sku, AttributeKeys.CapacityLiters) / 5m) * 5m;
            var laptop = a.Attr(sku, AttributeKeys.LaptopSizeIn);
            var max = CeilTo(a.Price(sku), 1000);
            yield return new("I14", Persona.ComparisonShopper, valid, sku,
                $"En az {InvariantFormat.Number(capacity)} litre, {Decimal(laptop)} inç laptop sığan sırt çantalarını karşılaştırmak istiyorum. Bütçem {Money(max)} TL.",
                new ShopperConstraints
                {
                    Category = Category.Backpacks, MinimumCapacityLiters = capacity,
                    MinimumLaptopSizeIn = InvariantFormat.ParseDecimal(laptop), MaxPriceTry = max,
                });
        }

        // No valid product: the generator never produces size 47 (max 45), headphones with 60+ hours of battery,
        // returnable opened small electronics, or next-day delivery to Ankara (minimum 2 business days).
        yield return new("I15", Persona.SizeSensitive, none, null,
            "47 numara siyah bir koşu ayakkabısı arıyorum.",
            new ShopperConstraints { Category = Category.Sneakers, ShoeSize = 47, Color = "Siyah" });

        yield return new("I16", Persona.BudgetShopper, none, null,
            "500 TL altında, aktif gürültü engellemeli ve en az 60 saat pil ömrü olan bir kulaklık istiyorum.",
            new ShopperConstraints { Category = Category.Headphones, MaxPriceTry = 500m, NoiseCancellation = true, MinimumBatteryHours = 60 });

        yield return new("I17", Persona.ReturnSensitive, none, null,
            "Kutusunu açtıktan sonra da koşulsuz iade edebileceğim bir akıllı bileklik istiyorum.",
            new ShopperConstraints
            {
                Category = Category.SmallElectronics, ProductType = "Akıllı Bileklik",
                ReturnRequirement = new ReturnRequirement(ReturnQuestion.OpenedReturn, AllowConditional: false),
            });

        yield return new("I18", Persona.UrgentDelivery, none, null,
            "Yarın Ankara'da olacak, tamamen su geçirmez bir sırt çantası lazım.",
            new ShopperConstraints
            {
                Category = Category.Backpacks, MinimumWaterResistance = WaterResistance.Waterproof, DeliverTo = City.Ankara, DeliverWithinDays = 1,
            });

        // Insufficient information: sneakers need a size; a delivery deadline needs a destination city.
        yield return new("I19", Persona.GiftShopper, insufficient, null,
            "Kız arkadaşıma hediye olarak beyaz bir spor ayakkabı almak istiyorum, bütçem 3.000 TL civarı.",
            new ShopperConstraints { Category = Category.Sneakers, Color = "Beyaz", MaxPriceTry = 3000m });

        yield return new("I20", Persona.UrgentDelivery, insufficient, null,
            "En geç iki gün içinde elimde olacak bir Bluetooth hoparlör istiyorum.",
            new ShopperConstraints { Category = Category.SmallElectronics, ProductType = "Bluetooth Hoparlör", DeliverWithinDays = 2 });
    }

    private static decimal CeilTo(decimal value, int step) => Math.Ceiling(value / step) * step;

    private static string LowerColor(string name) => CatalogTemplates.SneakerColors.First(c => c.Name == name).Lower;

    private static string BudgetNoun(Category category) => category switch
    {
        Category.Backpacks => "sırt çantası",
        Category.Headphones => "kulaklık",
        Category.SmallElectronics => "küçük elektronik ürün",
        _ => "spor ayakkabı",
    };

    private sealed record IntentDraft(
        string Id,
        Persona Persona,
        IntentOutcome IntendedOutcome,
        string? AnchorSku,
        string Text,
        ShopperConstraints Constraints,
        string? TrapSku = null);

    private sealed class Anchors(AuthoritativeFacts facts, DeterministicRandom rng)
    {
        public AuthoritativeFacts Facts { get; } = facts;

        public string Pick(string intentId, Category category, Func<string, bool> predicate) =>
            rng.Fork(intentId).Pick(Candidates(intentId, category, predicate));

        public string PickCheapest(string intentId, Category category, int poolSize) =>
            rng.Fork(intentId).Pick(Candidates(intentId, category, _ => true)
                .OrderBy(Price)
                .ThenBy(s => s, StringComparer.Ordinal)
                .Take(poolSize)
                .ToList());

        public string PickTrap(string intentId, IEnumerable<string> candidates)
        {
            var list = candidates.Order(StringComparer.Ordinal).ToList();
            return list.Count > 0
                ? rng.Fork(intentId + "-trap").Pick(list)
                : throw new InvalidOperationException($"Intent {intentId}: no trap candidate.");
        }

        public string Attr(string sku, string key) =>
            Facts.AttributeOf(sku, key) ?? throw new InvalidOperationException($"{sku} has no '{key}'.");

        public decimal Number(string sku, string key) => InvariantFormat.ParseDecimal(Attr(sku, key));

        public WaterResistance Water(string sku) => Enum.Parse<WaterResistance>(Attr(sku, AttributeKeys.WaterResistance));

        public decimal Price(string sku) => Facts.PriceOf(sku)!.Value;

        public string Warehouse(string sku) => Facts.WarehouseOf(sku);

        private List<string> Candidates(string intentId, Category category, Func<string, bool> predicate)
        {
            var candidates = Facts.AllSkus
                .Where(s => Facts.CategoryOf(s) == category && Facts.IsInStock(s) == true && predicate(s))
                .ToList();
            return candidates.Count > 0
                ? candidates
                : throw new InvalidOperationException($"Intent {intentId}: no in-stock anchor SKU matches.");
        }
    }
}
