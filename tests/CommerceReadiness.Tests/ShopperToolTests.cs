using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Shopper.Tools;

namespace CommerceReadiness.Tests;

public class ShopperToolTests
{
    [Fact]
    public async Task Search_products_always_uses_hybrid_and_propagates_filters()
    {
        await using var host = await ShopperTestSupport.OpenMemoryAsync();
        var result = await host.Tools.SearchProductsAsync(new SearchProductsArguments(
            "yağmurda laptop çantası", nameof(Category.Backpacks), 4000m, true, null, null, 15.6m, null, null, null, 5));

        Assert.True(result.Ok);
        Assert.NotEmpty(result.Data!.Candidates);
        Assert.All(result.Data.Candidates, c =>
        {
            Assert.Equal(Category.Backpacks, c.Category);
            Assert.True(c.PriceTry <= 4000m);
            Assert.True(c.AvailableQuantity > 0);
            Assert.NotNull(c.SimilarityScore);
        });
    }

    [Fact]
    public async Task Product_details_expose_structured_attributes_not_description()
    {
        await using var host = await ShopperTestSupport.OpenMemoryAsync();
        var result = host.Tools.GetProductDetails(new(["SKU-BP-009"]));

        Assert.True(result.Ok);
        var product = Assert.Single(result.Data!.Products);
        Assert.Equal("SKU-BP-009", product.Sku);
        Assert.Contains(product.Attributes, a => a.Field == ProductAttributeField.WaterResistance && a.Value == nameof(WaterResistance.WaterRepellent));
        Assert.DoesNotContain(product.Attributes, a => a.Value.Contains("sağanak", StringComparison.Ordinal));
        Assert.DoesNotContain(JsonSerializerPreview(product), "Tamamen su geçirmez", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inventory_reads_authoritative_published_quantity()
    {
        await using var host = await ShopperTestSupport.OpenMemoryAsync();
        var sku = TestData.Seed42.Published.Inventory.First(i => i.Available > 0).Sku;
        var result = host.Tools.GetInventory(new([sku]));

        Assert.True(result.Ok);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(TestData.Seed42.Published.GetInventory(sku).Available, item.AvailableQuantity);
        Assert.Equal(TestData.Seed42.Published.GetInventory(sku).WarehouseId, item.WarehouseId);
    }

    [Fact]
    public async Task Delivery_estimate_matches_delivery_calculator_for_ankara()
    {
        await using var host = await ShopperTestSupport.OpenMemoryAsync();
        var sku = "SKU-BP-002";
        var result = host.Tools.GetDeliveryEstimate(new(sku, nameof(City.Ankara), 1));
        var expected = DeliveryCalculator.EstimateDelivery(TestData.Seed42.Published, sku, City.Ankara, host.Tools.AsOf);

        Assert.True(result.Ok);
        Assert.Equal(expected, result.Data!.EstimatedDeliveryDate);
        Assert.Equal(DeliveryCalculator.Deadline(host.Tools.AsOf, 1), result.Data.Deadline);
        Assert.False(result.Data.MeetsDeadline);
    }

    [Fact]
    public async Task Return_policy_is_resolved_from_sku_category()
    {
        await using var host = await ShopperTestSupport.OpenMemoryAsync();
        var result = host.Tools.GetReturnPolicy(new("SKU-SE-009", nameof(ReturnQuestion.OpenedReturn)));

        Assert.True(result.Ok);
        Assert.Equal(Category.SmallElectronics, result.Data!.Category);
        // Published AmbiguousReturn defect turns OpenedReturn into Unspecified; tools read published, not clean.
        Assert.Equal(ReturnAnswer.Unspecified, result.Data.Answer);
    }

    [Fact]
    public async Task Payment_options_are_resolved_from_sku_price()
    {
        await using var host = await ShopperTestSupport.OpenMemoryAsync();
        var sku = "SKU-HP-001";
        var result = host.Tools.GetPaymentOptions(new(sku));
        var expected = Core.Payments.InstallmentCalculator.MaxInstallments(
            TestData.Seed42.Published.InstallmentRules,
            TestData.Seed42.Published.ProductOf(sku).Category,
            TestData.Seed42.Published.GetPrice(sku));

        Assert.True(result.Ok);
        Assert.Equal(expected, result.Data!.MaxInstallments);
        Assert.Equal(TestData.Seed42.Published.GetPrice(sku), result.Data.PriceTry);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Search_rejects_invalid_top(int top)
    {
        await using var host = await ShopperTestSupport.OpenMemoryAsync();
        var result = await host.Tools.SearchProductsAsync(new("query", null, null, null, null, null, null, null, null, null, top));
        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.InvalidArgument, result.ErrorCode);
    }

    [Fact]
    public async Task Unknown_sku_returns_typed_error()
    {
        await using var host = await ShopperTestSupport.OpenMemoryAsync();
        var result = host.Tools.GetProductDetails(new(["SKU-NOPE"]));
        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.UnknownSku, result.ErrorCode);
    }

    [Fact]
    public void Tool_set_is_exactly_the_six_read_only_names()
    {
        Assert.Equal(
            new[] { "search_products", "get_product_details", "get_inventory", "get_delivery_estimate", "get_return_policy", "get_payment_options" },
            ShopperToolNames.All.ToArray());
    }

    private static string JsonSerializerPreview(object value) =>
        System.Text.Json.JsonSerializer.Serialize(value, Shopper.ShopperJson.Options);
}
