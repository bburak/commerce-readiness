using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.SourceOfTruth;
using CommerceReadiness.Core.Time;

namespace CommerceReadiness.DemoStore.Generation;

public sealed record InjectionResult(StoreSnapshot Published, ImmutableArray<SemanticFact> PublishedFacts, ImmutableArray<InjectedDefect> Defects);

/// <summary>
/// Derives the published store from the clean store by injecting known defects. Defects only touch storefront
/// projections (catalog columns and description text) and the published return policy; the published Pricing,
/// Inventory, Shipping and Payments files stay authoritative, so every defect is detectable from published data.
/// </summary>
public static class DefectInjector
{
    public const int InventoryShowsInStockCount = 3;
    public const int InventoryShowsOutOfStockCount = 2;
    public const int PriceConflictCount = 4;
    public const int DeliveryConflictCount = 3;
    public const int MissingWithTextEvidenceCount = 3;
    public const int MissingWithoutTextEvidenceCount = 3;
    public const int MisleadingDescriptionCount = 4;
    public const int AmbiguousReturnCount = 2;

    private static readonly decimal[] PriceFactors = [0.80m, 0.85m, 1.15m, 1.20m];

    public static InjectionResult Inject(
        StoreSnapshot clean,
        ImmutableArray<SemanticFact> cleanFacts,
        Reservations reservations,
        DeterministicRandom rng,
        SimulationContext context)
    {
        var state = new PublishedDraft(clean, cleanFacts);
        var defects = new List<InjectedDefect>();

        InjectInventoryConflicts(state, reservations, rng.Fork("inventory"), context, defects);
        InjectPriceConflicts(state, reservations, rng.Fork("price"), defects);
        InjectDeliveryConflicts(state, reservations, rng.Fork("delivery"), context, defects);
        InjectMissingAttributes(state, reservations, rng.Fork("missing"), defects);
        InjectMisleadingDescriptions(state, reservations, rng.Fork("misleading"), defects);
        InjectAmbiguousReturns(state, reservations, rng.Fork("returns"), defects);

        return new InjectionResult(
            state.ToSnapshot(),
            [.. state.Facts.OrderBy(f => f.ProductId, StringComparer.Ordinal).ThenBy(f => f.AttributeKey, StringComparer.Ordinal)],
            [.. defects
                .OrderBy(d => d.DefectId, StringComparer.Ordinal)
                .Select(d => d with { ExpectedImpacts = EvaluationUnits.ExpectedImpacts(d, cleanFacts) })]);
    }

    private static void InjectInventoryConflicts(
        PublishedDraft state, Reservations reservations, DeterministicRandom rng, SimulationContext context, List<InjectedDefect> defects)
    {
        var clean = state.Clean;
        var outOfStock = clean.Listings.Where(l => l.StockStatus == StockStatus.OutOfStock).Select(l => l.Sku).ToList();
        bool IsSneaker(string sku) => clean.ProductOf(sku).Category == Category.Sneakers;

        // At least one stale IN_STOCK sneaker variant (SKU-level conflict) and the rest on single-SKU products,
        // which the intent generator uses to plant a guaranteed published-data trap.
        var showsInStock = reservations.TakeFree(
                rng.Fork("shows-in-stock-variant"), outOfStock.Where(IsSneaker), Reservations.Sku, 1, "stale IN_STOCK variant")
            .Concat(reservations.TakeFree(
                rng.Fork("shows-in-stock"), outOfStock.Where(s => !IsSneaker(s)), Reservations.Sku, InventoryShowsInStockCount - 1, "stale IN_STOCK"));
        foreach (var sku in showsInStock)
        {
            var inventory = clean.GetInventory(sku);
            var stalePromise = DeliveryCalculator.EstimateFromWarehouse(clean, inventory.WarehouseId, City.Istanbul, context.Now);
            state.Listings[sku] = state.Listings[sku] with { StockStatus = StockStatus.InStock, DeliveryPromiseIstanbul = stalePromise };
            defects.Add(new InjectedDefect(
                $"DEF-INV-{sku}", DefectType.InventoryConflict, clean.GetVariant(sku).ProductId, sku, null, "stockStatus",
                StockStatusText.InStock, $"available={InvariantFormat.Number(inventory.Available)}", DataSource.Inventory,
                "Set catalog stockStatus to OUT_OF_STOCK and remove the delivery promise; inventory.json reports 0 units."));
        }

        var inStock = clean.Listings.Where(l => l.StockStatus == StockStatus.InStock).Select(l => l.Sku);
        foreach (var sku in reservations.TakeFree(rng.Fork("shows-out-of-stock"), inStock, Reservations.Sku, InventoryShowsOutOfStockCount, "stale OUT_OF_STOCK"))
        {
            var available = clean.GetInventory(sku).Available;
            state.Listings[sku] = state.Listings[sku] with { StockStatus = StockStatus.OutOfStock, DeliveryPromiseIstanbul = null };
            defects.Add(new InjectedDefect(
                $"DEF-INV-{sku}", DefectType.InventoryConflict, clean.GetVariant(sku).ProductId, sku, null, "stockStatus",
                StockStatusText.OutOfStock, $"available={InvariantFormat.Number(available)}", DataSource.Inventory,
                $"Set catalog stockStatus to IN_STOCK; inventory.json reports {InvariantFormat.Number(available)} units."));
        }
    }

