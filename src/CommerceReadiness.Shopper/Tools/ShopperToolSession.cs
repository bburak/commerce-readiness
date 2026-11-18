using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using CommerceReadiness.Core.Shopping;
using Microsoft.Extensions.AI;

namespace CommerceReadiness.Shopper.Tools;

/// <summary>
/// Per-run tool surface for the model. Issues application-owned evidence ids (EVID-001, EVID-002, ...), records a
/// trace entry for every executed call, enforces the total tool-call budget, and stops the agent loop when a tool
/// fails unexpectedly. Validation and business errors are returned to the model as typed tool errors.
/// </summary>
public sealed class ShopperToolSession(CommerceTools tools, int maxToolCalls)
{
    private readonly List<ToolInvocationTrace> _trace = [];
    private int _calls;

    public ImmutableArray<ToolInvocationTrace> Trace => [.. _trace];

    public bool BudgetExceeded { get; private set; }

    public string? ExecutionFailure { get; private set; }

    public IList<AITool> CreateFunctions() =>
    [
        Function(ShopperToolNames.SearchProducts,
            "Hybrid product discovery: exact filters plus semantic ranking of the query text. Similarity is candidate " +
            "relevance only and never proves a product attribute; verify attributes with get_product_details.",
            SearchProductsAsync),
        Function(ShopperToolNames.GetProductDetails,
            "Structured published catalog attributes and published price for up to 10 SKUs. The authoritative source " +
            "for product attribute claims.",
            GetProductDetailsAsync),
        Function(ShopperToolNames.GetInventory, "Authoritative published inventory (available quantity) for up to 10 SKUs.", GetInventoryAsync),
        Function(ShopperToolNames.GetDeliveryEstimate,
            "Deterministic delivery estimate for one SKU to a city at the store clock. Pass withinDays to get the " +
            "deadline and whether the estimate meets it. Never compute dates yourself.",
            GetDeliveryEstimateAsync),
        Function(ShopperToolNames.GetReturnPolicy, "Published return policy for one SKU and one return question.", GetReturnPolicyAsync),
        Function(ShopperToolNames.GetPaymentOptions, "Published installment options for one SKU at its published price.", GetPaymentOptionsAsync),
    ];

    private static AIFunction Function(string name, string description, Delegate method) =>
        AIFunctionFactory.Create(method, new AIFunctionFactoryOptions
        {
            Name = name,
            Description = description,
            SerializerOptions = ShopperJson.Options,
        });

    private Task<object> SearchProductsAsync(
        [Description("Shopper's need in natural language, used for semantic ranking.")] string query,
        [Description("Backpacks, Sneakers, Headphones or SmallElectronics.")] string? category = null,
        [Description("Maximum price in TRY.")] decimal? maxPriceTry = null,
        [Description("Only SKUs with available stock. Default true.")] bool? inStockOnly = null,
        [Description("Exact shoe size (sneakers).")] int? shoeSize = null,
        [Description("Exact color in Turkish, e.g. Siyah, Beyaz.")] string? color = null,
        [Description("Minimum laptop compartment size in inches.")] decimal? minimumLaptopSizeIn = null,
        [Description("Minimum capacity in liters.")] decimal? minimumCapacityLiters = null,
        [Description("Minimum battery life in hours.")] int? minimumBatteryHours = null,
        [Description("Minimum warranty in months.")] int? minimumWarrantyMonths = null,
        [Description("Number of candidates, 1 to 10. Default 5.")] int? top = null,
        CancellationToken cancellationToken = default)
    {
        var args = new SearchProductsArguments(query, category, maxPriceTry, inStockOnly, shoeSize, color,
            minimumLaptopSizeIn, minimumCapacityLiters, minimumBatteryHours, minimumWarrantyMonths, top);
        return InvokeAsync(ShopperToolNames.SearchProducts, args, () => tools.SearchProductsAsync(args, cancellationToken));
    }

    private Task<object> GetProductDetailsAsync([Description("Up to 10 SKUs.")] string[] skus)
    {
        var args = new SkuListArguments([.. skus ?? []]);
        return InvokeAsync(ShopperToolNames.GetProductDetails, args, () => Task.FromResult(tools.GetProductDetails(args)));
    }

    private Task<object> GetInventoryAsync([Description("Up to 10 SKUs.")] string[] skus)
    {
        var args = new SkuListArguments([.. skus ?? []]);
        return InvokeAsync(ShopperToolNames.GetInventory, args, () => Task.FromResult(tools.GetInventory(args)));
    }

    private Task<object> GetDeliveryEstimateAsync(
        string sku,
        [Description("Istanbul, Ankara or Izmir.")] string city,
        [Description("Optional delivery deadline in calendar days from today.")] int? withinDays = null)
    {
        var args = new DeliveryEstimateArguments(sku, city, withinDays);
        return InvokeAsync(ShopperToolNames.GetDeliveryEstimate, args, () => Task.FromResult(tools.GetDeliveryEstimate(args)));
    }

    private Task<object> GetReturnPolicyAsync(
        string sku,
        [Description("UnopenedReturn, OpenedReturn or DefectiveReturn.")] string question)
    {
        var args = new ReturnPolicyArguments(sku, question);
        return InvokeAsync(ShopperToolNames.GetReturnPolicy, args, () => Task.FromResult(tools.GetReturnPolicy(args)));
    }

    private Task<object> GetPaymentOptionsAsync(string sku)
    {
        var args = new PaymentOptionsArguments(sku);
        return InvokeAsync(ShopperToolNames.GetPaymentOptions, args, () => Task.FromResult(tools.GetPaymentOptions(args)));
    }

    private async Task<object> InvokeAsync<TArgs, TData>(string toolName, TArgs args, Func<Task<ToolOutcome<TData>>> body)
    {
        if (ExecutionFailure is not null)
        {
            Terminate();
            return Refusal(ToolErrorCodes.UnexpectedToolFailure, "the run was stopped after an unexpected tool failure");
        }

        if (++_calls > maxToolCalls)
        {
            BudgetExceeded = true;
            Terminate();
            return Refusal(ToolErrorCodes.ToolBudgetExceeded, $"the run exceeded the budget of {maxToolCalls} tool calls");
        }

        var evidenceCallId = $"EVID-{_trace.Count + 1:000}";
        var stopwatch = Stopwatch.StartNew();
        ToolOutcome<TData> outcome;
        try
        {
            outcome = await body();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ExecutionFailure = $"{toolName} failed: {ex.GetType().Name}: {ex.Message}";
            Terminate();
            outcome = ToolOutcome<TData>.Fail(ToolErrorCodes.UnexpectedToolFailure, "the tool failed unexpectedly");
        }

        _trace.Add(new ToolInvocationTrace(
            _trace.Count + 1,
            evidenceCallId,
            toolName,
            JsonSerializer.SerializeToElement(args, ShopperJson.Options),
            outcome.Ok,
            outcome.ErrorCode,
            outcome.ErrorMessage,
            outcome.Ok ? JsonSerializer.SerializeToElement(outcome.Data, ShopperJson.Options) : null,
            stopwatch.ElapsedMilliseconds));
        return new ToolResultEnvelope<TData>(evidenceCallId, toolName, outcome.Ok, outcome.ErrorCode, outcome.ErrorMessage, outcome.Data);
    }

    private static object Refusal(string code, string message) => new { ok = false, errorCode = code, errorMessage = message };

    private static void Terminate()
    {
        if (FunctionInvokingChatClient.CurrentContext is { } context)
        {
            context.Terminate = true;
        }
    }
}
