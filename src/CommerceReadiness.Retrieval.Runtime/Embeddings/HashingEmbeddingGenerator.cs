using System.Text;
using Microsoft.Extensions.AI;

namespace CommerceReadiness.Retrieval.Embeddings;

/// <summary>
/// Deterministic, offline, lexical stand-in for an embedding model: folds Turkish characters, lowercases, and hashes
/// word tokens plus 4-character prefixes (crude Turkish stemming) into a fixed-size L2-normalized vector.
/// It makes ranking behavior testable without a network or API key; it does not measure semantic quality.
/// </summary>
public sealed class HashingEmbeddingGenerator(int dimensions = HashingEmbeddingGenerator.DefaultDimensions)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    public const string ModelId = "hashing-v1";
    public const int DefaultDimensions = 256;

    public int Dimensions { get; } = dimensions;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var embeddings = values.Select(v => new Embedding<float>(Embed(v)) { ModelId = ModelId });
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (var token in Tokens(text))
        {
            var hash = Fnv1a(token);
            var index = (int)(hash % (uint)Dimensions);
            vector[index] += (hash & 0x80000000) == 0 ? 1f : -1f;
        }

        var norm = Math.Sqrt(vector.Sum(x => (double)x * x));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)(vector[i] / norm);
            }
        }

        return vector;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private static IEnumerable<string> Tokens(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            folded.Append(c switch
            {
                'ı' or 'I' or 'İ' or 'i' => 'i',
                'ş' or 'Ş' => 's',
                'ğ' or 'Ğ' => 'g',
                'ü' or 'Ü' => 'u',
                'ö' or 'Ö' => 'o',
                'ç' or 'Ç' => 'c',
                _ => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ',
            });
        }

        foreach (var word in folded.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return word;
            if (word.Length > 4)
            {
                yield return "#" + word[..4];
            }
        }
    }

    private static uint Fnv1a(string token)
    {
        var hash = 2166136261u;
        foreach (var c in token)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return hash;
    }
}
