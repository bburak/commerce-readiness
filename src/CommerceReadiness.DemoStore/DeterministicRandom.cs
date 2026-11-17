namespace CommerceReadiness.DemoStore;

/// <summary>
/// SplitMix64 generator. Used instead of System.Random because seeded System.Random output is not
/// contractually stable across .NET versions.
/// </summary>
public sealed class DeterministicRandom
{
    private readonly ulong _origin;
    private ulong _state;

    public DeterministicRandom(int seed)
        : this(unchecked((ulong)seed * 0x9E3779B97F4A7C15UL) ^ 0xD1B54A32D192ED03UL)
    {
    }

    private DeterministicRandom(ulong state)
    {
        _origin = state;
        _state = state;
    }

    /// <summary>
    /// An independent stream derived from the original seed and a label, unaffected by how much of this stream
    /// was consumed. Lets one generation stage change without reshuffling the others.
    /// </summary>
    public DeterministicRandom Fork(string label)
    {
        // FNV-1a; string.GetHashCode is randomized per process.
        var hash = 14695981039346656037UL;
        foreach (var c in label)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }

        return new DeterministicRandom(_origin ^ hash);
    }

    public ulong NextUInt64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public int Next(int maxExclusive) => (int)(NextUInt64() % (ulong)maxExclusive);

    public int Next(int minInclusive, int maxExclusive) => minInclusive + Next(maxExclusive - minInclusive);

    public T Pick<T>(IReadOnlyList<T> items) =>
        items.Count == 0 ? throw new InvalidOperationException("Cannot pick from an empty list.") : items[Next(items.Count)];

    public List<T> Shuffle<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    public List<T> Sample<T>(IEnumerable<T> items, int count) => Shuffle(items).Take(count).ToList();

    /// <summary>A shuffled list containing each value exactly the given number of times.</summary>
    public List<T> Quota<T>(params (T Value, int Count)[] entries) =>
        Shuffle(entries.SelectMany(e => Enumerable.Repeat(e.Value, e.Count)));
}
