using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation.Shopping;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Shopper.Tools;

namespace CommerceReadiness.Tests;

public class ShopperEvaluatorTests
{
    private static readonly ShopperIntent I01 = TestData.Seed42.Intents.Intents.Single(i => i.Id == "I01");
    private static readonly ShopperIntent I15 = TestData.Seed42.Intents.Intents.Single(i => i.Id == "I15");
    private static readonly ShopperIntent I17 = TestData.Seed42.Intents.Intents.Single(i => i.Id == "I17");
    private static readonly ShopperIntent I18 = TestData.Seed42.Intents.Intents.Single(i => i.Id == "I18");
    private static readonly ShopperIntent I19 = TestData.Seed42.Intents.Intents.Single(i => i.Id == "I19");

    [Fact]
    public void Valid_grounded_recommendation_earns_primary_and_grounding_credit()
    {
        var sku = I01.Expected.ValidSkus[0];
        var price = TestData.Seed42.Clean.GetPrice(sku);
        var delivery = DeliveryCalculator.EstimateDelivery(TestData.Seed42.Clean, sku, City.Istanbul, TestData.Seed42.Context.Now)!;
        var traces = new[]
        {
            ShopperTestSupport.Trace(1, ShopperToolNames.SearchProducts, new SearchProductsArguments("q", "Backpacks", 3500, true, null, null, 17, null, null, null, 5),
                new SearchProductsData([new(1, sku, "P", "t", Category.Backpacks, price, 3, null, null, 0.9)])),
            ShopperTestSupport.Trace(2, ShopperToolNames.GetProductDetails, new SkuListArguments([sku]),
                new ProductDetailsData([Details(sku)])),
            ShopperTestSupport.Trace(3, ShopperToolNames.GetInventory, new SkuListArguments([sku]),
                new InventoryData([new(sku, 3, "IST-TUZLA")])),
            ShopperTestSupport.Trace(4, ShopperToolNames.GetDeliveryEstimate, new DeliveryEstimateArguments(sku, "Istanbul", 2),
                new DeliveryEstimateData(sku, City.Istanbul, DateOnly.FromDateTime(TestData.Seed42.Context.Now.DateTime), true, delivery.Value, "IST-TUZLA", 14, 1,
                    DeliveryCalculator.Deadline(TestData.Seed42.Context.Now, 2), true)),
        };
        var result = ShopperTestSupport.Recommended(ShopperTestSupport.Rec(sku, RecommendationConfidence.Confirmed,
            ShopperTestSupport.Attr(ProductAttributeField.Category, nameof(Category.Backpacks), "EVID-002"),
            ShopperTestSupport.Attr(ProductAttributeField.WaterResistance, nameof(WaterResistance.WaterRepellent), "EVID-002"),
            ShopperTestSupport.Attr(ProductAttributeField.LaptopSizeIn, "17", "EVID-002"),
            ShopperTestSupport.Price(InvariantFormat.Money(price), "EVID-001"),
            ShopperTestSupport.Stock(ClaimFormat.InStock, "EVID-003"),
            ShopperTestSupport.Delivery(City.Istanbul, InvariantFormat.Date(delivery.Value), "EVID-004")));

        var report = Evaluate(("I01", result, traces));
        Assert.Equal(1, report.Primary.Single(m => m.Name == "GroundedRecommendationSuccessRate").Numerator);
        Assert.Equal(1, report.Primary.Single(m => m.Name == "PrimaryRecommendationSuccessRate").Numerator);
        Assert.All(report.Runs[0].Recommendations[0].Constraints, c => Assert.True(c.Grounded));
    }

    [Fact]
    public void Valid_sku_with_zero_claims_gets_no_grounding_credit()
    {
        var sku = I01.Expected.ValidSkus[0];
        var result = ShopperTestSupport.Recommended(ShopperTestSupport.Rec(sku, RecommendationConfidence.Confirmed));
        var report = Evaluate(("I01", result, Array.Empty<ToolInvocationTrace>()));

        Assert.Equal(0, report.Primary.Single(m => m.Name == "GroundedRecommendationSuccessRate").Numerator);
        Assert.Equal(1, report.Primary.Single(m => m.Name == "PrimaryRecommendationSuccessRate").Numerator);
        Assert.Contains(report.Runs[0].Recommendations[0].Constraints, c => c.Detail == "no claim");
    }

    [Fact]
    public void Confirmed_wrong_recommendation_is_counted()
    {
        var bad = I01.Expected.Forbidden[0].Sku;
        var result = ShopperTestSupport.Recommended(ShopperTestSupport.Rec(bad, RecommendationConfidence.Confirmed,
            ShopperTestSupport.Stock(ClaimFormat.InStock, "EVID-001")));
        var traces = new[]
        {
            ShopperTestSupport.Trace(1, ShopperToolNames.GetInventory, new SkuListArguments([bad]),
                new InventoryData([new(bad, 1, "IST-TUZLA")])),
        };
        var report = Evaluate(("I01", result, traces));
        Assert.Equal(1, report.Primary.Single(m => m.Name == "ConfidentWrongRate").Numerator);
    }

