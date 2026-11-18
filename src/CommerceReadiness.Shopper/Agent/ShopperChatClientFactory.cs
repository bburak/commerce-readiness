using Microsoft.Extensions.AI;
using OpenAI;

namespace CommerceReadiness.Shopper.Agent;

/// <summary>
/// The only place that knows about OpenAI chat. The Responses API adapter is marked experimental (OPENAI001) in the
/// installed SDK; the diagnostic is suppressed for this file only, so the rest of the build stays warning-free.
/// </summary>
public static class ShopperChatClientFactory
{
    public static IChatClient CreateOpenAi(string? apiKey, ShopperSettings settings)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OPENAI_API_KEY is not configured. Set it with 'dotnet user-secrets set OPENAI_API_KEY <key> --id commerce-readiness-retrieval' " +
                "or as an environment variable.");
        }

#pragma warning disable OPENAI001
        var responses = new OpenAIClient(apiKey).GetResponsesClient().AsIChatClient(settings.Model);
#pragma warning restore OPENAI001
        return ShopperChatPipeline.Wrap(responses, settings);
    }
}

/// <summary>Bounded, sequential automatic function invocation around any inner chat client.</summary>
public static class ShopperChatPipeline
{
    public static IChatClient Wrap(IChatClient inner, ShopperSettings settings) =>
        new ChatClientBuilder(inner)
            .UseFunctionInvocation(configure: f =>
            {
                f.MaximumIterationsPerRequest = settings.MaxToolIterations;
                f.MaximumConsecutiveErrorsPerRequest = ShopperSettings.MaxConsecutiveErrors;
                f.AllowConcurrentInvocation = false;
                f.IncludeDetailedErrors = false;
            })
            .Build();
}
