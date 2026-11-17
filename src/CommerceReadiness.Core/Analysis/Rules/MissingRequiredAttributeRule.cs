using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.SourceOfTruth;

namespace CommerceReadiness.Core.Analysis.Rules;

/// <summary>
/// A required structured attribute is absent. Mirrors Catalog Completeness semantics (description text never
/// satisfies a requirement) but reports one finding per root cause: a missing product-level attribute is one finding
/// for the product, a missing variant-level attribute is one finding per SKU.
/// </summary>
public static class MissingRequiredAttributeRule
{
    public static IEnumerable<Finding> Evaluate(StoreSnapshot published)
    {
        foreach (var product in published.Products.OrderBy(p => p.ProductId, StringComparer.Ordinal))
        {
            foreach (var requirement in CategoryRequirements.For(product.Category).Where(r => r.Level == AttributeLevel.Product))
            {
                if (IsMissing(product.Attributes.Find(requirement.Key)))
                {
                    yield return Create(product, null, requirement.Key);
                }
            }
        }

        foreach (var variant in published.Variants.OrderBy(v => v.Sku, StringComparer.Ordinal))
        {
            var product = published.GetProduct(variant.ProductId);
            foreach (var requirement in CategoryRequirements.For(product.Category).Where(r => r.Level == AttributeLevel.Variant))
            {
                if (IsMissing(variant.Attributes.Find(requirement.Key)))
                {
                    yield return Create(product, variant.Sku, requirement.Key);
                }
            }
        }
    }

    private static bool IsMissing(string? value) => string.IsNullOrWhiteSpace(value);

    private static Finding Create(Product product, string? sku, string key)
    {
        var target = sku ?? product.ProductId;
        return new Finding(
            $"FND-MRA-{target}-{key}",
            FindingType.MissingRequiredAttribute,
            FindingSeverities.For(FindingType.MissingRequiredAttribute),
            product.ProductId,
            sku,
            null,
            key,
            $"Required structured attribute '{key}' is missing for {product.Category} {target}.",
            [
                new FindingEvidence(DataSource.Catalog, key, null),
                new FindingEvidence(DataSource.Catalog, "categoryRequirement", $"{product.Category} requires {key}"),
            ],
            $"Populate the structured '{key}' attribute for {target}; description text does not satisfy the requirement.");
    }
}
