using System.Text;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Shopping;

namespace CommerceReadiness.Shopper;

/// <summary>Deterministic Turkish text from a ShopperRun. No second model call can add unsupported facts.</summary>
public static class ShopperResultRenderer
{
    public static string Render(ShopperRun run)
    {
        var text = new StringBuilder();
        if (run.Status != ShopperRunStatus.Completed || run.Result is not { } result)
        {
            text.AppendLine($"Sonuç üretilemedi ({run.Status}).");
            if (run.StatusDetail is { } detail)
            {
                text.AppendLine($"  Ayrıntı: {detail}");
            }

            return text.ToString();
        }

        switch (result.Outcome)
        {
            case ShopperOutcome.NeedsClarification:
                text.AppendLine("Devam etmeden önce bir bilgiye ihtiyacım var:");
                text.AppendLine($"  {result.Clarification!.QuestionTr}");
                text.AppendLine($"  Eksik bilgi: {string.Join(", ", result.Clarification.MissingFields.Select(MissingFieldText))}");
                break;
            case ShopperOutcome.NoMatch:
                text.AppendLine("Tüm şartlarınızı karşılayan bir ürün bulamadım.");
                text.AppendLine($"  Neden: {result.NoMatchReason}");
                break;
            default:
                for (var i = 0; i < result.Recommendations.Length; i++)
                {
                    var recommendation = result.Recommendations[i];
                    var label = i == 0 ? "Önerim" : "Alternatif";
                    var confidence = recommendation.Confidence == RecommendationConfidence.Confirmed ? "doğrulandı" : "doğrulanamayan bilgi var";
                    text.AppendLine($"{label}: {recommendation.Sku} ({confidence})");
                    foreach (var claim in recommendation.Claims)
                    {
                        text.AppendLine($"  - {ClaimText(claim)} [{string.Join(", ", claim.EvidenceCallIds)}]");
                    }
                }

                break;
        }

        return text.ToString();
    }

    public static string RenderTrace(ShopperRun run)
    {
        var text = new StringBuilder();
        foreach (var call in run.Trace)
        {
            var status = call.Ok ? "ok" : call.ErrorCode;
            text.AppendLine($"  {call.EvidenceCallId} {call.ToolName,-22} {status,-16} {call.Arguments.GetRawText()}");
        }

        return text.ToString();
    }

    private static string ClaimText(GroundedClaim claim) => claim.Type switch
    {
        ClaimType.ProductAttribute => $"{claim.AttributeField}: {claim.Value}",
        ClaimType.Price => $"Fiyat: {claim.Value} TL",
        ClaimType.Availability => claim.Value == ClaimFormat.InStock ? "Stokta" : "Stokta değil",
        ClaimType.Delivery => $"{CityText(claim.City)} teslimat: {claim.Value}",
        ClaimType.ReturnPolicy => $"İade ({claim.ReturnQuestion}): {claim.Value}",
        ClaimType.PaymentOption => $"En fazla {claim.Value} taksit",
        _ => claim.Value,
    };

    private static string CityText(City? city) => city switch
    {
        City.Istanbul => "İstanbul",
        City.Izmir => "İzmir",
        City.Ankara => "Ankara",
        _ => "?",
    };

    private static string MissingFieldText(ShopperMissingField field) => field switch
    {
        ShopperMissingField.Category => "ürün kategorisi",
        ShopperMissingField.ShoeSize => "ayakkabı numarası",
        ShopperMissingField.DeliveryCity => "teslimat şehri",
        ShopperMissingField.DeliveryDeadline => "teslimat süresi",
        ShopperMissingField.MaxPrice => "bütçe",
        ShopperMissingField.Color => "renk",
        ShopperMissingField.ProductType => "ürün tipi",
        ShopperMissingField.TargetGroup => "hedef grup",
        _ => field.ToString(),
    };
}
