using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Shopper.Agent;
using CommerceReadiness.Shopper.Tools;
using Microsoft.Extensions.AI;

namespace CommerceReadiness.Tests;

public class ShopperAgentTests
{
    [Fact]
    public async Task Agent_issues_ordered_evidence_ids_and_returns_structured_result()
    {
        await using var host = await ShopperTestSupport.OpenMemoryAsync();
        var sku = "SKU-BP-001";
        using var chat = ShopperChatPipeline.Wrap(new ScriptedChatClient(
            ScriptedChatClient.FunctionCall(ShopperToolNames.SearchProducts, Args(("query", "sırt çantası"), ("category", "Backpacks"), ("top", 5))),
            ScriptedChatClient.FunctionCall(ShopperToolNames.GetInventory, Args(("skus", new[] { sku })), "call-2"),
            ScriptedChatClient.Final(ShopperTestSupport.Recommended(
                ShopperTestSupport.Rec(sku, RecommendationConfidence.Confirmed,
                    ShopperTestSupport.Stock(ClaimFormat.InStock, "EVID-002"),
                    ShopperTestSupport.Attr(ProductAttributeField.Category, nameof(Category.Backpacks), "EVID-001"))))),
            ShopperTestSupport.Settings);

        var run = await ShopperAgent.RunAsync(chat, host.Tools, ShopperTestSupport.Settings, "sırt çantası istiyorum");

        Assert.Equal(ShopperRunStatus.Completed, run.Status);
        Assert.Equal(["EVID-001", "EVID-002"], run.Trace.Select(t => t.EvidenceCallId));
        Assert.Equal([ShopperToolNames.SearchProducts, ShopperToolNames.GetInventory], run.Trace.Select(t => t.ToolName));
        Assert.Equal(ShopperOutcome.Recommended, run.Result!.Outcome);
        Assert.Equal(sku, run.Result.Recommendations[0].Sku);
    }

    [Fact]
    public async Task Tool_budget_stops_the_run()
    {
        await using var host = await ShopperTestSupport.OpenMemoryAsync();
        var settings = ShopperTestSupport.Settings with { MaxToolCalls = 1, MaxToolIterations = 8 };
        using var chat = ShopperChatPipeline.Wrap(new ScriptedChatClient(
            ScriptedChatClient.FunctionCall(ShopperToolNames.SearchProducts, Args(("query", "çanta"))),
            ScriptedChatClient.FunctionCall(ShopperToolNames.GetInventory, Args(("skus", new[] { "SKU-BP-001" })), "call-2"),
            ScriptedChatClient.Final(ShopperTestSupport.NoMatch("none"))),
            settings);

        var run = await ShopperAgent.RunAsync(chat, host.Tools, settings, "çanta");

        Assert.Equal(ShopperRunStatus.ToolBudgetExceeded, run.Status);
        Assert.Single(run.Trace);
    }

    [Fact]
    public async Task Typed_validation_error_is_returned_to_the_model_without_failing_the_run()
    {
        await using var host = await ShopperTestSupport.OpenMemoryAsync();
        using var chat = ShopperChatPipeline.Wrap(new ScriptedChatClient(
            ScriptedChatClient.FunctionCall(ShopperToolNames.GetProductDetails, Args(("skus", new[] { "SKU-NOPE" }))),
            ScriptedChatClient.Final(ShopperTestSupport.Clarify("Hangi ürün?", ShopperMissingField.Category))),
            ShopperTestSupport.Settings);

        var run = await ShopperAgent.RunAsync(chat, host.Tools, ShopperTestSupport.Settings, "bir şey");

        Assert.Equal(ShopperRunStatus.Completed, run.Status);
        Assert.False(Assert.Single(run.Trace).Ok);
        Assert.Equal(ToolErrorCodes.UnknownSku, run.Trace[0].ErrorCode);
        Assert.Equal(ShopperOutcome.NeedsClarification, run.Result!.Outcome);
    }

    [Fact]
    public async Task Invalid_final_result_is_reported()
    {
        await using var host = await ShopperTestSupport.OpenMemoryAsync();
        using var chat = ShopperChatPipeline.Wrap(
            new ScriptedChatClient(ScriptedChatClient.Final(ShopperTestSupport.Recommended(
                ShopperTestSupport.Rec("SKU-A", RecommendationConfidence.Uncertain)))),
            ShopperTestSupport.Settings);

        var run = await ShopperAgent.RunAsync(chat, host.Tools, ShopperTestSupport.Settings, "x");

        Assert.Equal(ShopperRunStatus.InvalidResult, run.Status);
        Assert.Contains("Confirmed", run.StatusDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unparsable_final_json_is_invalid_result()
    {
        await using var host = await ShopperTestSupport.OpenMemoryAsync();
        using var chat = ShopperChatPipeline.Wrap(new ScriptedChatClient(ScriptedChatClient.Text("not-json")), ShopperTestSupport.Settings);

        var run = await ShopperAgent.RunAsync(chat, host.Tools, ShopperTestSupport.Settings, "x");

        Assert.Equal(ShopperRunStatus.InvalidResult, run.Status);
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
}
