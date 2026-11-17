using System.Globalization;
using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Core.Time;

namespace CommerceReadiness.Tests;

public class OracleTests
{
    private static readonly StoreSnapshot Clean = TestData.Seed42.Clean;
    private static readonly SimulationContext Context = TestData.Seed42.Context;
    private static readonly ImmutableArray<ShopperIntent> Intents = TestData.Seed42.Intents.Intents;

    [Fact]
    public void Intent_set_has_the_intended_shape()
    {
        Assert.Equal(20, Intents.Length);
        Assert.Equal(20, Intents.Select(i => i.Id).Distinct().Count());
        Assert.Equal(14, Intents.Count(i => i.Expected.Outcome == IntentOutcome.ValidProductsExist));
        Assert.Equal(4, Intents.Count(i => i.Expected.Outcome == IntentOutcome.NoValidProduct));
        Assert.Equal(2, Intents.Count(i => i.Expected.Outcome == IntentOutcome.InsufficientInformation));
        Assert.All(Enum.GetValues<Persona>(), p => Assert.Contains(Intents, i => i.Persona == p));
        Assert.All(Intents, i => Assert.False(string.IsNullOrWhiteSpace(i.Text)));
    }

    [Fact]
    public void Oracle_rejects_the_published_store()
    {
        var constraints = Intents[0].Constraints;
        Assert.Throws<ArgumentException>(() => OracleShopper.Evaluate(TestData.Seed42.Published, constraints, Context));
    }

    [Fact]
    public void Oracle_judges_stock_from_clean_inventory_not_from_published_catalog()
    {
        var staleInStock = TestData.Seed42.GroundTruth.Defects
            .Where(d => d.Type == DefectType.InventoryConflict && d.PublishedValue == StockStatusText.InStock)
            .ToList();
        Assert.NotEmpty(staleInStock);

        foreach (var defect in staleInStock)
        {
            var category = Clean.ProductOf(defect.Sku!).Category;
            var constraints = new ShopperConstraints { Category = category, ShoeSize = ShoeSizeOf(defect.Sku!) };
            var result = OracleShopper.Evaluate(Clean, constraints, Context);

            Assert.DoesNotContain(defect.Sku!, result.ValidSkus);
            Assert.Contains(result.Forbidden, f => f.Sku == defect.Sku && f.Violations.Any(v => v.Constraint == "inStock"));
        }
    }

    [Fact]
    public void Planted_trap_looks_valid_in_published_data_but_is_forbidden()
    {
        var traps = Intents.SelectMany(i => i.Expected.Forbidden.Where(f => f.LooksValidInPublished).Select(f => (i.Id, f.Sku))).ToList();
        Assert.Contains(traps, t => t.Id == "I02");

        foreach (var (_, sku) in traps)
        {
            Assert.Contains(TestData.Seed42.GroundTruth.Defects, d => d.Sku == sku);
        }
    }

    [Fact]
    public void Expected_results_are_reproduced_by_the_oracle()
    {
        foreach (var intent in Intents)
        {
            var result = OracleShopper.Evaluate(Clean, intent.Constraints, Context);
            Assert.Equal(intent.Expected.Outcome, result.Outcome);
            Assert.Equal(intent.Expected.ValidSkus.ToArray(), result.ValidSkus.ToArray());
            Assert.Equal(intent.Expected.MissingInformation.ToArray(), result.MissingInformation.ToArray());
            Assert.Equal(intent.Expected.Forbidden.Select(f => f.Sku), result.Forbidden.Select(f => f.Sku));
        }
    }

    [Fact]
    public void Every_valid_sku_satisfies_every_mandatory_constraint()
    {
        foreach (var intent in Intents.Where(i => i.Expected.Outcome == IntentOutcome.ValidProductsExist))
        {
            Assert.NotEmpty(intent.Expected.ValidSkus);
            Assert.All(intent.Expected.ValidSkus, sku => Assert.True(
                IndependentCheck.Satisfies(Clean, intent.Constraints, sku, Context), $"{intent.Id}: {sku} violates a constraint."));
            Assert.All(intent.Expected.Forbidden, f => Assert.False(
                IndependentCheck.Satisfies(Clean, intent.Constraints, f.Sku, Context), $"{intent.Id}: {f.Sku} is forbidden but valid."));
        }
    }

    [Fact]
    public void No_valid_result_intents_truly_have_zero_valid_skus()
    {
        var noValid = Intents.Where(i => i.Expected.Outcome == IntentOutcome.NoValidProduct).ToList();
        Assert.Equal(4, noValid.Count);

        foreach (var intent in noValid)
        {
            Assert.True(intent.Expected.NoValidAnswer);
            Assert.True(intent.Expected.ShouldAbstain);
            Assert.Empty(intent.Expected.ValidSkus);
            Assert.All(Clean.Variants, v => Assert.False(
                IndependentCheck.Satisfies(Clean, intent.Constraints, v.Sku, Context), $"{intent.Id}: {v.Sku} satisfies all constraints."));
        }
    }

