using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.SourceOfTruth;
using CommerceReadiness.Core.Storage;

namespace CommerceReadiness.Core.Analysis.Rules;

/// <summary>
/// The catalog listed price differs from the authoritative Pricing source. The strike-through compareAtPrice is not
/// a price claim and is ignored; no discount logic beyond the model is inferred.
/// </summary>
public static class PriceConflictRule
{
    public static IEnumerable<Finding> Evaluate(StoreSnapshot published)
    {
        foreach (var listing in published.Listings.OrderBy(l => l.Sku, StringComparer.Ordinal))
        {
            var price = published.Prices.FirstOrDefault(p => p.Sku == listing.Sku);
            if (price is null || listing.ListedPrice == price.PriceTry)
            {
                continue;
            }

            var authoritative = InvariantFormat.Money(price.PriceTry);
            yield return new Finding(
                $"FND-PRC-{listing.Sku}",
                FindingType.PriceConflict,
                FindingSeverities.For(FindingType.PriceConflict),
                published.GetVariant(listing.Sku).ProductId,
                listing.Sku,
                null,
                PublishedFields.ListedPrice,
                "Published price differs from the Pricing source.",
                [
                    new FindingEvidence(DataSource.Catalog, PublishedFields.ListedPrice, InvariantFormat.Money(listing.ListedPrice)),
                    new FindingEvidence(DataSource.Pricing, "priceTry", authoritative),
                ],
                $"Update the published listedPrice to {authoritative} TRY from the Pricing source.");
        }
    }
}
