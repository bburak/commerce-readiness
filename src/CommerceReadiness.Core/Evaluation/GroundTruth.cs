using CommerceReadiness.Core.Catalog;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.SourceOfTruth;

namespace CommerceReadiness.Core.Evaluation;

public enum DefectType
{
    /// <summary>A required structured field is missing.</summary>
    MissingRequiredAttribute,

    /// <summary>Published stock status conflicts with authoritative inventory.</summary>
    InventoryConflict,

    /// <summary>Published price differs from the authoritative Pricing source.</summary>
    PriceConflict,

    /// <summary>Description text asserts something that conflicts with authoritative structured truth.</summary>
    MisleadingDescription,

    /// <summary>A category x return-question cell has no explicit Yes/No/Conditional answer.</summary>
    AmbiguousReturnRule,

    /// <summary>Published delivery promise conflicts with the deterministic delivery calculation.</summary>
    DeliveryConflict,
}

/// <param name="DefectId">Stable for a given seed; derived from type and affected entity.</param>
/// <param name="Field">
/// The affected field: an attribute key for MissingRequiredAttribute and MisleadingDescription,
/// otherwise stockStatus, listedPrice, deliveryPromiseIstanbul or answer.
/// </param>
/// <param name="Source">The authoritative source that decides the correct value.</param>
public sealed record InjectedDefect(
    string DefectId,
    DefectType Type,
    string? ProductId,
    string? Sku,
    string? Policy,
    string Field,
    string? PublishedValue,
    string AuthoritativeValue,
    DataSource Source,
    string ExpectedFix)
{
    /// <summary>Evaluation units this defect is expected to make not pass (see <see cref="EvaluationUnits.ExpectedImpacts"/>).</summary>
    public ImmutableArray<string> ExpectedImpacts { get; init; } = [];
}

/// <summary>
/// A published case that looks like a defect but is consistent with authoritative truth. Analyzers that flag
/// these lose precision.
/// </summary>
public sealed record DecoyCase(
    string DecoyId,
    string Pattern,
    DefectType NearMissOf,
    string? ProductId,
    string? Sku,
    string? Policy,
    string Field,
    string PublishedValue,
    string AuthoritativeValue,
    DataSource Source,
    string Explanation);

/// <summary>
/// A fact asserted by a phrase in a product description of the given store.
/// <see cref="SupportsAuthoritativeValue"/> is true only when <see cref="AssertedValue"/> equals the
/// clean-store structured value. Later AI extraction can be evaluated against these facts.
/// </summary>
/// <param name="Sku">
/// Null for a product-scoped fact ("Malzeme: Deri."), which applies to every SKU of the product but can only
/// evidence product-level attributes. Set for a SKU-scoped fact ("43 numara siyah seçenek mevcut."), which applies
/// to that SKU only. Variant-level evidence must be resolved to a SKU; unresolved it is never evidence.
/// </param>
public sealed record SemanticFact(
    StoreKind Store,
    string ProductId,
    string? Sku,
    string AttributeKey,
    string AssertedValue,
    string Phrase,
    bool SupportsAuthoritativeValue);

public sealed record GroundTruth(
    string StoreVersion,
    int Seed,
    DateTimeOffset SimulationClock,
    ImmutableArray<FieldOwner> SourceOfTruth,
    ImmutableArray<InjectedDefect> Defects,
    ImmutableArray<DecoyCase> Decoys,
    ImmutableArray<SemanticFact> SemanticFacts);

/// <summary>Scope and support rules for text evidence. All metrics go through these two methods.</summary>
public static class SemanticSupport
{
    /// <summary>
    /// Whether a fact is evidence for one SKU's requirement. Product-scoped facts apply to every SKU of the product,
    /// but only for product-level attributes; SKU-scoped facts apply to their own SKU only. Evidence therefore never
    /// spreads from one variant to its siblings.
    /// </summary>
    public static bool AppliesTo(SemanticFact fact, string productId, string sku, AttributeRequirement requirement) =>
        fact.ProductId == productId
        && fact.AttributeKey == requirement.Key
        && (fact.Sku is null ? requirement.Level == AttributeLevel.Product : fact.Sku == sku);

    /// <summary>
    /// Text evidence counts only when it matches the authoritative clean-store value at the fact's scope:
    /// the product's attribute for product-scoped facts, the SKU's (then its product's) attribute for SKU-scoped ones.
    /// </summary>
    public static bool Supports(SemanticFact fact, StoreSnapshot clean)
    {
        if (clean.Kind != StoreKind.Clean)
        {
            throw new ArgumentException("Semantic support is judged against the clean store.", nameof(clean));
        }

        var product = clean.Products.FirstOrDefault(p => p.ProductId == fact.ProductId);
        if (product is null)
        {
            return false;
        }

        if (fact.Sku is null)
        {
            return product.Attributes.Find(fact.AttributeKey) == fact.AssertedValue;
        }

        var variant = clean.Variants.FirstOrDefault(v => v.Sku == fact.Sku);
        return variant is not null
            && variant.ProductId == fact.ProductId
            && (variant.Attributes.Find(fact.AttributeKey) ?? product.Attributes.Find(fact.AttributeKey)) == fact.AssertedValue;
    }
}
