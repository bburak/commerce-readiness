using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Shopping;

namespace CommerceReadiness.Tests;

public class ShopperResultValidatorTests
{
    [Fact]
    public void Recommended_requires_confirmed_primary_and_unique_skus()
    {
        var ok = ShopperTestSupport.Recommended(
            ShopperTestSupport.Rec("SKU-A", RecommendationConfidence.Confirmed, ShopperTestSupport.Stock(ClaimFormat.InStock, "EVID-001")));
        Assert.Empty(ShopperResultValidator.Validate(ok));

        var uncertainPrimary = ShopperTestSupport.Recommended(
            ShopperTestSupport.Rec("SKU-A", RecommendationConfidence.Uncertain, ShopperTestSupport.Stock(ClaimFormat.InStock, "EVID-001")));
        Assert.Contains(ShopperResultValidator.Validate(uncertainPrimary), e => e.Contains("Confirmed", StringComparison.Ordinal));

        var duplicates = ShopperTestSupport.Recommended(
            ShopperTestSupport.Rec("SKU-A", RecommendationConfidence.Confirmed),
            ShopperTestSupport.Rec("SKU-A", RecommendationConfidence.Uncertain));
        Assert.Contains(ShopperResultValidator.Validate(duplicates), e => e.Contains("unique", StringComparison.Ordinal));
    }

    [Fact]
    public void NoMatch_and_clarification_invariants()
    {
        Assert.Empty(ShopperResultValidator.Validate(ShopperTestSupport.NoMatch("hiçbiri uymuyor")));
        Assert.Contains(ShopperResultValidator.Validate(new ShopperResult(1, ShopperOutcome.NoMatch, null, [], null, [])),
            e => e.Contains("noMatchReason", StringComparison.Ordinal));

        Assert.Empty(ShopperResultValidator.Validate(ShopperTestSupport.Clarify("Numaranız nedir?", ShopperMissingField.ShoeSize)));
        Assert.Contains(
            ShopperResultValidator.Validate(new ShopperResult(
                1, ShopperOutcome.NeedsClarification, new Clarification("?", []), [], null, [])),
            e => e.Contains("missing field", StringComparison.Ordinal));
    }

    [Fact]
    public void Claim_qualifiers_must_match_type()
    {
        var bad = ShopperTestSupport.Recommended(ShopperTestSupport.Rec(
            "SKU-A",
            RecommendationConfidence.Confirmed,
            new GroundedClaim(ClaimType.Delivery, ProductAttributeField.Color, null, null, "2026-11-18", ["EVID-001"])));
        Assert.Contains(ShopperResultValidator.Validate(bad), e => e.Contains("qualifiers", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ClaimType.Price, "3499.00", true)]
    [InlineData(ClaimType.Price, "3499", false)]
    [InlineData(ClaimType.Availability, "InStock", true)]
    [InlineData(ClaimType.Availability, "true", false)]
    [InlineData(ClaimType.Delivery, "2026-11-18", true)]
    [InlineData(ClaimType.PaymentOption, "6", true)]
    [InlineData(ClaimType.ReturnPolicy, "Conditional:14", true)]
    [InlineData(ClaimType.ReturnPolicy, "Maybe", false)]
    public void Claim_format_accepts_only_canonical_values(ClaimType type, string value, bool wellFormed)
    {
        var claim = type switch
        {
            ClaimType.Delivery => ShopperTestSupport.Delivery(City.Ankara, value, "EVID-001"),
            ClaimType.ReturnPolicy => ShopperTestSupport.Return(ReturnQuestion.OpenedReturn, value, "EVID-001"),
            ClaimType.Price => ShopperTestSupport.Price(value, "EVID-001"),
            ClaimType.Availability => ShopperTestSupport.Stock(value, "EVID-001"),
            _ => ShopperTestSupport.Payment(value, "EVID-001"),
        };
        Assert.Equal(wellFormed, ClaimFormat.IsWellFormed(claim));
    }
}
