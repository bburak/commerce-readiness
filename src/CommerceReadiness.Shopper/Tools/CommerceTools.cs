using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Payments;
using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Core.Storage;
using CommerceReadiness.Core.Time;
using CommerceReadiness.Retrieval;
using CommerceReadiness.Retrieval.Embeddings;
using CommerceReadiness.Retrieval.Index;

namespace CommerceReadiness.Shopper.Tools;

public sealed record ToolOutcome<T>(T? Data, string? ErrorCode, string? ErrorMessage)
{
    public bool Ok => ErrorCode is null;

    public static ToolOutcome<T> Success(T data) => new(data, null, null);

    public static ToolOutcome<T> Fail(string code, string message) => new(default, code, message);
}

/// <summary>
/// The six read-only shopper tools over the published store. Arguments come from the model and are validated here;
/// entity-derived facts (category, price, warehouse, policy) are always resolved from the SKU, never taken from the
/// model. search_products always uses Milestone 2 Hybrid retrieval.
/// </summary>
public sealed class CommerceTools
{
    public const int DefaultTop = 5;
    public const int MaxTop = 10;
    public const int MaxBatch = 10;
    public const int MaxQueryLength = 500;
    public const int MaxWithinDays = 30;

    private readonly StoreSnapshot _store;
    private readonly EmbeddingSetup _embeddings;
    private readonly IProductSearchIndex _index;
    private readonly ImmutableHashSet<string> _skus;
    private readonly ImmutableArray<string> _colors;

