namespace CommerceReadiness.DemoStore.Generation;

/// <summary>
/// Entities already used by a decoy or defect. Each SKU, product and policy cell carries at most one case, so a
/// decoy can never be overwritten by a defect and every case maps to exactly one evaluable unit.
/// </summary>
public sealed class Reservations
{
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public static string Sku(string sku) => $"sku:{sku}";

    public static string Product(string productId) => $"product:{productId}";

    public static string Policy(string cellId) => $"policy:{cellId}";

    public bool IsFree(string key) => !_keys.Contains(key);

    public void Reserve(string key)
    {
        if (!_keys.Add(key))
        {
            throw new InvalidOperationException($"'{key}' is already reserved.");
        }
    }

    /// <summary>Shuffles candidates and takes the first <paramref name="count"/> whose key is still free, reserving them.</summary>
    public List<T> TakeFree<T>(DeterministicRandom rng, IEnumerable<T> candidates, Func<T, string> key, int count, string purpose)
    {
        var taken = new List<T>();
        foreach (var candidate in rng.Shuffle(candidates))
        {
            if (taken.Count == count)
            {
                break;
            }

            if (IsFree(key(candidate)))
            {
                Reserve(key(candidate));
                taken.Add(candidate);
            }
        }

        if (taken.Count < count)
        {
            throw new InvalidOperationException($"Only {taken.Count} of {count} candidates available for {purpose}.");
        }

        return taken;
    }
}
