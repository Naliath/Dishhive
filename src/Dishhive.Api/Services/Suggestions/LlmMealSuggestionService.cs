using Dishhive.Api.Models;
using Dishhive.Api.Services.Ai;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Text.Json;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Coordinates capability gating, completion attempts, external-tool sessions,
/// deterministic post-processing, and rules fallback.</summary>
public sealed class LlmMealSuggestionService(
    IAiExecutionGate executionGate,
    RulesMealSuggestionService fallback,
    AiOptions options,
    IAiModelCapabilityService capability,
    IAiPromptProvider promptProvider,
    IExternalRecipeSessionFactory externalRecipeSessions,
    MealSuggestionPostProcessor postProcessor,
    ILogger<LlmMealSuggestionService> logger,
    AiPlanningMetricsStore? metricsStore = null) : IMealSuggestionService
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
        var run = new AiPlanningRun
        {
            RequestId = requestId,
            StartedAt = DateTime.UtcNow,
            Provider = options.Provider,
            Model = options.Model,
            Instructions = request.Instructions,
            RequestedDays = request.DaysToFill.Count
        };
        IExternalRecipeSession? externalRecipeSession = null;
        List<ExternalRecipeTools.RecipeResearchRequest> researchRequirements = [];
        IReadOnlyList<ExternalRecipeTools.GetRecipeResult> verifiedResearchCandidates = [];
        string? unresolvedQualityIssue = null;

        IReadOnlyList<MealSuggestion> Complete(IReadOnlyList<MealSuggestion> result, string outcome)
        {
            run.Outcome = outcome;
            run.SuggestedItems = result.Count;
            run.ExternalSuggestions = result.Count(item => item.SourceUrl != null);
            run.FallbackSuggestions = result.Count(item => item.Source == MealSuggestionSource.RulesFallback);
            return result;
        }

        try
        {
            var gateDecision = await executionGate.EnsureAvailableAsync(
                AiTaskKind.WeekPlanning, capability, cancellationToken);
            run.CapabilityWaitMs = gateDecision.WaitMs;
            if (!gateDecision.Allowed)
            {
                logger.LogWarning(
                    "[{RequestId}] Model {Provider}/{Model} failed its capability test (verdict={Verdict}); using rules fallback without calling it",
                    requestId, options.Provider, options.Model, gateDecision.Reason);
                return Complete(Finalize(await fallback.SuggestAsync(request, cancellationToken), request), "capabilityFallback");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(executionGate.Profile(
                AiTaskKind.WeekPlanning, request.SourceConstraints.Count > 0).TimeoutSeconds));

            var intent = new PlanningIntent();
            if (!string.IsNullOrWhiteSpace(request.Instructions) || request.SourceConstraints.Count > 0)
            {
                var intentStopwatch = Stopwatch.StartNew();
                var intentExecution = await executionGate.ExecuteAsync(new AiExecutionRequest(
                    AiTaskKind.PlanningIntent,
                    [
                        new ChatMessage(ChatRole.System, PlanningIntentContract.SystemPrompt),
                        new ChatMessage(ChatRole.User, BuildPlanningIntentPrompt(request))
                    ],
                    "planning-intent-v1",
                    requestId,
                    ResponseFormat: PlanningIntentContract.JsonSchemaFormat), timeout.Token);
                var intentResponse = intentExecution.Response;
                intentStopwatch.Stop();
                RecordCompletion(run, intentResponse, intentStopwatch.ElapsedMilliseconds);
                intent = PlanningIntentContract.Parse(intentResponse.Text, request)!;
                if (intent == null)
                {
                    await intentExecution.ReportOutcomeAsync("parseFailure", "Invalid planning intent");
                    run.ParseFailures++;
                    logger.LogWarning(
                        "[{RequestId}] AI planning intent was invalid; visible={Visible}. Using rules fallback instead of guessing at language-specific meaning",
                        requestId, FormatForLog(intentResponse.Text, 2000));
                    return Complete(Finalize(await fallback.SuggestAsync(request, cancellationToken), request), "intentFallback");
                }
                await intentExecution.ReportOutcomeAsync("success");
                logger.LogInformation(
                    "[{RequestId}] AI planning intent completed in {ElapsedMs}ms with {ConstraintCount} constraint(s)",
                    requestId, intentStopwatch.ElapsedMilliseconds, intent.Constraints.Count);
                logger.LogDebug(
                    "[{RequestId}] Normalized planning intent: {Intent}",
                    requestId, PlanningIntentContract.FormatForModel(intent));
            }
            request = request with { Intent = intent };
            run.NormalizedIntentJson = PlanningIntentContract.FormatForModel(intent);

            var useExternalResearch = externalRecipeSessions.IsConfigured
                && intent.Constraints.Any(item => item.SourceHost != null);
            run.UsedExternalResearch = useExternalResearch;

            logger.LogInformation(
                "[{RequestId}] AI suggestion request starting: {DayCount} day(s) to fill, externalResearch={UseExternalResearch}, {Provider}/{Model}",
                requestId, request.DaysToFill.Count, useExternalResearch, options.Provider, options.Model);

            var effectivePrompt = await promptProvider.GetEffectiveSystemPromptAsync(cancellationToken);
            var userPrompt = MealSuggestionPromptBuilder.BuildUserPrompt(request, options.MaxPromptTokens);
            var constraintIdsField = AiJsonContract.PropertyName<DaySuggestionPayload>(
                nameof(DaySuggestionPayload.ConstraintIds));
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, effectivePrompt),
                new(ChatRole.User, userPrompt),
                new(ChatRole.User,
                    $"Authoritative normalized planning intent follows. Satisfy every constraint and copy each satisfied constraint id into {constraintIdsField} on the corresponding suggestion.\n"
                    + PlanningIntentContract.FormatForModel(intent))
            };

            if (useExternalResearch)
            {
                externalRecipeSession = externalRecipeSessions.Create(request.SourceConstraints, requestId, logger);
                var researchRequests = intent.Constraints
                    .Where(item => item.SourceHost != null)
                    .Select(item => new ExternalRecipeTools.RecipeResearchRequest(
                        item.Id,
                        item.SearchQuery ?? item.DishRequest ?? "recipe",
                        item.SourceHost!,
                        item.Count,
                        item.Dates.Select(date => date.ToString("yyyy-MM-dd")).ToList(),
                        AiJsonContract.EnumName(item.Course),
                        item.RequiredClasses,
                        item.ExcludedClasses))
                    .ToList();

                var verifiedCandidates = await externalRecipeSession.ResearchAsync(researchRequests, timeout.Token);
                verifiedResearchCandidates = verifiedCandidates;
                researchRequirements = researchRequests;
                var sourceSiteField = AiJsonContract.PropertyName<CandidatePromptPayload>(
                    nameof(CandidatePromptPayload.SourceSite));
                var candidateIdField = AiJsonContract.PropertyName<CandidatePromptPayload>(
                    nameof(CandidatePromptPayload.CandidateId));
                var externalCandidateIdField = AiJsonContract.PropertyName<DaySuggestionPayload>(
                    nameof(DaySuggestionPayload.ExternalCandidateId));
                messages.Add(new ChatMessage(
                    ChatRole.User,
                    "Dishhive deterministically completed the required source research. "
                    + $"Use these verified candidates for every source-backed dish, matching {sourceSiteField}, and copy {candidateIdField} into {externalCandidateIdField}. "
                    + "Do not invent candidate ids or replace requested source dishes with local recipes.\n"
                    + FormatCandidates(verifiedCandidates)));
            }

            WeekSuggestionsPayload? payload = null;
            // Source-backed plans get one additional quality-repair opportunity. A first
            // correction may fix attendee allocation while accidentally dropping a source
            // candidate; correctness is worth one short no-reasoning pass here.
            var maxCompletionRetries = executionGate.Profile(
                AiTaskKind.WeekPlanning, useExternalResearch).MaxRetries + (useExternalResearch ? 1 : 0);
            var useStructuredOutput = true;
            for (var attempt = 0; attempt <= maxCompletionRetries; attempt++)
            {
                var attemptStopwatch = Stopwatch.StartNew();
                var completion = await executionGate.ExecuteAsync(new AiExecutionRequest(
                    AiTaskKind.WeekPlanning,
                    messages,
                    "week-suggestions-v2",
                    requestId,
                    attempt + 1,
                    MealSuggestionResponseContract.JsonSchemaFormat,
                    useStructuredOutput ? AiResponseFormatPolicy.WhenSupported : AiResponseFormatPolicy.None,
                    UseExtendedTimeout: useExternalResearch),
                    timeout.Token);
                var response = completion.Response;
                attemptStopwatch.Stop();
                RecordCompletion(run, response, attemptStopwatch.ElapsedMilliseconds);
                LogUsage(requestId, response, attempt, attemptStopwatch.Elapsed);
                var reasoning = GetReasoningText(response);
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(
                        "[{RequestId}] Raw AI response (attempt {Attempt}/{MaxAttempts}): finish={Finish}; "
                        + "visibleChars={VisibleChars}; visible={Visible}; reasoningChars={ReasoningChars}; "
                        + "reasoning={Reasoning}; contentTypes={ContentTypes}",
                        requestId, attempt + 1, maxCompletionRetries + 1, response.FinishReason,
                        response.Text?.Length ?? 0, FormatForLog(response.Text, 4000),
                        reasoning.Length, FormatForLog(reasoning, 4000), GetContentTypes(response));
                }

                payload = MealSuggestionResponseContract.Parse(response.Text);
                if (payload?.Suggestions is not null)
                {
                    var qualityIssues = PlanningIntentValidator.Validate(
                        payload, intent, request, externalRecipeSession).ToList();

                    if (qualityIssues.Count > 0)
                    {
                        var qualityIssue = string.Join("; ", qualityIssues);
                        unresolvedQualityIssue = qualityIssue;
                        logger.LogWarning(
                            "[{RequestId}] AI reply was valid JSON but missed explicit planning requirements: {Issue}",
                            requestId, qualityIssue);
                        if (attempt < maxCompletionRetries)
                        {
                            // Some local OpenAI-compatible servers satisfy a JSON schema
                            // while becoming oddly rigid about the previous field values.
                            // The focused repair already has valid JSON to imitate, so let
                            // prompted JSON prioritize semantic corrections.
                            await completion.ReportOutcomeAsync("validationFailure", qualityIssue);
                            useStructuredOutput = false;
                            messages.Add(new ChatMessage(ChatRole.Assistant, response.Text));
                            messages.Add(new ChatMessage(
                                ChatRole.User,
                                $"Correct the plan: {qualityIssue}. Re-read the original instructions and authoritative weekday/date mapping. Use the verified candidate ids already supplied, preserve all other requirements, and reply with ONLY the corrected JSON object."));
                            payload = null;
                            continue;
                        }
                        await completion.ReportOutcomeAsync("validationFailure", qualityIssue);
                    }
                    else
                    {
                        unresolvedQualityIssue = null;
                        await completion.ReportOutcomeAsync("success");
                    }
                    break;
                }

                var text = response.Text ?? "";
                run.ParseFailures++;
                await completion.ReportOutcomeAsync("parseFailure", "Unparseable week-suggestions payload");
                var truncated = response.FinishReason == ChatFinishReason.Length;
                var diagnosis = truncated && string.IsNullOrWhiteSpace(text)
                    ? "The model reached its output limit before producing visible JSON; reasoning likely consumed the output budget."
                    : null;
                logger.LogWarning(
                    "[{RequestId}] AI reply unparseable (attempt {Attempt}/{Max}, truncated={Truncated}). "
                    + "VisibleChars={VisibleChars}, visible={Visible}, reasoningChars={ReasoningChars}, "
                    + "contentTypes={ContentTypes}, diagnosis={Diagnosis}",
                    requestId, attempt + 1, maxCompletionRetries + 1, truncated, text.Length,
                    FormatForLog(text, 600), reasoning.Length, GetContentTypes(response), diagnosis ?? "none");

                if (attempt < maxCompletionRetries)
                {
                    messages.Add(new ChatMessage(
                        ChatRole.Assistant,
                        MealSuggestionResponseContract.BuildRepromptQuote(text)));
                    messages.Add(new ChatMessage(
                        ChatRole.User,
                        (truncated
                            ? "Your reply was cut off before the JSON was complete. Reply again with ONLY the JSON object and keep every reason to a few words."
                            : "That was not valid JSON. Reply with ONLY the JSON object in the required shape, no other text.")));
                }
            }

            if (payload?.Suggestions is null)
            {
                logger.LogWarning(
                    "[{RequestId}] AI suggestions unparseable after {Attempts} attempt(s) in {ElapsedMs}ms; using rules fallback",
                    requestId, maxCompletionRetries + 1, stopwatch.ElapsedMilliseconds);
                return Complete(Finalize(await fallback.SuggestAsync(request, cancellationToken), request), "parseFallback");
            }

            if (unresolvedQualityIssue != null)
            {
                payload = PlanningIntentValidator.Repair(
                    payload, intent, request, verifiedResearchCandidates);
                var repairedIssues = PlanningIntentValidator.Validate(
                    payload, intent, request, externalRecipeSession).ToList();
                unresolvedQualityIssue = repairedIssues.Count == 0
                    ? null
                    : string.Join("; ", repairedIssues);
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
            var finalized = Finalize(suggestions, request);
            return Complete(finalized, unresolvedQualityIssue != null
                ? "qualityFallback"
                : finalized.Any(item => item.Source == MealSuggestionSource.RulesFallback)
                    ? "partialFallback"
                    : "success");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "[{RequestId}] AI suggestion request cancelled by caller after {ElapsedMs}ms",
                requestId, stopwatch.ElapsedMilliseconds);
            run.Outcome = "cancelled";
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[{RequestId}] AI suggestion call failed ({Provider}/{Model}) after {ElapsedMs}ms; using rules fallback",
                requestId, options.Provider, options.Model, stopwatch.ElapsedMilliseconds);
            run.Error = ex.Message;
            return Complete(Finalize(await fallback.SuggestAsync(request, cancellationToken), request), "errorFallback");
        }
        finally
        {
            stopwatch.Stop();
            run.CompletedAt = DateTime.UtcNow;
            run.TotalDurationMs = stopwatch.ElapsedMilliseconds;
            if (externalRecipeSession != null)
            {
                var research = externalRecipeSession.GetMetrics();
                run.ResearchCalls = research.ResearchCalls;
                run.SearchCount = research.SearchCount;
                run.EmptySearchCount = research.EmptySearchCount;
                run.SearchResultCount = research.SearchResultCount;
                run.RecipeResolutionCount = research.ResolutionCount;
                run.RecipeResolutionFailureCount = research.ResolutionFailureCount;
                run.SearchDurationMs = research.SearchDurationMs;
                run.RecipeResolutionDurationMs = research.ResolutionDurationMs;
            }
            if (metricsStore != null)
            {
                await metricsStore.SaveAsync(run);
            }
        }
    }

    private IReadOnlyList<MealSuggestion> Finalize(
        IReadOnlyList<MealSuggestion> suggestions,
        MealSuggestionRequest request)
        => postProcessor.Finalize(suggestions, request);

    private static string BuildPlanningIntentPrompt(MealSuggestionRequest request)
    {
        return AiJsonContract.Serialize(new PlanningIntentPromptInput(
            request.SourceConstraints
                .Select(constraint => new PlanningIntentSource(constraint.Name, constraint.Host))
                .ToList(),
            Enumerable.Range(0, 7)
                .Select(request.WeekStart.AddDays)
                .Select(date => new PlanningIntentDate(date.DayOfWeek.ToString(), date))
                .ToList(),
            request.Instructions));
    }

    private static string FormatCandidates(
        IReadOnlyList<ExternalRecipeTools.GetRecipeResult> candidates)
        => AiJsonContract.Serialize(candidates
            .Where(candidate => candidate.Error == null)
            .Select(candidate => new CandidatePromptPayload(
                candidate.CandidateId,
                candidate.SourceSite,
                candidate.Title,
                candidate.TotalTimeMinutes,
                candidate.Servings,
                candidate.Ingredients?.Take(30).ToList(),
                candidate.Instructions?.Take(10).ToList())));

    private sealed record PlanningIntentPromptInput(
        IReadOnlyList<PlanningIntentSource> ReferencedSources,
        IReadOnlyList<PlanningIntentDate> WeekdayMapping,
        string? PlannerInstructions);

    private sealed record PlanningIntentSource(string Name, string Host);
    private sealed record PlanningIntentDate(string Weekday, DateOnly Date);
    private sealed record CandidatePromptPayload(
        string CandidateId,
        string? SourceSite,
        string? Title,
        int? TotalTimeMinutes,
        int? Servings,
        IReadOnlyList<string>? Ingredients,
        IReadOnlyList<string>? Instructions);

    private static void RecordCompletion(AiPlanningRun run, ChatResponse response, long elapsedMs)
    {
        run.CompletionAttempts++;
        run.CompletionDurationMs += elapsedMs;
        run.ModelTurns += Math.Max(1, response.Messages.Count(message => message.Role == ChatRole.Assistant));
        if (response.Usage is not { } usage)
        {
            return;
        }

        run.InputTokens += usage.InputTokenCount ?? 0;
        run.OutputTokens += usage.OutputTokenCount ?? 0;
        run.ReasoningTokens += usage.ReasoningTokenCount ?? 0;
        run.TotalTokens += usage.TotalTokenCount ?? 0;
    }

    private void LogUsage(
        string requestId,
        ChatResponse response,
        int attempt,
        TimeSpan elapsed)
    {
        if (response.Usage is { } usage)
        {
            logger.LogInformation(
                "[{RequestId}] AI completion (attempt {Attempt}) in {ElapsedMs}ms: input={Input}, output={Output}, reasoning={Reasoning}, total={Total} tokens; finish={Finish}",
                requestId, attempt + 1, elapsed.TotalMilliseconds, usage.InputTokenCount,
                usage.OutputTokenCount, usage.ReasoningTokenCount, usage.TotalTokenCount, response.FinishReason);
        }
        else
        {
            logger.LogInformation(
                "[{RequestId}] AI completion (attempt {Attempt}) in {ElapsedMs}ms; finish={Finish} (no usage reported)",
                requestId, attempt + 1, elapsed.TotalMilliseconds, response.FinishReason);
        }
    }

    private static string GetReasoningText(ChatResponse response) => string.Join(
        "\n\n",
        response.Messages
            .SelectMany(message => message.Contents)
            .OfType<TextReasoningContent>()
            .Select(content => content.Text)
            .Where(text => !string.IsNullOrEmpty(text)));

    private static string GetContentTypes(ChatResponse response)
    {
        var types = response.Messages
            .SelectMany(message => message.Contents)
            .Select(content => content.GetType().Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return types.Count == 0 ? "none" : string.Join(",", types);
    }

    /// <summary>JSON-escapes control characters so whitespace-only model output remains visible in logs.</summary>
    internal static string FormatForLog(string? value, int maxChars)
    {
        if (value is null)
        {
            return "<null>";
        }

        if (value.Length <= maxChars)
        {
            return JsonSerializer.Serialize(value);
        }

        var half = Math.Max(1, maxChars / 2);
        var omitted = value.Length - (half * 2);
        var excerpt = value[..half] + $"\n… {omitted} character(s) omitted …\n" + value[^half..];
        return JsonSerializer.Serialize(excerpt);
    }

    private async Task<List<MealSuggestion>> BackfillMissingDaysAsync(
        string requestId,
        List<MealSuggestion> suggestions,
        MealSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        var filled = suggestions
            .Where(suggestion => suggestion.MealType == Dishhive.Api.Models.MealType.Dinner
                && suggestion.Course == Dishhive.Api.Models.Course.Main)
            .Select(suggestion => suggestion.Date)
            .ToHashSet();
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
                MealType = suggestion.MealType,
                Course = suggestion.Course,
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
