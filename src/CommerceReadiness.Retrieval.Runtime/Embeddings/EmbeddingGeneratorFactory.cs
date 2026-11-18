using Microsoft.Extensions.AI;
using OpenAI.Embeddings;

namespace CommerceReadiness.Retrieval.Embeddings;

public enum EmbeddingProvider
{
    OpenAI,
    Hashing,
}

/// <summary>Identity of the vectors in an index partition: vectors from different profiles are never compared.</summary>
public sealed record EmbeddingProfile(EmbeddingProvider Provider, string Model, int Dimensions);

public sealed record EmbeddingSetup(IEmbeddingGenerator<string, Embedding<float>> Generator, EmbeddingProfile Profile);

/// <summary>The only place that knows about OpenAI. Everything else depends on IEmbeddingGenerator.</summary>
public static class EmbeddingGeneratorFactory
{
    public static EmbeddingSetup Create(EmbeddingProvider provider, RetrievalSettings settings) => provider switch
    {
        EmbeddingProvider.Hashing => new EmbeddingSetup(
            new HashingEmbeddingGenerator(),
            new EmbeddingProfile(EmbeddingProvider.Hashing, HashingEmbeddingGenerator.ModelId, HashingEmbeddingGenerator.DefaultDimensions)),
        EmbeddingProvider.OpenAI => CreateOpenAi(settings),
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    private static EmbeddingSetup CreateOpenAi(RetrievalSettings settings)
    {
        if (settings.OpenAiApiKey is not { } apiKey)
        {
            throw new InvalidOperationException(
                "OPENAI_API_KEY is not set (environment variable or user-secrets). Use --embeddings hashing for offline runs.");
        }

        var generator = new EmbeddingClient(settings.EmbeddingModel, apiKey).AsIEmbeddingGenerator(settings.EmbeddingDimensions);
        return new EmbeddingSetup(generator, new EmbeddingProfile(EmbeddingProvider.OpenAI, settings.EmbeddingModel, settings.EmbeddingDimensions));
    }
}
