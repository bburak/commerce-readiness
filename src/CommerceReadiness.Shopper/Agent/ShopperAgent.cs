using System.ClientModel;
using System.Diagnostics;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Shopper.Tools;
using Microsoft.Extensions.AI;

namespace CommerceReadiness.Shopper.Agent;

/// <summary>
/// One single-turn shopper run: system instructions plus the shopper's text only (never an intent id or expected
/// outcome), automatic tool invocation, and a structured ShopperResult. Failures become a run status, never a crash.
/// </summary>
public static class ShopperAgent
{
    public static async Task<ShopperRun> RunAsync(
        IChatClient chat, CommerceTools tools, ShopperSettings settings, string query, CancellationToken cancellationToken = default)
    {
        var session = new ShopperToolSession(tools, settings.MaxToolCalls);
        var options = new ChatOptions { Tools = session.CreateFunctions() };
        if (settings.ReasoningEffort is { } effort)
        {
            options.Reasoning = new ReasoningOptions
            {
                Effort = effort switch
                {
                    "low" => ReasoningEffort.Low,
                    "medium" => ReasoningEffort.Medium,
                    _ => ReasoningEffort.High,
                },
            };
        }

        ChatMessage[] messages = [new(ChatRole.System, ShopperPrompt.Text), new(ChatRole.User, query)];
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await chat.GetResponseAsync<ShopperResult>(
                messages, ShopperJson.Options, options, useJsonSchemaResponseFormat: true, cancellationToken);
            var usage = response.Usage is { } u ? new ShopperUsage(u.InputTokenCount, u.OutputTokenCount, u.TotalTokenCount) : null;

            if (session.ExecutionFailure is { } failure)
            {
                return Run(ShopperRunStatus.ToolExecutionError, failure, null);
            }

            if (session.BudgetExceeded)
            {
                return Run(ShopperRunStatus.ToolBudgetExceeded, $"more than {settings.MaxToolCalls} tool calls", null);
            }

            if (!TryGetResult(response, out var result))
            {
                return Run(ShopperRunStatus.InvalidResult, "the final output is not a parsable ShopperResult", null);
            }

            var errors = ShopperResultValidator.Validate(result);
            return errors.IsEmpty
                ? Run(ShopperRunStatus.Completed, null, result)
                : Run(ShopperRunStatus.InvalidResult, string.Join("; ", errors), result);

            ShopperRun Run(ShopperRunStatus status, string? detail, ShopperResult? value) =>
                new(status, detail, value, session.Trace, usage, response.ModelId, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var status = session.ExecutionFailure is not null || ex is not (ClientResultException or HttpRequestException or TimeoutException or OperationCanceledException)
                ? ShopperRunStatus.ToolExecutionError
                : ShopperRunStatus.ProviderError;
            return new ShopperRun(status, session.ExecutionFailure ?? $"{ex.GetType().Name}: {ex.Message}", null, session.Trace, null, null,
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static bool TryGetResult(ChatResponse<ShopperResult> response, out ShopperResult result)
    {
        try
        {
            if (response.TryGetResult(out var value) && value is not null)
            {
                result = value;
                return true;
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        result = null!;
        return false;
    }
}
