using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation.Shopping;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Shopper;
using CommerceReadiness.Shopper.Agent;
using CommerceReadiness.Shopper.Tools;

namespace CommerceReadiness.Tests;

/// <summary>
/// General return-policy intent vs published Answer semantics (no intent-id / SKU / category overfitting).
/// </summary>
public class ShopperReturnSemanticsTests
{
    private static readonly ReturnRequirement AllowsConditional =
        new(ReturnQuestion.OpenedReturn, AllowConditional: true);

    private static readonly ReturnRequirement RequiresUnconditional =
        new(ReturnQuestion.OpenedReturn, AllowConditional: false);

    [Fact]
    public void Active_prompt_is_v2_and_states_general_return_semantics()
    {
        Assert.Equal("shopper-prompt-v2", ShopperPrompt.Version);
        Assert.Contains("koşulsuz", ShopperPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("şartsız", ShopperPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("hiçbir koşul olmadan", ShopperPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("Conditional may satisfy", ShopperPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("only Answer=Yes satisfies", ShopperPrompt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("I07", ShopperPrompt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowConditional", ShopperPrompt.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReturnAnswer.Yes, true, true)]
    [InlineData(ReturnAnswer.Yes, false, true)]
    [InlineData(ReturnAnswer.Conditional, true, true)]
    [InlineData(ReturnAnswer.Conditional, false, false)]
    [InlineData(ReturnAnswer.No, true, false)]
    [InlineData(ReturnAnswer.No, false, false)]
    public void Published_answer_vs_allow_conditional_matches_domain_matrix(
        ReturnAnswer answer, bool allowConditional, bool expectedValid)
    {
        var rule = new ReturnRule(Category.Headphones, ReturnQuestion.OpenedReturn, answer, "örnek koşul", 14);
        var requirement = new ReturnRequirement(ReturnQuestion.OpenedReturn, allowConditional);
        Assert.Equal(expectedValid, ConstraintEvaluator.SatisfiesReturn(rule, requirement));
    }

    [Fact]
    public void Generic_return_request_semantics_accept_conditional_when_allow_conditional()
    {
        Assert.True(ConstraintEvaluator.SatisfiesReturn(
            new ReturnRule(Category.Backpacks, ReturnQuestion.OpenedReturn, ReturnAnswer.Conditional, "etiket durmalı", 14),
            AllowsConditional));
    }

    [Fact]
    public void Explicit_unconditional_semantics_reject_conditional()
    {
        Assert.False(ConstraintEvaluator.SatisfiesReturn(
            new ReturnRule(Category.Sneakers, ReturnQuestion.OpenedReturn, ReturnAnswer.Conditional, "giyilmemiş olmalı", 30),
            RequiresUnconditional));
    }

    [Fact]
    public void Yes_satisfies_both_strictness_levels_and_No_satisfies_neither()
    {
        var yes = new ReturnRule(Category.Headphones, ReturnQuestion.OpenedReturn, ReturnAnswer.Yes, null, 14);
        var no = new ReturnRule(Category.Headphones, ReturnQuestion.OpenedReturn, ReturnAnswer.No, null, null);
        Assert.True(ConstraintEvaluator.SatisfiesReturn(yes, AllowsConditional));
        Assert.True(ConstraintEvaluator.SatisfiesReturn(yes, RequiresUnconditional));
        Assert.False(ConstraintEvaluator.SatisfiesReturn(no, AllowsConditional));
        Assert.False(ConstraintEvaluator.SatisfiesReturn(no, RequiresUnconditional));
    }

    [Fact]
    public void Conditional_claim_must_not_be_emitted_as_unconditional_Yes()
    {
        const string sku = "SKU-HP-007";
        const string condition = "Hijyen bandı açılmamış ve ürün hasarsız olmalıdır.";
        var traces = new Dictionary<string, ToolInvocationTrace>(StringComparer.Ordinal)
        {
            ["EVID-001"] = ShopperTestSupport.Trace(
                1, ShopperToolNames.GetReturnPolicy, new ReturnPolicyArguments(sku, "OpenedReturn"),
                new ReturnPolicyData(sku, Category.Headphones, ReturnQuestion.OpenedReturn, ReturnAnswer.Conditional, 14, condition)),
        };

        var conditionalClaim = ShopperTestSupport.Return(
            ReturnQuestion.OpenedReturn, ClaimFormat.Return(ReturnAnswer.Conditional, 14), "EVID-001");
        var yesClaim = ShopperTestSupport.Return(
            ReturnQuestion.OpenedReturn, ClaimFormat.Return(ReturnAnswer.Yes, 14), "EVID-001");

        Assert.Equal(ClaimVerdict.Supported, ClaimEvidence.Verify(sku, conditionalClaim, traces));
        Assert.Equal(ClaimVerdict.Contradicted, ClaimEvidence.Verify(sku, yesClaim, traces));
        Assert.DoesNotContain("Yes", conditionalClaim.Value, StringComparison.Ordinal);
        Assert.StartsWith(nameof(ReturnAnswer.Conditional), conditionalClaim.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Renderer_surfaces_published_condition_for_conditional_return_claims()
    {
        const string sku = "SKU-HP-007";
        const string condition = "Hijyen bandı açılmamış ve ürün hasarsız olmalıdır.";
        var trace = ShopperTestSupport.Trace(
            1, ShopperToolNames.GetReturnPolicy, new ReturnPolicyArguments(sku, "OpenedReturn"),
            new ReturnPolicyData(sku, Category.Headphones, ReturnQuestion.OpenedReturn, ReturnAnswer.Conditional, 14, condition));
        var result = ShopperTestSupport.Recommended(ShopperTestSupport.Rec(
            sku, RecommendationConfidence.Confirmed,
            ShopperTestSupport.Return(ReturnQuestion.OpenedReturn, "Conditional:14", "EVID-001")));
        var rendered = ShopperResultRenderer.Render(ShopperTestSupport.Completed(result, trace));

        Assert.Contains("Conditional:14", rendered, StringComparison.Ordinal);
        Assert.Contains(condition, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("İade (OpenedReturn): Yes", rendered, StringComparison.Ordinal);
    }
}