    [Fact]
    public void Claim_verdicts_distinguish_malformed_wrong_source_and_contradiction()
    {
        var sku = I01.Expected.ValidSkus[0];
        var traces = new[]
        {
            ShopperTestSupport.Trace(1, ShopperToolNames.SearchProducts, new SearchProductsArguments("q", null, null, null, null, null, null, null, null, null, 5),
                new SearchProductsData([new(1, sku, "P", "t", Category.Backpacks, 1000m, 1, null, null, 0.1)])),
            ShopperTestSupport.Trace(2, ShopperToolNames.GetInventory, new SkuListArguments([sku]),
                new InventoryData([new(sku, 0, "IST-TUZLA")])),
        };
        var result = ShopperTestSupport.Recommended(ShopperTestSupport.Rec(sku, RecommendationConfidence.Confirmed,
            ShopperTestSupport.Price("1000", "EVID-001"), // malformed money
            ShopperTestSupport.Attr(ProductAttributeField.WaterResistance, nameof(WaterResistance.Waterproof), "EVID-001"), // wrong source
            ShopperTestSupport.Stock(ClaimFormat.InStock, "EVID-002"))); // contradicted (qty 0)

        var report = Evaluate(("I01", result, traces));
        var verdicts = report.Runs[0].Recommendations[0].Claims.Select(c => c.Verdict).ToList();
        Assert.Contains(ClaimVerdict.Malformed, verdicts);
        Assert.Contains(ClaimVerdict.WrongSource, verdicts);
        Assert.Contains(ClaimVerdict.Contradicted, verdicts);
    }

    [Fact]
    public void Correct_and_grounded_nomatch_for_i17_return_evidence()
    {
        var sku = "SKU-SE-009";
        var traces = new[]
        {
            ShopperTestSupport.Trace(1, ShopperToolNames.SearchProducts,
                new SearchProductsArguments("bileklik", "SmallElectronics", null, true, null, null, null, null, null, null, 5),
                new SearchProductsData([new(1, sku, "P-SE-009", "t", Category.SmallElectronics, 1000m, 2, null, null, 0.8)])),
            ShopperTestSupport.Trace(2, ShopperToolNames.GetReturnPolicy, new ReturnPolicyArguments(sku, "OpenedReturn"),
                new ReturnPolicyData(sku, Category.SmallElectronics, ReturnQuestion.OpenedReturn, ReturnAnswer.No, null, "x")),
        };
        var report = Evaluate(("I17", ShopperTestSupport.NoMatch("iade yok"), traces));
        Assert.True(report.Runs[0].OutcomeCorrect);
        Assert.True(report.Runs[0].GroundedNoMatch);
        Assert.Equal(1, report.Primary.Single(m => m.Name == "CorrectNoMatchRate").Numerator);
        Assert.Equal(1, report.Primary.Single(m => m.Name == "GroundedNoMatchRate").Numerator);
    }

    [Fact]
    public void Correct_nomatch_without_decisive_evidence_is_not_grounded()
    {
        var report = Evaluate(("I17", ShopperTestSupport.NoMatch("yok"), Array.Empty<ToolInvocationTrace>()));
        Assert.True(report.Runs[0].OutcomeCorrect);
        Assert.False(report.Runs[0].GroundedNoMatch);
        Assert.Equal(1, report.Primary.Single(m => m.Name == "CorrectNoMatchRate").Numerator);
        Assert.Equal(0, report.Primary.Single(m => m.Name == "GroundedNoMatchRate").Numerator);
    }

    [Fact]
    public void Grounded_nomatch_for_i18_delivery_evidence()
    {
        var sku = "SKU-BP-002";
        var date = DeliveryCalculator.EstimateDelivery(TestData.Seed42.Clean, sku, City.Ankara, TestData.Seed42.Context.Now)!;
        var traces = new[]
        {
            ShopperTestSupport.Trace(1, ShopperToolNames.SearchProducts,
                new SearchProductsArguments("waterproof", "Backpacks", null, true, null, null, null, null, null, null, 5),
                new SearchProductsData([new(1, sku, "P", "t", Category.Backpacks, 2000m, 1, null, null, 0.7)])),
            ShopperTestSupport.Trace(2, ShopperToolNames.GetDeliveryEstimate, new DeliveryEstimateArguments(sku, "Ankara", 1),
                new DeliveryEstimateData(sku, City.Ankara, DateOnly.FromDateTime(TestData.Seed42.Context.Now.DateTime), true, date, "IST-TUZLA", 14, 2,
                    DeliveryCalculator.Deadline(TestData.Seed42.Context.Now, 1), false)),
        };
        var report = Evaluate(("I18", ShopperTestSupport.NoMatch("Ankara'ya yetişmiyor"), traces));
        Assert.True(report.Runs[0].GroundedNoMatch);
    }

