using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation.Retrieval;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Core.Time;

namespace CommerceReadiness.DemoStore.Generation;

/// <summary>
/// Builds the retrieval benchmark: one case per shopper intent (query = intent text, filters projected by
/// <see cref="RetrievalPolicy"/>) plus discovery-only templates whose differentiating need is semantic. Relevant SKUs
/// always come from <see cref="RetrievalRelevanceOracle"/> over the clean store; no case is hand-labelled.
/// </summary>
public static class RetrievalBenchmarkGenerator
{
    public static RetrievalBenchmarkFile Generate(StoreSnapshot clean, ImmutableArray<ShopperIntent> intents, SimulationContext context)
    {
        var shopperCases = intents.Select(intent => RetrievalRelevanceOracle.CreateCase(
            clean,
            context.Now,
            intent.Id,
            RetrievalCaseGroup.ShopperDerived,
            intent.Id,
            intent.Text,
            intent.Constraints,
            intent.Expected.Outcome == IntentOutcome.InsufficientInformation));

        var discoveryCases = DiscoveryTemplates(clean, context).Select(t =>
        {
            var benchmarkCase = RetrievalRelevanceOracle.CreateCase(
                clean, context.Now, t.Id, RetrievalCaseGroup.DiscoveryOnly, null, t.Query, t.Constraints, insufficientInformation: false);
            if (!benchmarkCase.IsSemanticDiscriminating)
            {
                throw new InvalidOperationException(
                    $"Discovery case {t.Id} gives semantic retrieval no job: {benchmarkCase.RelevantSkus.Length} relevant of {benchmarkCase.HardFilterPoolSize} filtered.");
            }

            return benchmarkCase;
        });

        return new RetrievalBenchmarkFile(
            RetrievalBenchmarkFile.CurrentSchemaVersion,
            clean.StoreVersion,
            context.Now,
            [.. shopperCases, .. discoveryCases]);
    }

    private static IEnumerable<(string Id, string Query, ShopperConstraints Constraints)> DiscoveryTemplates(StoreSnapshot clean, SimulationContext context)
    {
        var backpackBudget = MedianInStockPrice(clean, Category.Backpacks);
        var headphoneBudget = MedianInStockPrice(clean, Category.Headphones);

        yield return ("R01",
            $"{TurkishText.Money(backpackBudget)} TL altında, yağmurlu havalarda işe giderken kullanabileceğim bir sırt çantası arıyorum.",
            new ShopperConstraints { Category = Category.Backpacks, MaxPriceTry = backpackBudget, MinimumWaterResistance = WaterResistance.WaterRepellent });
        yield return ("R02",
            "Sağanak yağmurda bile içindekileri kuru tutacak bir çanta arıyorum.",
            new ShopperConstraints { Category = Category.Backpacks, MinimumWaterResistance = WaterResistance.Waterproof });
        yield return ("R03",
            "15,6 inç laptopumu taşıyabileceğim, yağmurda ıslanmayan bir çanta istiyorum.",
            new ShopperConstraints { Category = Category.Backpacks, MinimumLaptopSizeIn = 15.6m, MinimumWaterResistance = WaterResistance.Waterproof });
        yield return ("R04",
            "Uzun bir seyahat için en az 25 litrelik, hafif yağmura dayanıklı bir çanta arıyorum.",
            new ShopperConstraints { Category = Category.Backpacks, MinimumCapacityLiters = 25m, MinimumWaterResistance = WaterResistance.WaterRepellent });
        yield return ("R05",
            $"{TurkishText.Money(headphoneBudget)} TL altında, toplantılarda ve telefon görüşmelerinde kullanabileceğim bir kulaklık.",
            new ShopperConstraints { Category = Category.Headphones, MaxPriceTry = headphoneBudget, Microphone = true });
        yield return ("R06",
            "Kalabalık metroda dış sesi kesen bir kulaklık istiyorum.",
            new ShopperConstraints { Category = Category.Headphones, NoiseCancellation = true });
        yield return ("R07",
            "En az 25 saat pil ömrü olan, dış sesi kesen bir kulaklık arıyorum.",
            new ShopperConstraints { Category = Category.Headphones, MinimumBatteryHours = 25, NoiseCancellation = true });
        yield return ("R08",
            "Seyahatte telefonumu yolda şarj edebileceğim bir cihaz arıyorum.",
            new ShopperConstraints { Category = Category.SmallElectronics, ProductType = "Powerbank" });
        yield return ("R09",
            "Sabah işe hazırlanırken saçımı hızlıca kurutacak bir cihaz istiyorum.",
            new ShopperConstraints { Category = Category.SmallElectronics, ProductType = "Saç Kurutma Makinesi" });
        yield return ("R10",
            "Spor yaparken adımlarımı takip edecek bir cihaz arıyorum.",
            new ShopperConstraints { Category = Category.SmallElectronics, ProductType = "Akıllı Bileklik" });
    }

    /// <summary>Median in-stock authoritative price of a category, rounded up to 500 TL.</summary>
    private static decimal MedianInStockPrice(StoreSnapshot clean, Category category)
    {
        var prices = clean.Variants
            .Where(v => clean.GetProduct(v.ProductId).Category == category && clean.GetInventory(v.Sku).Available > 0)
            .Select(v => clean.GetPrice(v.Sku))
            .Order()
            .ToList();
        return Math.Ceiling(prices[prices.Count / 2] / 500m) * 500m;
    }
}
