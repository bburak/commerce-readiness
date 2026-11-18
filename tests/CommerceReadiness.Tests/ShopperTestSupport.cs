using System.Text.Json;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation.Shopping;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Core.Storage;
using CommerceReadiness.Retrieval;
using CommerceReadiness.Retrieval.Embeddings;
using CommerceReadiness.Shopper;
using CommerceReadiness.Shopper.Agent;
using CommerceReadiness.Shopper.Tools;
using Microsoft.Extensions.AI;

namespace CommerceReadiness.Tests;

internal static class ShopperTestSupport
{
    public static RetrievalSettings Retrieval { get; } = new(
        RetrievalSettings.DefaultConnectionString, null, RetrievalSettings.DefaultEmbeddingModel, RetrievalSettings.DefaultEmbeddingDimensions);

    public static ShopperSettings Settings { get; } = ShopperSettings.Default;

    public static async Task<ShopperHost> OpenMemoryAsync(string? dataset = null) =>
        await ShopperHost.OpenAsync(dataset ?? TestData.NewSeed42DatasetDirectory(), Retrieval, EmbeddingProvider.Hashing, "memory");

    public static Task<(int Code, string Output, string Error)> RunCliAsync(IChatClient? chat, params string[] args) =>
        RunCliAsync(chat, Retrieval, Settings, args);

    public static async Task<(int Code, string Output, string Error)> RunCliAsync(
        IChatClient? chat, RetrievalSettings retrieval, ShopperSettings shopper, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await ShopperCli.RunAsync(args, output, error, retrieval, shopper, chat, TimeProvider.System);
        return (code, output.ToString(), error.ToString());
    }

    public static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, JsonDefaults.Options);

    public static ToolInvocationTrace Trace(
        int sequence, string tool, object args, object? data, bool ok = true, string? errorCode = null, string? errorMessage = null) =>
        new(sequence, $"EVID-{sequence:000}", tool, Json(args), ok, errorCode, errorMessage, data is null ? null : Json(data), 1);

    public static ShopperRun Completed(ShopperResult result, params ToolInvocationTrace[] traces) =>
        new(ShopperRunStatus.Completed, null, result, [.. traces], null, "scripted", 1);

    public static ShopperRun Failed(ShopperRunStatus status, string detail, params ToolInvocationTrace[] traces) =>
        new(status, detail, null, [.. traces], null, "scripted", 1);

    public static Recommendation Rec(string sku, RecommendationConfidence confidence, params GroundedClaim[] claims) =>
        new(sku, confidence, [.. claims]);

    public static GroundedClaim Attr(ProductAttributeField field, string value, params string[] evidence) =>
        new(ClaimType.ProductAttribute, field, null, null, value, [.. evidence]);

    public static GroundedClaim Price(string value, params string[] evidence) =>
        new(ClaimType.Price, null, null, null, value, [.. evidence]);

    public static GroundedClaim Stock(string value, params string[] evidence) =>
        new(ClaimType.Availability, null, null, null, value, [.. evidence]);

    public static GroundedClaim Delivery(City city, string value, params string[] evidence) =>
        new(ClaimType.Delivery, null, city, null, value, [.. evidence]);

    public static GroundedClaim Return(ReturnQuestion question, string value, params string[] evidence) =>
        new(ClaimType.ReturnPolicy, null, null, question, value, [.. evidence]);

    public static GroundedClaim Payment(string value, params string[] evidence) =>
        new(ClaimType.PaymentOption, null, null, null, value, [.. evidence]);

    public static ShopperResult Recommended(params Recommendation[] recommendations) =>
        new(1, ShopperOutcome.Recommended, null, [.. recommendations], null, [.. recommendations.SelectMany(r => r.Claims.SelectMany(c => c.EvidenceCallIds)).Distinct()]);

    public static ShopperResult NoMatch(string reason) =>
        new(1, ShopperOutcome.NoMatch, null, [], reason, []);

    public static ShopperResult Clarify(string question, params ShopperMissingField[] fields) =>
        new(1, ShopperOutcome.NeedsClarification, new Clarification(question, [.. fields]), [], null, []);
}

/// <summary>
/// Deterministic IChatClient: returns planned function calls, then a final structured ShopperResult as JSON text.
/// FunctionInvokingChatClient drives the loop; this client never calls OpenAI.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<ChatResponse> _responses;

    public ScriptedChatClient(params ChatResponse[] responses) => _responses = new(responses);

    public int Remaining => _responses.Count;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(_responses.Count > 0
            ? _responses.Dequeue()
            : throw new InvalidOperationException("ScriptedChatClient has no more planned responses."));

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    public static ChatResponse FunctionCall(string name, IDictionary<string, object?> arguments, string callId = "call-1") =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]));

    public static ChatResponse FunctionCalls(params (string Name, IDictionary<string, object?> Args, string CallId)[] calls) =>
        new(new ChatMessage(ChatRole.Assistant,
            [.. calls.Select(c => (AIContent)new FunctionCallContent(c.CallId, c.Name, c.Args))]));

    public static ChatResponse Final(ShopperResult result) =>
        new(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(result, ShopperJson.Options))) { ModelId = "scripted" };

    public static ChatResponse Text(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text)) { ModelId = "scripted" };
}