    [Fact]
    public void Insufficient_information_intents_name_what_is_missing()
    {
        var insufficient = Intents.Where(i => i.Expected.Outcome == IntentOutcome.InsufficientInformation).ToList();
        Assert.Equal(2, insufficient.Count);
        Assert.All(insufficient, i =>
        {
            Assert.NotEmpty(i.Expected.MissingInformation);
            Assert.Empty(i.Expected.ValidSkus);
            Assert.False(i.Expected.NoValidAnswer);
            Assert.True(i.Expected.ShouldAbstain);
        });
        Assert.Contains(insufficient, i => i.Expected.MissingInformation.Contains("shoeSize"));
        Assert.Contains(insufficient, i => i.Expected.MissingInformation.Contains("deliverTo"));
    }

    [Fact]
    public void Oracle_output_is_identical_across_runs()
    {
        foreach (var intent in Intents)
        {
            Assert.Equal(
                JsonDefaults.Serialize(OracleShopper.Evaluate(Clean, intent.Constraints, Context)),
                JsonDefaults.Serialize(OracleShopper.Evaluate(Clean, intent.Constraints, Context)));
        }
    }

    private static int? ShoeSizeOf(string sku) =>
        Clean.GetVariant(sku).Attributes.Find("shoeSize") is { } size ? int.Parse(size, CultureInfo.InvariantCulture) : null;

    /// <summary>A second, deliberately separate implementation of the constraint semantics.</summary>
    private static class IndependentCheck
    {
        public static bool Satisfies(StoreSnapshot clean, ShopperConstraints c, string sku, SimulationContext context)
        {
            var product = clean.ProductOf(sku);
            var variant = clean.GetVariant(sku);
            string? A(string key) => variant.Attributes.TryGetValue(key, out var v) ? v : product.Attributes.TryGetValue(key, out var p) ? p : null;
            decimal? N(string key) => A(key) is { } s ? decimal.Parse(s, CultureInfo.InvariantCulture) : null;

            if (c.Category is { } category && product.Category != category) return false;
            if (clean.Inventory.Single(i => i.Sku == sku).Available <= 0) return false;

            var price = clean.Prices.Single(p => p.Sku == sku).PriceTry;
            if (c.MaxPriceTry is { } max && price > max) return false;

            if (c.DeliverTo is { } city)
            {
                var date = DeliveryCalculator.EstimateDelivery(clean, sku, city, context.Now);
                if (date is null) return false;
                if (c.DeliverWithinDays is { } days && date.Value > context.Today.AddDays(days)) return false;
            }

            if (c.MinimumLaptopSizeIn is { } laptop && !(N("laptopSizeIn") >= laptop)) return false;
            if (c.MinimumCapacityLiters is { } capacity && !(N("capacityLiters") >= capacity)) return false;
            if (c.MinimumBatteryHours is { } battery && !(N("batteryHours") >= battery)) return false;
            if (c.MinimumWarrantyMonths is { } warranty && !(N("warrantyMonths") >= warranty)) return false;
            if (c.MinimumWaterResistance is { } water && !(A("waterResistance") is { } w && Enum.Parse<WaterResistance>(w) >= water)) return false;
            if (c.ShoeSize is { } size && A("shoeSize") != size.ToString(CultureInfo.InvariantCulture)) return false;
            if (c.Color is { } color && A("color") != color) return false;
            if (c.ProductType is { } type && A("productType") != type) return false;
            if (c.TargetGroup is { } group && A("genderOrTargetGroup") != group && A("genderOrTargetGroup") != "Unisex") return false;
            if (c.NoiseCancellation is { } anc && A("noiseCancellation") != (anc ? "true" : "false")) return false;
            if (c.Microphone is { } mic && A("microphone") != (mic ? "true" : "false")) return false;

            if (c.InstallmentCount is { } installments)
            {
                var allowed = clean.InstallmentRules
                    .Where(r => r.Category == product.Category && price >= r.MinimumPriceTry)
                    .Select(r => r.MaxInstallments)
                    .DefaultIfEmpty(1)
                    .Max();
                if (allowed < installments) return false;
            }

            if (c.ReturnRequirement is { } requirement)
            {
                var rule = clean.ReturnRules.Single(r => r.Category == product.Category && r.Question == requirement.Question);
                var answered = rule.Answer == ReturnAnswer.Yes || (rule.Answer == ReturnAnswer.Conditional && requirement.AllowConditional);
                if (!answered) return false;
                if (requirement.MinimumWindowDays is { } minimum && (rule.WindowDays ?? 0) < minimum) return false;
            }

            return true;
        }
    }
}