    private static void InjectPriceConflicts(PublishedDraft state, Reservations reservations, DeterministicRandom rng, List<InjectedDefect> defects)
    {
        var clean = state.Clean;
        var skus = reservations.TakeFree(rng, clean.Listings.Select(l => l.Sku), Reservations.Sku, PriceConflictCount, "price conflicts");
        for (var i = 0; i < skus.Count; i++)
        {
            var sku = skus[i];
            var price = clean.GetPrice(sku);
            var stale = StoreConfiguration.RoundPrice(price * PriceFactors[i]);
            if (stale == price)
            {
                stale += 100m;
            }

            state.Listings[sku] = state.Listings[sku] with { ListedPrice = stale };
            defects.Add(new InjectedDefect(
                $"DEF-PRC-{sku}", DefectType.PriceConflict, clean.GetVariant(sku).ProductId, sku, null, "listedPrice",
                InvariantFormat.Money(stale), InvariantFormat.Money(price), DataSource.Pricing,
                $"Update catalog listedPrice to {InvariantFormat.Money(price)} TRY from price-list.json."));
        }
    }

    private static void InjectDeliveryConflicts(
        PublishedDraft state, Reservations reservations, DeterministicRandom rng, SimulationContext context, List<InjectedDefect> defects)
    {
        var clean = state.Clean;
        var promised = clean.Listings.Where(l => l.DeliveryPromiseIstanbul is not null).Select(l => l.Sku);
        foreach (var sku in reservations.TakeFree(rng, promised, Reservations.Sku, DeliveryConflictCount, "delivery conflicts"))
        {
            var calculated = clean.GetListing(sku).DeliveryPromiseIstanbul!.Value;
            var tomorrow = context.Today.AddDays(1);
            var claimed = calculated > tomorrow ? tomorrow : context.Today;
            state.Listings[sku] = state.Listings[sku] with { DeliveryPromiseIstanbul = claimed };
            defects.Add(new InjectedDefect(
                $"DEF-DLV-{sku}", DefectType.DeliveryConflict, clean.GetVariant(sku).ProductId, sku, null, "deliveryPromiseIstanbul",
                InvariantFormat.Date(claimed), InvariantFormat.Date(calculated), DataSource.Shipping,
                $"Set the Istanbul delivery promise to {InvariantFormat.Date(calculated)} as calculated from shipping.json at the simulation clock."));
        }
    }

    private static void InjectMissingAttributes(PublishedDraft state, Reservations reservations, DeterministicRandom rng, List<InjectedDefect> defects)
    {
        var clean = state.Clean;
        var withText = state.CleanFacts.Select(f => (f.ProductId, f.AttributeKey)).ToHashSet();
        var candidates = clean.Products
            .Where(p => p.Category != Category.Sneakers)
            .SelectMany(p => CategoryRequirements.For(p.Category)
                .Where(r => r.Level == AttributeLevel.Product)
                .Select(r => (p.ProductId, Key: r.Key)))
            .ToList();

        var chosen = reservations.TakeFree(
                rng.Fork("with-text"), candidates.Where(withText.Contains), c => Reservations.Product(c.ProductId),
                MissingWithTextEvidenceCount, "missing attributes with text evidence")
            .Concat(reservations.TakeFree(
                rng.Fork("without-text"), candidates.Where(c => !withText.Contains(c)), c => Reservations.Product(c.ProductId),
                MissingWithoutTextEvidenceCount, "missing attributes without text evidence"));

        foreach (var (productId, key) in chosen)
        {
            var product = state.Products[productId];
            var value = product.Attributes[key];
            state.Products[productId] = product with { Attributes = product.Attributes.Remove(key) };
            defects.Add(new InjectedDefect(
                $"DEF-MRA-{productId}-{key}", DefectType.MissingRequiredAttribute, productId, DecoyGenerator.SingleSku(clean, productId), null, key,
                null, value, DataSource.Catalog,
                $"Populate structured attribute '{key}' with '{value}'."));
        }
    }

