namespace CommerceReadiness.Core.Domain;

public static class Attributes
{
    public static ImmutableSortedDictionary<string, string> Empty { get; } =
        ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);

    public static ImmutableSortedDictionary<string, string> Create(params (string Key, string Value)[] entries) =>
        Empty.AddRange(entries.Select(e => KeyValuePair.Create(e.Key, e.Value)));

    public static ImmutableSortedDictionary<string, string> Create(IEnumerable<KeyValuePair<string, string>> entries) =>
        Empty.AddRange(entries);

    public static string? Find(this ImmutableSortedDictionary<string, string> attributes, string key) =>
        attributes.TryGetValue(key, out var value) ? value : null;
}

/// <summary>catalog.csv spells stock status in upper snake case.</summary>
public static class StockStatusText
{
    public const string InStock = "IN_STOCK";
    public const string OutOfStock = "OUT_OF_STOCK";

    public static string ToCatalogText(this StockStatus status) => status == StockStatus.InStock ? InStock : OutOfStock;

    public static StockStatus Parse(string text) => text switch
    {
        InStock => StockStatus.InStock,
        OutOfStock => StockStatus.OutOfStock,
        _ => throw new FormatException($"Unknown stock status '{text}'."),
    };
}

/// <summary>Culture-independent formatting for everything that is serialized or compared.</summary>
public static class InvariantFormat
{
    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Bool(bool value) => value ? "true" : "false";

    public static decimal ParseDecimal(string text) => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    public static bool TryParseDecimal(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    public static DateOnly ParseDate(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