    [Fact]
    public void Grounded_nomatch_for_i15_uses_blocking_search_filter()
    {
        var traces = new[]
        {
            ShopperTestSupport.Trace(1, ShopperToolNames.SearchProducts,
                new SearchProductsArguments("47 numara", "Sneakers", null, true, 47, "Siyah", null, null, null, null, 5),
                new SearchProductsData([])),
        };
        var report = Evaluate(("I15", ShopperTestSupport.NoMatch("yok"), traces));
        Assert.True(report.Runs[0].OutcomeCorrect);
        Assert.True(report.Runs[0].GroundedNoMatch);
    }

    [Fact]
    public void Clarification_requires_expected_missing_fields()
    {
        var correct = Evaluate(("I19", ShopperTestSupport.Clarify("Numaranız?", ShopperMissingField.ShoeSize), Array.Empty<ToolInvocationTrace>()));
        Assert.True(correct.Runs[0].ClarificationCorrect);

        var wrong = Evaluate(("I19", ShopperTestSupport.Clarify("Renk?", ShopperMissingField.Color), Array.Empty<ToolInvocationTrace>()));
        Assert.False(wrong.Runs[0].ClarificationCorrect);
        Assert.Equal(0, wrong.Primary.Single(m => m.Name == "CorrectClarificationRate").Numerator);
    }

    [Fact]
    public void Provider_error_counts_in_attempted_denominators_and_zero_rates_are_null()
    {
        var failed = new ShopperRunRecord("I01", 1, ShopperTestSupport.Failed(ShopperRunStatus.ProviderError, "timeout"));
        var report = ShopperEvaluator.Evaluate(
            TestData.Seed42.Clean, TestData.Seed42.Published, TestData.Seed42.Intents, TestData.Seed42.GroundTruth, [failed]);

        Assert.Equal(0, report.Primary.Single(m => m.Name == "RunCompletionRate").Numerator);
        Assert.Equal(1, report.Primary.Single(m => m.Name == "OutcomeAccuracy").Denominator);
        Assert.Equal(0, report.Primary.Single(m => m.Name == "OutcomeAccuracy").Numerator);
        Assert.Null(report.Primary.Single(m => m.Name == "ConfidentWrongRate").Value);
        Assert.Null(report.Primary.Single(m => m.Name == "UnsupportedClaimRate").Value);
    }

    [Fact]
    public void Misleading_description_candidate_rejected_after_details_is_recorded()
    {
        var sku = "SKU-BP-009";
        var traces = new[]
        {
            ShopperTestSupport.Trace(1, ShopperToolNames.SearchProducts,
                new SearchProductsArguments("su geçirmez", "Backpacks", null, true, null, null, null, null, null, null, 5),
                new SearchProductsData([new(1, sku, "P-BP-009", "t", Category.Backpacks, 1500m, 2, null, null, 0.9)])),
            ShopperTestSupport.Trace(2, ShopperToolNames.GetProductDetails, new SkuListArguments([sku]),
                new ProductDetailsData([Details(sku)])),
        };
        var report = Evaluate(("I02", ShopperTestSupport.NoMatch("su dayanımı yetmiyor"), traces));
        var msd = Assert.Single(report.MisleadingDescription, m => m.Sku == sku);
        Assert.Equal(1, msd.RunsExposed);
        Assert.Equal(1, msd.RunsWithDetailsFetched);
        Assert.Equal(0, msd.ConfirmedRecommendations);
        Assert.Equal(1, msd.RejectedAfterDetails);
    }

    private static ShopperEvaluationReport Evaluate(params (string IntentId, ShopperResult Result, ToolInvocationTrace[] Traces)[] runs) =>
        ShopperEvaluator.Evaluate(
            TestData.Seed42.Clean,
            TestData.Seed42.Published,
            TestData.Seed42.Intents,
            TestData.Seed42.GroundTruth,
            runs.Select((r, i) => new ShopperRunRecord(r.IntentId, i + 1, ShopperTestSupport.Completed(r.Result, r.Traces))).ToList());

    private static ProductDetails Details(string sku)
    {
        var published = TestData.Seed42.Published;
        var variant = published.GetVariant(sku);
        var product = published.GetProduct(variant.ProductId);
        var attrs = new List<ProductAttributeValue> { new(ProductAttributeField.Category, product.Category.ToString()) };
        foreach (var field in Enum.GetValues<ProductAttributeField>())
        {
            if (ProductAttributeFields.KeyOf(field) is { } key
                && (variant.Attributes.Find(key) ?? product.Attributes.Find(key)) is { } value)
            {
                attrs.Add(new(field, value));
            }
        }

        return new ProductDetails(sku, product.ProductId, product.Title, product.Category, published.GetPrice(sku), [.. attrs]);
    }
}