    private static void InjectMisleadingDescriptions(PublishedDraft state, Reservations reservations, DeterministicRandom rng, List<InjectedDefect> defects)
    {
        var clean = state.Clean;
        var candidates = clean.Products
            .SelectMany(p => MisleadingClaims(p).Select(claim => (p.ProductId, claim.Key, claim.Asserted)))
            .ToList();

        foreach (var (productId, key, asserted) in reservations.TakeFree(
                     rng, candidates, c => Reservations.Product(c.ProductId), MisleadingDescriptionCount, "misleading descriptions"))
        {
            var product = state.Products[productId];
            var phrase = DescriptionPhrases.For(key, asserted);
            var existing = state.Facts.SingleOrDefault(f => f.ProductId == productId && f.AttributeKey == key);
            string description;
            if (existing is null)
            {
                description = $"{product.Description} {phrase}";
            }
            else
            {
                description = product.Description.Replace(existing.Phrase, phrase, StringComparison.Ordinal);
                state.Facts.Remove(existing);
            }

            state.Products[productId] = product with { Description = description };
            state.Facts.Add(new SemanticFact(StoreKind.Published, productId, null, key, asserted, phrase, SupportsAuthoritativeValue: false));

            var authoritative = product.Attributes[key];
            defects.Add(new InjectedDefect(
                $"DEF-MSD-{productId}-{key}", DefectType.MisleadingDescription, productId, DecoyGenerator.SingleSku(clean, productId), null, key,
                phrase, authoritative, DataSource.Catalog,
                $"Rewrite the description so it states {key}={authoritative} (it currently asserts {asserted})."));
        }
    }

    /// <summary>Claims that would contradict the product's authoritative structured value.</summary>
    private static IEnumerable<(string Key, string Asserted)> MisleadingClaims(Product product)
    {
        string? Value(string key) => product.Attributes.Find(key);

        switch (product.Category)
        {
            case Category.Backpacks when Value(AttributeKeys.WaterResistance) != nameof(WaterResistance.Waterproof):
                yield return (AttributeKeys.WaterResistance, nameof(WaterResistance.Waterproof));
                break;
            case Category.Headphones:
                if (Value(AttributeKeys.NoiseCancellation) == "false")
                {
                    yield return (AttributeKeys.NoiseCancellation, "true");
                }

                if (Value(AttributeKeys.Microphone) == "false")
                {
                    yield return (AttributeKeys.Microphone, "true");
                }

                if (InvariantFormat.TryParseDecimal(Value(AttributeKeys.BatteryHours), out var hours))
                {
                    yield return (AttributeKeys.BatteryHours, InvariantFormat.Number(hours + 25));
                }

                break;
            case Category.Sneakers when Value(AttributeKeys.Material) != "Deri":
                yield return (AttributeKeys.Material, "Deri");
                break;
            case Category.SmallElectronics when Value(AttributeKeys.WarrantyMonths) == "24":
                yield return (AttributeKeys.WarrantyMonths, "36");
                break;
        }
    }

    private static void InjectAmbiguousReturns(PublishedDraft state, Reservations reservations, DeterministicRandom rng, List<InjectedDefect> defects)
    {
        var candidates = state.Clean.ReturnRules
            .Where(r => r.Question != ReturnQuestion.DefectiveReturn)
            .Select(r => r.CellId);

        foreach (var cellId in reservations.TakeFree(rng, candidates, Reservations.Policy, AmbiguousReturnCount, "ambiguous return rules"))
        {
            var rule = state.ReturnRules[cellId];
            const string vague = "Duruma göre değerlendirilir.";
            state.ReturnRules[cellId] = rule with { Answer = ReturnAnswer.Unspecified, ConditionTr = vague, WindowDays = null };

            var window = rule.WindowDays is { } days ? $" within {InvariantFormat.Number(days)} days" : string.Empty;
            var condition = rule.ConditionTr is null ? string.Empty : $" (condition: {rule.ConditionTr})";
            defects.Add(new InjectedDefect(
                $"DEF-RET-{cellId.Replace(':', '-')}", DefectType.AmbiguousReturnRule, null, null, cellId, "answer",
                $"{ReturnAnswer.Unspecified}: {vague}", rule.Answer.ToString(), DataSource.ReturnPolicy,
                $"State an explicit answer: {rule.Answer}{window}{condition}."));
        }
    }

    private sealed class PublishedDraft
    {
        public PublishedDraft(StoreSnapshot clean, ImmutableArray<SemanticFact> cleanFacts)
        {
            Clean = clean;
            CleanFacts = cleanFacts;
            Listings = clean.Listings.ToDictionary(l => l.Sku, StringComparer.Ordinal);
            Products = clean.Products.ToDictionary(p => p.ProductId, StringComparer.Ordinal);
            ReturnRules = clean.ReturnRules.ToDictionary(r => r.CellId, StringComparer.Ordinal);
            Facts = [.. cleanFacts.Select(f => f with { Store = StoreKind.Published })];
        }

        public StoreSnapshot Clean { get; }
        public ImmutableArray<SemanticFact> CleanFacts { get; }
        public Dictionary<string, CatalogListing> Listings { get; }
        public Dictionary<string, Product> Products { get; }
        public Dictionary<string, ReturnRule> ReturnRules { get; }
        public List<SemanticFact> Facts { get; }

        public StoreSnapshot ToSnapshot() => Clean with
        {
            Kind = StoreKind.Published,
            Products = [.. Clean.Products.Select(p => Products[p.ProductId])],
            Listings = [.. Clean.Listings.Select(l => Listings[l.Sku])],
            ReturnRules = [.. Clean.ReturnRules.Select(r => ReturnRules[r.CellId])],
        };
    }
}