    public CommerceTools(LoadedStore published, EmbeddingSetup embeddings, IProductSearchIndex index)
    {
        if (published.Store.Kind == StoreKind.Clean)
        {
            throw new ArgumentException("Shopper tools only read the observable published store.", nameof(published));
        }

        _store = published.Store;
        AsOf = published.Manifest.AsOf;
        _embeddings = embeddings;
        _index = index;
        _skus = [.. _store.Variants.Select(v => v.Sku)];
        _colors = [.. _store.Variants.Select(v => v.Attributes.Find(Core.Catalog.AttributeKeys.Color))
            .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    public DateTimeOffset AsOf { get; }

    public string StoreVersion => _store.StoreVersion;

    public async Task<ToolOutcome<SearchProductsData>> SearchProductsAsync(SearchProductsArguments args, CancellationToken cancellationToken = default)
    {
        var top = args.Top ?? DefaultTop;
        if (string.IsNullOrWhiteSpace(args.Query) || args.Query.Length > MaxQueryLength)
        {
            return Invalid<SearchProductsData>($"query is required and at most {MaxQueryLength} characters");
        }

        if (top is < 1 or > MaxTop)
        {
            return Invalid<SearchProductsData>($"top must be between 1 and {MaxTop}");
        }

        if (!TryParseOptionalEnum(args.Category, out Category? category))
        {
            return Invalid<SearchProductsData>($"unsupported category '{args.Category}'; use {string.Join(", ", Enum.GetNames<Category>())}");
        }

        string? color = null;
        if (args.Color is { } requestedColor)
        {
            color = _colors.FirstOrDefault(c => string.Equals(c, requestedColor.Trim(), StringComparison.OrdinalIgnoreCase));
            if (color is null)
            {
                return Invalid<SearchProductsData>($"unsupported color '{requestedColor}'; use {string.Join(", ", _colors)}");
            }
        }

        if (args.MaxPriceTry is <= 0 || args.MinimumLaptopSizeIn is <= 0 || args.MinimumCapacityLiters is <= 0
            || args.MinimumBatteryHours is <= 0 || args.MinimumWarrantyMonths is <= 0 || args.ShoeSize is <= 0)
        {
            return Invalid<SearchProductsData>("prices, sizes and minimum thresholds must be positive");
        }

        var filters = new StructuredFilters
        {
            Category = category,
            MaxPriceTry = args.MaxPriceTry,
            InStockOnly = args.InStockOnly ?? true,
            ShoeSize = args.ShoeSize,
            Color = color,
            MinimumLaptopSizeIn = args.MinimumLaptopSizeIn,
            MinimumCapacityLiters = args.MinimumCapacityLiters,
            MinimumBatteryHours = args.MinimumBatteryHours,
            MinimumWarrantyMonths = args.MinimumWarrantyMonths,
        };
        var request = new SearchRequest(_store.StoreVersion, args.Query.Trim(), filters, SearchMode.Hybrid, top);
        var results = await ProductSearchService.SearchAsync(request, _embeddings, _index, cancellationToken);
        return ToolOutcome<SearchProductsData>.Success(new([.. results.Select(r => new ProductCandidate(
            r.Rank, r.Sku, r.ProductId, r.Title, r.Category, r.PriceTry, r.AvailableQuantity, r.ShoeSize, r.Color, r.SimilarityScore))]));
    }

    public ToolOutcome<ProductDetailsData> GetProductDetails(SkuListArguments args)
    {
        if (ValidateSkus(args.Skus) is { } error)
        {
            return ToolOutcome<ProductDetailsData>.Fail(error.Code, error.Message);
        }

        return ToolOutcome<ProductDetailsData>.Success(new([.. args.Skus.Select(Details)]));
    }

    public ToolOutcome<InventoryData> GetInventory(SkuListArguments args)
    {
        if (ValidateSkus(args.Skus) is { } error)
        {
            return ToolOutcome<InventoryData>.Fail(error.Code, error.Message);
        }

        return ToolOutcome<InventoryData>.Success(new([.. args.Skus.Select(sku =>
        {
            var record = _store.GetInventory(sku);
            return new InventoryItem(sku, record.Available, record.WarehouseId);
        })]));
    }

    public ToolOutcome<DeliveryEstimateData> GetDeliveryEstimate(DeliveryEstimateArguments args)
    {
        if (ValidateSku(args.Sku) is { } error)
        {
            return ToolOutcome<DeliveryEstimateData>.Fail(error.Code, error.Message);
        }

        if (ParseCity(args.City) is not { } city)
        {
            return Invalid<DeliveryEstimateData>($"unsupported city '{args.City}'; use {string.Join(", ", Enum.GetNames<City>())}");
        }

        if (args.WithinDays is < 0 or > MaxWithinDays)
        {
            return Invalid<DeliveryEstimateData>($"withinDays must be between 0 and {MaxWithinDays}");
        }

        var sku = args.Sku!;
        var inventory = _store.GetInventory(sku);
        var warehouse = _store.Warehouses.FirstOrDefault(w => w.WarehouseId == inventory.WarehouseId);
        var rule = _store.ShippingRules.FirstOrDefault(r => r.WarehouseId == inventory.WarehouseId && r.Destination == city);
        var date = DeliveryCalculator.EstimateDelivery(_store, sku, city, AsOf);
        var deadline = args.WithinDays is { } days ? DeliveryCalculator.Deadline(AsOf, days) : (DateOnly?)null;
        return ToolOutcome<DeliveryEstimateData>.Success(new(
            sku,
            city,
            SimulationContext.TodayOf(AsOf),
            date is not null,
            date,
            inventory.WarehouseId,
            warehouse?.CutoffHour,
            rule?.TransitBusinessDays,
            deadline,
            deadline is { } d ? date is { } estimated && estimated <= d : null));
    }

    public ToolOutcome<ReturnPolicyData> GetReturnPolicy(ReturnPolicyArguments args)
    {
        if (ValidateSku(args.Sku) is { } error)
        {
            return ToolOutcome<ReturnPolicyData>.Fail(error.Code, error.Message);
        }

        if (!TryParseOptionalEnum(args.Question, out ReturnQuestion? question) || question is not { } q)
        {
            return Invalid<ReturnPolicyData>($"unsupported question '{args.Question}'; use {string.Join(", ", Enum.GetNames<ReturnQuestion>())}");
        }

        var sku = args.Sku!;
        var category = _store.ProductOf(sku).Category;
        return _store.FindReturnRule(category, q) is { } rule
            ? ToolOutcome<ReturnPolicyData>.Success(new(sku, category, q, rule.Answer, rule.WindowDays, rule.ConditionTr))
            : ToolOutcome<ReturnPolicyData>.Fail(ToolErrorCodes.NoReturnRule, $"no published {q} rule for {category}");
    }

    public ToolOutcome<PaymentOptionsData> GetPaymentOptions(PaymentOptionsArguments args)
    {
        if (ValidateSku(args.Sku) is { } error)
        {
            return ToolOutcome<PaymentOptionsData>.Fail(error.Code, error.Message);
        }

        var sku = args.Sku!;
        var category = _store.ProductOf(sku).Category;
        var price = _store.GetPrice(sku);
        return ToolOutcome<PaymentOptionsData>.Success(new(
            sku, category, price, "TRY", InstallmentCalculator.MaxInstallments(_store.InstallmentRules, category, price)));
    }

    private ProductDetails Details(string sku)
    {
        var variant = _store.GetVariant(sku);
        var product = _store.GetProduct(variant.ProductId);
        var attributes = new List<ProductAttributeValue> { new(ProductAttributeField.Category, product.Category.ToString()) };
        foreach (var field in Enum.GetValues<ProductAttributeField>())
        {
            if (ProductAttributeFields.KeyOf(field) is { } key
                && (variant.Attributes.Find(key) ?? product.Attributes.Find(key)) is { } value
                && !string.IsNullOrWhiteSpace(value))
            {
                attributes.Add(new(field, value));
            }
        }

        return new ProductDetails(sku, product.ProductId, product.Title, product.Category, _store.GetPrice(sku), [.. attributes]);
    }

    private (string Code, string Message)? ValidateSkus(ImmutableArray<string> skus)
    {
        if (skus.IsDefaultOrEmpty || skus.Length > MaxBatch)
        {
            return (ToolErrorCodes.InvalidArgument, $"skus must contain 1 to {MaxBatch} SKUs");
        }

        if (skus.Distinct(StringComparer.Ordinal).Count() != skus.Length)
        {
            return (ToolErrorCodes.InvalidArgument, "skus must be unique");
        }

        var unknown = skus.Where(s => !_skus.Contains(s)).ToList();
        return unknown.Count > 0 ? (ToolErrorCodes.UnknownSku, $"unknown SKU(s): {string.Join(", ", unknown)}") : null;
    }

    private (string Code, string Message)? ValidateSku(string? sku) =>
        string.IsNullOrWhiteSpace(sku) ? (ToolErrorCodes.InvalidArgument, "sku is required")
        : _skus.Contains(sku) ? null
        : (ToolErrorCodes.UnknownSku, $"unknown SKU: {sku}");

    private static City? ParseCity(string? text) => text?.Trim() switch
    {
        "İstanbul" or "istanbul" => City.Istanbul,
        "İzmir" or "izmir" => City.Izmir,
        { } other => TryParseOptionalEnum(other, out City? city) ? city : null,
        null => null,
    };

    /// <summary>Accepts only defined enum names (case-insensitive); numbers and unknown names are rejected.</summary>
    private static bool TryParseOptionalEnum<TEnum>(string? text, out TEnum? value)
        where TEnum : struct, Enum
    {
        value = null;
        if (text is null)
        {
            return true;
        }

        var name = Enum.GetNames<TEnum>().FirstOrDefault(n => string.Equals(n, text.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return false;
        }

        value = Enum.Parse<TEnum>(name);
        return true;
    }

    private static ToolOutcome<T> Invalid<T>(string message) => ToolOutcome<T>.Fail(ToolErrorCodes.InvalidArgument, message);
}
