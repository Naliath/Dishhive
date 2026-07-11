using Microsoft.Extensions.AI;
using System.Diagnostics;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Coordinates capability gating, completion attempts, external-tool sessions,
/// deterministic post-processing, and rules fallback.</summary>
public sealed class LlmMealSuggestionService(
    IChatClient chatClient,
    RulesMealSuggestionService fallback,
    AiOptions options,
    IAiModelCapabilityService capability,
    IAiPromptProvider promptProvider,
    IExternalRecipeSessionFactory externalRecipeSessions,
    MealSuggestionPostProcessor postProcessor,
    ILogger<LlmMealSuggestionService> logger) : IMealSuggestionService
{
    public bool IsEnabled => true;

    public async Task<IReadOnlyList<MealSuggestion>> SuggestAsync(
        MealSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DaysToFill.Count == 0)
        {
            return [];
        }

        var requestId = Guid.NewGuid().ToString("N")[..8];
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var modelCapability = await capability.EnsureTestedAsync(cancellationToken);
            if (!modelCapability.Viable)
            {
                logger.LogWarning(
                    "[{RequestId}] Model {Provider}/{Model} failed its capability test (verdict={Verdict}); using rules fallback without calling it",
                    requestId, options.Provider, options.Model, modelCapability.Verdict);
                return Finalize(await fallback.SuggestAsync(request, cancellationToken), request);
            }

            var useTools = externalRecipeSessions.IsConfigured && request.SourceConstraints.Count > 0;
            if (useTools && modelCapability.ToolCallingPassed == false)
            {
                logger.LogWarning(
                    "[{RequestId}] Model {Provider}/{Model} failed its external-tool capability test; using rules fallback",
                    requestId, options.Provider, options.Model);
                return Finalize(await fallback.SuggestAsync(request, cancellationToken), request);
            }

            logger.LogInformation(
                "[{RequestId}] AI suggestion request starting: {DayCount} day(s) to fill, tools={UseTools}, {Provider}/{Model}",
                requestId, request.DaysToFill.Count, useTools, options.Provider, options.Model);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(useTools ? options.AgentTimeoutSeconds : options.TimeoutSeconds));

            var effectivePrompt = await promptProvider.GetEffectiveSystemPromptAsync(cancellationToken);
            var systemPrompt = options.DisableThinking && !useTools
                ? "/no_think\n" + effectivePrompt
                : effectivePrompt;
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, MealSuggestionPromptBuilder.BuildUserPrompt(request, options.MaxPromptTokens))
            };

            IChatClient requestClient = chatClient;
            IList<AITool>? tools = null;
            IExternalRecipeSession? externalRecipeSession = null;
            if (useTools)
            {
                externalRecipeSession = externalRecipeSessions.Create(request.SourceConstraints, requestId, logger);
                tools = externalRecipeSession.BuildTools();
                requestClient = chatClient.AsBuilder()
                    .UseFunctionInvocation(configure: invocation =>
                        invocation.MaximumIterationsPerRequest = options.MaxToolIterations)
                    .Build();
            }

            var chatOptions = new ChatOptions
            {
                MaxOutputTokens = options.MaxOutputTokens,
                Temperature = (float)options.Temperature,
                Tools = tools,
                ResponseFormat = modelCapability.ResponseMode == AiResponseMode.JsonSchema
                    ? MealSuggestionResponseContract.JsonSchemaFormat
                    : null
            };

            WeekSuggestionsPayload? payload = null;
            for (var attempt = 0; attempt <= Math.Max(0, options.MaxRetries); attempt++)
            {
                var attemptStopwatch = Stopwatch.StartNew();
                var response = await requestClient.GetResponseAsync(
                    messages,
                    chatOptions,
                    cancellationToken: timeout.Token);
                attemptStopwatch.Stop();
                LogUsage(requestId, response, attempt, attemptStopwatch.Elapsed);
                logger.LogDebug(
                    "[{RequestId}] Raw AI output (attempt {Attempt}/{MaxAttempts}): {Output}",
                    requestId, attempt + 1, options.MaxRetries + 1, response.Text ?? "<null>");

                payload = MealSuggestionResponseContract.Parse(response.Text);
                if (payload?.Suggestions is not null)
                {
                    break;
                }

                var text = response.Text ?? "";
                var truncated = response.FinishReason == ChatFinishReason.Length;
                logger.LogWarning(
                    "[{RequestId}] AI reply unparseable (attempt {Attempt}/{Max}, truncated={Truncated}). Length={Length}, start: {Snippet}",
                    requestId, attempt + 1, options.MaxRetries + 1, truncated, text.Length,
                    text.Length > 300 ? text[..300] : text);

                if (attempt < options.MaxRetries)
                {
                    messages.Add(new ChatMessage(
                        ChatRole.Assistant,
                        MealSuggestionResponseContract.BuildRepromptQuote(text)));
                    messages.Add(new ChatMessage(
                        ChatRole.User,
                        truncated
                            ? "Your reply was cut off before the JSON was complete. Reply again with ONLY the JSON object and keep every reason to a few words."
                            : "That was not valid JSON. Reply with ONLY the JSON object in the required shape, no other text."));
                }
            }

            if (payload?.Suggestions is null)
            {
                logger.LogWarning(
                    "[{RequestId}] AI suggestions unparseable after {Attempts} attempt(s) in {ElapsedMs}ms; using rules fallback",
                    requestId, options.MaxRetries + 1, stopwatch.ElapsedMilliseconds);
                return Finalize(await fallback.SuggestAsync(request, cancellationToken), request);
            }

            var suggestions = postProcessor.Process(payload, request, externalRecipeSession);
            suggestions = await BackfillMissingDaysAsync(
                requestId,
                suggestions,
                request,
                cancellationToken);
            logger.LogInformation(
                "[{RequestId}] AI proposed {Count} meal suggestions via {Provider}/{Model} in {ElapsedMs}ms",
                requestId, suggestions.Count, options.Provider, options.Model, stopwatch.ElapsedMilliseconds);
            return Finalize(suggestions, request);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "[{RequestId}] AI suggestion request cancelled by caller after {ElapsedMs}ms",
                requestId, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[{RequestId}] AI suggestion call failed ({Provider}/{Model}) after {ElapsedMs}ms; using rules fallback",
                requestId, options.Provider, options.Model, stopwatch.ElapsedMilliseconds);
            return Finalize(await fallback.SuggestAsync(request, cancellationToken), request);
        }
    }

    private IReadOnlyList<MealSuggestion> Finalize(
        IReadOnlyList<MealSuggestion> suggestions,
        MealSuggestionRequest request)
        => postProcessor.Finalize(suggestions, request);

    private void LogUsage(
        string requestId,
        ChatResponse response,
        int attempt,
        TimeSpan elapsed)
    {
        if (response.Usage is { } usage)
        {
            logger.LogInformation(
                "[{RequestId}] AI completion (attempt {Attempt}) in {ElapsedMs}ms: input={Input}, output={Output}, total={Total} tokens; finish={Finish}",
                requestId, attempt + 1, elapsed.TotalMilliseconds, usage.InputTokenCount,
                usage.OutputTokenCount, usage.TotalTokenCount, response.FinishReason);
        }
        else
        {
            logger.LogInformation(
                "[{RequestId}] AI completion (attempt {Attempt}) in {ElapsedMs}ms; finish={Finish} (no usage reported)",
                requestId, attempt + 1, elapsed.TotalMilliseconds, response.FinishReason);
        }
    }

    private async Task<List<MealSuggestion>> BackfillMissingDaysAsync(
        string requestId,
        List<MealSuggestion> suggestions,
        MealSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        var filled = suggestions.Select(suggestion => suggestion.Date).ToHashSet();
        var missing = request.DaysToFill.Where(date => !filled.Contains(date)).ToList();
        if (missing.Count == 0)
        {
            return suggestions;
        }

        logger.LogInformation(
            "[{RequestId}] AI left {Count} day(s) unfilled; backfilling from rules",
            requestId, missing.Count);
        var usedByItem = suggestions
            .Where(suggestion => suggestion.FreezyItemRef != null)
            .GroupBy(suggestion => suggestion.FreezyItemRef!)
            .ToDictionary(group => group.Key, group => group.Sum(suggestion => suggestion.FreezyItemQuantity));
        var remainingFrozen = request.AvailableFrozenItems
            .Select(item => usedByItem.TryGetValue(item.Id, out var used)
                ? item with { Quantity = item.Quantity - used }
                : item)
            .Where(item => item.Quantity > 0)
            .ToList();
        var aiPicksAsExisting = suggestions
            .Where(suggestion => suggestion.DishName != null)
            .Select(suggestion => new ExistingMeal
            {
                Date = suggestion.Date,
                DishName = suggestion.DishName
            });
        var fallbackRequest = request with
        {
            DaysToFill = missing,
            AvailableFrozenItems = remainingFrozen,
            WeekPlan = request.WeekPlan.Concat(aiPicksAsExisting).ToList()
        };
        var filler = await fallback.SuggestAsync(fallbackRequest, cancellationToken);
        return suggestions.Concat(filler).OrderBy(suggestion => suggestion.Date).ToList();
    }
}
