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
/// A fact asserted by a templated phrase in a product description of the given store.
/// <see cref="SupportsAuthoritativeValue"/> is true only when <see cref="AssertedValue"/> equals the
/// clean-store structured value. Later AI extraction can be evaluated against these facts.
/// </summary>
public sealed record SemanticFact(
    StoreKind Store,
    string ProductId,
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

public static class SemanticSupport
{
    /// <summary>Text evidence counts only when it matches the authoritative clean-store value.</summary>
    public static bool Supports(SemanticFact fact, StoreSnapshot clean)
    {
        if (clean.Kind != StoreKind.Clean)
        {
            throw new ArgumentException("Semantic support is judged against the clean store.", nameof(clean));
        }

        var product = clean.Products.FirstOrDefault(p => p.ProductId == fact.ProductId);
        return product is not null && product.Attributes.Find(fact.AttributeKey) == fact.AssertedValue;
    }
}
