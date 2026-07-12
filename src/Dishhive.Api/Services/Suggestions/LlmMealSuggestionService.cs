using Dishhive.Api.Models;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

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
            var capabilityStopwatch = Stopwatch.StartNew();
            var modelCapability = await capability.EnsureTestedAsync(cancellationToken);
            run.CapabilityWaitMs = capabilityStopwatch.ElapsedMilliseconds;
            if (!modelCapability.Viable)
            {
                logger.LogWarning(
                    "[{RequestId}] Model {Provider}/{Model} failed its capability test (verdict={Verdict}); using rules fallback without calling it",
                    requestId, options.Provider, options.Model, modelCapability.Verdict);
                return Complete(Finalize(await fallback.SuggestAsync(request, cancellationToken), request), "capabilityFallback");
            }

            var useTools = externalRecipeSessions.IsConfigured && request.SourceConstraints.Count > 0;
            run.UsedExternalResearch = useTools;

            logger.LogInformation(
                "[{RequestId}] AI suggestion request starting: {DayCount} day(s) to fill, tools={UseTools}, {Provider}/{Model}",
                requestId, request.DaysToFill.Count, useTools, options.Provider, options.Model);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(useTools ? options.AgentTimeoutSeconds : options.TimeoutSeconds));

            var effectivePrompt = await promptProvider.GetEffectiveSystemPromptAsync(cancellationToken);
            var userPrompt = MealSuggestionPromptBuilder.BuildUserPrompt(request, options.MaxPromptTokens);
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, effectivePrompt),
                new(ChatRole.User, userPrompt)
            };

            if (useTools)
            {
                externalRecipeSession = externalRecipeSessions.Create(request.SourceConstraints, requestId, logger);
                var researchStopwatch = Stopwatch.StartNew();
                var researchPlanResponse = await chatClient.GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System,
                            "Interpret source-specific recipe research requests. Resolve fuzzy wording and any language, but do not plan meals. Reply only as JSON: {\"requests\":[{\"query\":\"source-language search terms\",\"site\":\"exact allowed host\",\"candidateCount\":2,\"dates\":[\"yyyy-MM-dd\"],\"course\":\"main|appetizer|side|dessert|null\"}]}. Include one request per referenced source. candidateCount MUST equal the number of distinct source recipes/uses requested (1-6): 'dishes from @site for 4 days' means 4 even if the clause also says 'some'; '2 desserts from @site, Friday and Sunday' means 2. Only use 3 when there is no explicit quantity, duration, or date count anywhere in that source's clause. Resolve explicitly named weekdays through the supplied mapping; use an empty dates list when no dates were specified."),
                        new ChatMessage(ChatRole.User, BuildResearchIntentPrompt(request))
                    ],
                    new ChatOptions
                    {
                        MaxOutputTokens = Math.Min(options.MaxOutputTokens, 800),
                        Temperature = 0,
                        Reasoning = options.DisableThinking
                            ? new ReasoningOptions { Effort = ReasoningEffort.None, Output = ReasoningOutput.None }
                            : null
                    },
                    timeout.Token);
                researchStopwatch.Stop();
                RecordCompletion(run, researchPlanResponse, researchStopwatch.ElapsedMilliseconds);
                logger.LogInformation(
                    "[{RequestId}] AI research intent completed in {ElapsedMs}ms: input={Input}, output={Output}, reasoning={Reasoning}, finish={Finish}",
                    requestId, researchStopwatch.ElapsedMilliseconds,
                    researchPlanResponse.Usage?.InputTokenCount,
                    researchPlanResponse.Usage?.OutputTokenCount,
                    researchPlanResponse.Usage?.ReasoningTokenCount,
                    researchPlanResponse.FinishReason);
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(
                        "[{RequestId}] Raw AI research intent: {Response}",
                        requestId, FormatForLog(researchPlanResponse.Text, 4000));
                }

                var researchRequests = ParseResearchPlan(researchPlanResponse.Text, request);
                if (researchRequests.Count == 0)
                {
                    run.ParseFailures++;
                    researchRequests = request.SourceConstraints
                        .Select(constraint => new ExternalRecipeTools.RecipeResearchRequest(
                            Query: "recipe", Site: constraint.Host, CandidateCount: 3))
                        .ToList();
                    logger.LogWarning(
                        "[{RequestId}] AI research intent was unparseable; using one bounded generic search per referenced source",
                        requestId);
                }
                else
                {
                    foreach (var constraint in request.SourceConstraints.Where(constraint =>
                                 researchRequests.All(item => !string.Equals(
                                     NormalizeHost(item.Site), NormalizeHost(constraint.Host),
                                     StringComparison.OrdinalIgnoreCase))))
                    {
                        researchRequests.Add(new ExternalRecipeTools.RecipeResearchRequest(
                            Query: "recipe", Site: constraint.Host, CandidateCount: 3));
                    }
                }

                var verifiedCandidates = await externalRecipeSession.ResearchAsync(researchRequests, timeout.Token);
                verifiedResearchCandidates = verifiedCandidates;
                researchRequirements = researchRequests;
                messages.Add(new ChatMessage(
                    ChatRole.User,
                    "Dishhive deterministically completed the required source research. "
                    + "Use these verified candidates for every source-backed dish, matching sourceSite, and copy candidateId into externalCandidateId. "
                    + "Do not invent candidate ids or replace requested source dishes with local recipes.\n"
                    + FormatCandidates(verifiedCandidates)));
            }

            var chatOptions = new ChatOptions
            {
                MaxOutputTokens = options.MaxOutputTokens,
                Temperature = (float)options.Temperature,
                Reasoning = options.DisableThinking
                    ? new ReasoningOptions { Effort = ReasoningEffort.None, Output = ReasoningOutput.None }
                    : null,
                ResponseFormat = modelCapability.ResponseMode == AiResponseMode.JsonSchema
                    ? MealSuggestionResponseContract.JsonSchemaFormat
                    : null
            };

            WeekSuggestionsPayload? payload = null;
            // Source-backed plans get one additional quality-repair opportunity. A first
            // correction may fix attendee allocation while accidentally dropping a source
            // candidate; correctness is worth one short no-reasoning pass here.
            var maxCompletionRetries = Math.Max(0, options.MaxRetries) + (useTools ? 1 : 0);
            for (var attempt = 0; attempt <= maxCompletionRetries; attempt++)
            {
                var attemptStopwatch = Stopwatch.StartNew();
                var response = await chatClient.GetResponseAsync(
                    messages,
                    chatOptions,
                    cancellationToken: timeout.Token);
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
                    var qualityIssues = new List<string>();
                    if (useTools && externalRecipeSession != null
                        && !MeetsResearchRequirements(
                            payload, externalRecipeSession, researchRequirements, out var sourceIssue))
                    {
                        qualityIssues.Add(sourceIssue);
                    }
                    if (!InstructionsAllowRepeats(request.Instructions)
                        && !MeetsUniquenessRequirement(payload, out var duplicateIssue))
                    {
                        qualityIssues.Add(duplicateIssue);
                    }
                    if (!MeetsExplicitDatedDishRequirements(payload, request, out var datedDishIssue))
                    {
                        qualityIssues.Add(datedDishIssue);
                    }

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
                            chatOptions.ResponseFormat = null;
                            messages.Add(new ChatMessage(ChatRole.Assistant, response.Text));
                            messages.Add(new ChatMessage(
                                ChatRole.User,
                                $"Correct the plan: {qualityIssue}. Re-read the original instructions and authoritative weekday/date mapping. Use the verified candidate ids already supplied, preserve all other requirements, and reply with ONLY the corrected JSON object."));
                            payload = null;
                            continue;
                        }
                    }
                    else
                    {
                        unresolvedQualityIssue = null;
                    }
                    break;
                }

                var text = response.Text ?? "";
                run.ParseFailures++;
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
                var repairedIssues = new List<string>();
                if (useTools && externalRecipeSession != null)
                {
                    payload = RepairResearchRequirements(
                        payload, researchRequirements, verifiedResearchCandidates, request, requestId);
                    if (!MeetsResearchRequirements(
                            payload, externalRecipeSession, researchRequirements, out var sourceIssue))
                    {
                        repairedIssues.Add(sourceIssue);
                    }
                }
                if (!InstructionsAllowRepeats(request.Instructions)
                    && !MeetsUniquenessRequirement(payload, out var duplicateIssue))
                {
                    repairedIssues.Add(duplicateIssue);
                }
                payload = RepairExplicitDatedDishRequirements(payload, request, requestId);
                if (!MeetsExplicitDatedDishRequirements(payload, request, out var datedDishIssue))
                {
                    repairedIssues.Add(datedDishIssue);
                }
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
            if (metricsStore != null && logger.IsEnabled(LogLevel.Debug))
            {
                await metricsStore.SaveAsync(run);
            }
        }
    }

    private IReadOnlyList<MealSuggestion> Finalize(
        IReadOnlyList<MealSuggestion> suggestions,
        MealSuggestionRequest request)
        => postProcessor.Finalize(suggestions, request);

    private sealed record ResearchPlan(List<ExternalRecipeTools.RecipeResearchRequest>? Requests);

    private static string BuildResearchIntentPrompt(MealSuggestionRequest request)
    {
        var sources = string.Join("\n", request.SourceConstraints.Select(constraint =>
            $"- {constraint.Name}: {constraint.Host}"));
        var dates = string.Join("\n", Enumerable.Range(0, 7)
            .Select(request.WeekStart.AddDays)
            .Select(date => $"- {date.DayOfWeek}: {date:yyyy-MM-dd}"));
        return $"Referenced sources:\n{sources}\nWeekday mapping:\n{dates}\nPlanner instructions:\n{request.Instructions}";
    }

    private static List<ExternalRecipeTools.RecipeResearchRequest> ParseResearchPlan(
        string? text,
        MealSuggestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return [];
            }

            var plan = JsonSerializer.Deserialize<ResearchPlan>(
                text[start..(end + 1)],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var allowedHosts = request.SourceConstraints.Select(constraint => NormalizeHost(constraint.Host))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var normalized = (plan?.Requests ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Query)
                    && allowedHosts.Contains(NormalizeHost(item.Site)))
                .Select(item =>
                {
                    var site = NormalizeHost(item.Site);
                    var dates = (item.Dates ?? []).Where(date => DateOnly.TryParse(date, out _))
                        .Distinct(StringComparer.Ordinal).ToList();
                    return item with
                    {
                        Site = site,
                        Dates = dates,
                        Course = string.IsNullOrWhiteSpace(item.Course) ? "main" : item.Course.Trim().ToLowerInvariant(),
                        CandidateCount = Math.Clamp(Math.Max(item.CandidateCount, dates.Count), 1, 6)
                    };
                })
                .ToList();

            // The model may express one logical request as several dated requests (for example,
            // one Laura's Bakery dessert for Friday and another for Sunday). Merge those while
            // retaining genuinely different requests against the same site, such as vegetarian
            // Dagelijkse Kost dishes plus a separate chicken dish.
            var merged = normalized
                .GroupBy(item => string.Join('\u001f',
                    item.Site,
                    item.Course,
                    Regex.Replace(item.Query.Trim().ToLowerInvariant(), @"\s+", " ")),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first = group.First();
                    var dates = group.SelectMany(item => item.Dates ?? [])
                        .Distinct(StringComparer.Ordinal).ToList();
                    return first with
                    {
                        Dates = dates,
                        CandidateCount = Math.Clamp(
                            Math.Max(dates.Count, group.Max(item => item.CandidateCount)), 1, 6)
                    };
                })
                .ToList();

            // A research model can over-attach a nearby source to an adjacent request. In
            // "4 vegetarian dishes from @Source, and 1 chicken on Friday", for example,
            // the explicit source scope is four dishes, not five. When one request exactly
            // matches that explicit count, prefer it over extra inferred requests for the
            // same source. Separate requests whose combined count is explicitly requested
            // are retained.
            merged = merged
                .GroupBy(item => item.Site, StringComparer.OrdinalIgnoreCase)
                .SelectMany(group =>
                {
                    var constraint = request.SourceConstraints.First(source =>
                        string.Equals(NormalizeHost(source.Host), group.Key, StringComparison.OrdinalIgnoreCase));
                    var explicitCount = InferExplicitSourceCount(request.Instructions, constraint);
                    var exact = explicitCount > 0
                        ? group.FirstOrDefault(item => item.CandidateCount == explicitCount)
                        : null;
                    IEnumerable<ExternalRecipeTools.RecipeResearchRequest> selected =
                        exact != null && group.Sum(item => item.CandidateCount) > explicitCount
                            ? new[] { exact }
                            : group;
                    return selected;
                })
                .ToList();

            foreach (var sourceGroup in merged.GroupBy(item => item.Site, StringComparer.OrdinalIgnoreCase))
            {
                var singleRequestForSource = sourceGroup.Count() == 1;
                foreach (var item in sourceGroup.ToList())
                {
                    var constraint = request.SourceConstraints.First(source =>
                        string.Equals(NormalizeHost(source.Host), item.Site, StringComparison.OrdinalIgnoreCase));
                    var dates = (item.Dates ?? []).ToList();
                    var explicitCount = singleRequestForSource
                        ? InferExplicitSourceCount(request.Instructions, constraint)
                        : 0;
                    var candidateCount = Math.Clamp(
                        Math.Max(item.CandidateCount, Math.Max(dates.Count, explicitCount)), 1, 6);
                    if (dates.Count > 0 && dates.Count < candidateCount)
                    {
                        dates.AddRange(InferExplicitSourceDates(request, constraint)
                            .Where(date => !dates.Contains(date, StringComparer.Ordinal))
                            .Take(candidateCount - dates.Count));
                    }

                    var index = merged.IndexOf(item);
                    merged[index] = item with
                    {
                        Dates = dates,
                        CandidateCount = Math.Max(candidateCount, dates.Count)
                    };
                }
            }

            return merged.Take(4).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int InferExplicitSourceCount(string? instructions, SourceConstraint constraint)
    {
        if (string.IsNullOrWhiteSpace(instructions))
        {
            return 0;
        }

        var markers = new[] { $"@[{constraint.Name}]", $"@{constraint.Host}" };
        var index = markers.Select(marker => instructions.IndexOf(marker, StringComparison.OrdinalIgnoreCase))
            .Where(position => position >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (index < 0)
        {
            return 0;
        }

        // The closest small number to the source mention is the source-specific count in
        // natural prompts such as "4 dishes from @[Source]" or "@[Source] for 4 days".
        // Taking the largest number in the whole sentence incorrectly mixes adjacent sources.
        var closest = Regex.Matches(instructions, @"\b([1-6])\b")
            .Select(match => new
            {
                Count = int.Parse(match.Groups[1].Value),
                Distance = Math.Abs((match.Index + (match.Length / 2)) - index)
            })
            .OrderBy(match => match.Distance)
            .FirstOrDefault();
        return closest?.Count ?? 0;
    }

    private static IReadOnlyList<string> InferExplicitSourceDates(
        MealSuggestionRequest request,
        SourceConstraint constraint)
    {
        if (string.IsNullOrWhiteSpace(request.Instructions))
        {
            return [];
        }
        var instructions = request.Instructions;
        var markers = new[] { $"@[{constraint.Name}]", $"@{constraint.Host}" };
        var index = markers.Select(marker => instructions.IndexOf(marker, StringComparison.OrdinalIgnoreCase))
            .Where(position => position >= 0).DefaultIfEmpty(-1).Min();
        if (index < 0)
        {
            return [];
        }
        var start = index;
        while (start > 0 && ".!?;\r\n".IndexOf(instructions[start - 1]) < 0) start--;
        var end = index;
        while (end < instructions.Length && ".!?;\r\n".IndexOf(instructions[end]) < 0) end++;
        var clause = instructions[start..end];
        var names = new Dictionary<DayOfWeek, string[]>
        {
            [DayOfWeek.Monday] = ["monday", "maandag"],
            [DayOfWeek.Tuesday] = ["tuesday", "dinsdag"],
            [DayOfWeek.Wednesday] = ["wednesday", "woensdag"],
            [DayOfWeek.Thursday] = ["thursday", "donderdag"],
            [DayOfWeek.Friday] = ["friday", "vrijdag"],
            [DayOfWeek.Saturday] = ["saturday", "zaterdag"],
            [DayOfWeek.Sunday] = ["sunday", "zondag"]
        };
        return names
            .Where(entry => entry.Value.Any(name => Regex.IsMatch(
                clause, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            .Select(entry => Enumerable.Range(0, 7).Select(request.WeekStart.AddDays)
                .Single(date => date.DayOfWeek == entry.Key).ToString("yyyy-MM-dd"))
            .ToList();
    }

    private static string FormatCandidates(
        IReadOnlyList<ExternalRecipeTools.GetRecipeResult> candidates)
        => JsonSerializer.Serialize(candidates
            .Where(candidate => candidate.Error == null)
            .Select(candidate => new
            {
                candidateId = candidate.CandidateId,
                sourceSite = candidate.SourceSite,
                title = candidate.Title,
                candidate.TotalTimeMinutes,
                candidate.Servings,
                ingredients = candidate.Ingredients?.Take(30),
                instructions = candidate.Instructions?.Take(10)
            }));

    private static bool MeetsResearchRequirements(
        WeekSuggestionsPayload payload,
        IExternalRecipeSession session,
        IReadOnlyList<ExternalRecipeTools.RecipeResearchRequest> requirements,
        out string issue)
    {
        var issues = new List<string>();
        foreach (var requirement in requirements)
        {
            var selected = new List<(DaySuggestionPayload Suggestion, ExternalRecipeCandidate Candidate)>();
            foreach (var suggestion in payload.Suggestions ?? [])
            {
                if (session.TryResolveCandidate(suggestion.ExternalCandidateId, out var candidate)
                    && candidate != null
                    && Uri.TryCreate(candidate.SourceUrl, UriKind.Absolute, out var uri)
                    && string.Equals(NormalizeHost(uri.Host), NormalizeHost(requirement.Site),
                        StringComparison.OrdinalIgnoreCase)
                    && CandidateMatchesRequirement(candidate.Title, candidate.Ingredients, requirement))
                {
                    selected.Add((suggestion, candidate));
                }
            }
            var distinctCount = selected
                .Select(item => item.Suggestion.ExternalCandidateId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (distinctCount != requirement.CandidateCount)
            {
                issues.Add($"use exactly {requirement.CandidateCount} distinct verified candidate(s) from {requirement.Site}, not {distinctCount}");
            }

            foreach (var requiredDate in requirement.Dates ?? [])
            {
                var dateMatched = selected.Any(item =>
                    string.Equals(item.Suggestion.Date, requiredDate, StringComparison.Ordinal)
                    && (string.IsNullOrWhiteSpace(requirement.Course)
                        || string.Equals(item.Suggestion.Course ?? "main", requirement.Course, StringComparison.OrdinalIgnoreCase)));
                if (!dateMatched)
                {
                    issues.Add($"place a verified {requirement.Site} {requirement.Course ?? "dish"} on {requiredDate}");
                }
            }
        }

        issue = string.Join("; ", issues);
        return issues.Count == 0;
    }

    private WeekSuggestionsPayload RepairResearchRequirements(
        WeekSuggestionsPayload payload,
        IReadOnlyList<ExternalRecipeTools.RecipeResearchRequest> requirements,
        IReadOnlyList<ExternalRecipeTools.GetRecipeResult> candidates,
        MealSuggestionRequest request,
        string requestId)
    {
        var items = (payload.Suggestions ?? []).ToList();
        var changed = false;
        foreach (var requirement in requirements)
        {
            var available = candidates
                .Where(candidate => candidate.Error == null
                    && !string.IsNullOrWhiteSpace(candidate.Title)
                    && string.Equals(NormalizeHost(candidate.SourceSite ?? ""),
                        NormalizeHost(requirement.Site), StringComparison.OrdinalIgnoreCase)
                    && CandidateMatchesRequirement(
                        candidate.Title!, candidate.Ingredients ?? [], requirement))
                .DistinctBy(candidate => candidate.CandidateId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (available.Count == 0)
            {
                continue;
            }

            var candidateIds = available.Select(candidate => candidate.CandidateId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allSourceCandidateIds = candidates
                .Where(candidate => candidate.Error == null
                    && string.Equals(NormalizeHost(candidate.SourceSite ?? ""),
                        NormalizeHost(requirement.Site), StringComparison.OrdinalIgnoreCase))
                .Select(candidate => candidate.CandidateId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedDates = items
                .Where(item => item.ExternalCandidateId != null
                    && candidateIds.Contains(item.ExternalCandidateId)
                    && !string.IsNullOrWhiteSpace(item.Date))
                .Select(item => item.Date!)
                .Where(date => DateOnly.TryParse(date, out _))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var targetDates = (requirement.Dates ?? [])
                .Where(date => DateOnly.TryParse(date, out _))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (targetDates.Count == 0)
            {
                targetDates.AddRange(selectedDates.Take(requirement.CandidateCount));
            }
            targetDates.AddRange(request.DaysToFill
                .Select(date => date.ToString("yyyy-MM-dd"))
                .Where(date => !targetDates.Contains(date, StringComparer.Ordinal))
                .Take(requirement.CandidateCount - targetDates.Count));
            targetDates = targetDates.Take(requirement.CandidateCount).ToList();

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var course = string.IsNullOrWhiteSpace(requirement.Course) ? "main" : requirement.Course!;
            var removedWrongCourse = items.RemoveAll(item =>
                item.ExternalCandidateId != null
                && candidateIds.Contains(item.ExternalCandidateId)
                && !string.Equals(item.Course ?? "main", course, StringComparison.OrdinalIgnoreCase));
            changed |= removedWrongCourse > 0;
            foreach (var date in targetDates)
            {
                var existing = items.FirstOrDefault(item =>
                    string.Equals(item.Date, date, StringComparison.Ordinal)
                    && string.Equals(item.Course ?? "main", course, StringComparison.OrdinalIgnoreCase)
                    && item.ExternalCandidateId != null
                    && candidateIds.Contains(item.ExternalCandidateId)
                    && used.Add(item.ExternalCandidateId));
                if (existing != null)
                {
                    continue;
                }

                var replacement = available.FirstOrDefault(candidate => used.Add(candidate.CandidateId));
                if (replacement == null)
                {
                    break;
                }
                items.RemoveAll(item =>
                    string.Equals(item.Date, date, StringComparison.Ordinal)
                    && string.Equals(item.Course ?? "main", course, StringComparison.OrdinalIgnoreCase));
                items.Add(new DaySuggestionPayload(
                    Date: date,
                    DishName: replacement.Title,
                    RecipeTitle: null,
                    Reason: $"Verified distinct recipe from {requirement.Site}",
                    ExternalCandidateId: replacement.CandidateId,
                    MealType: "dinner",
                    Course: course,
                    AttendeeIds: []));
                changed = true;
            }
            var removedExtras = items.RemoveAll(item =>
                item.ExternalCandidateId != null
                && allSourceCandidateIds.Contains(item.ExternalCandidateId)
                && string.Equals(item.Course ?? "main", course, StringComparison.OrdinalIgnoreCase)
                && item.Date != null
                && !targetDates.Contains(item.Date, StringComparer.Ordinal));
            changed |= removedExtras > 0;
        }

        if (changed)
        {
            logger.LogWarning(
                "[{RequestId}] Deterministically repaired unresolved external source requirements using distinct verified candidates; no search or rules backfill was used for those slots",
                requestId);
        }
        return new WeekSuggestionsPayload(items);
    }

    private static bool CandidateMatchesRequirement(
        string title,
        IReadOnlyList<string> ingredients,
        ExternalRecipeTools.RecipeResearchRequest requirement)
    {
        var query = requirement.Query.ToLowerInvariant();
        if (!query.Contains("vegetar", StringComparison.Ordinal)
            && !query.Contains("vegan", StringComparison.Ordinal))
        {
            return true;
        }
        var normalizedTitle = title.ToLowerInvariant();
        if (normalizedTitle.Contains("vegetar", StringComparison.Ordinal)
            || normalizedTitle.Contains("vegan", StringComparison.Ordinal))
        {
            return true;
        }
        var text = $" {normalizedTitle} {string.Join(" ", ingredients).ToLowerInvariant()} ";
        return !new[]
        {
            " chicken ", " kip ", " poulet ", " beef ", " rund ", " pork ", " varken ",
            " bacon ", " ham ", " fish ", " vis ", " salmon ", " zalm ", " tuna ", " tonijn ",
            " gehakt ", " meat ", " vlees "
        }.Any(text.Contains);
    }

    private static bool MeetsUniquenessRequirement(
        WeekSuggestionsPayload payload,
        out string issue)
    {
        var duplicates = (payload.Suggestions ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.DishName))
            .GroupBy(item => !string.IsNullOrWhiteSpace(item.ExternalCandidateId)
                    ? $"candidate:{item.ExternalCandidateId!.Trim()}"
                    : $"dish:{item.DishName!.Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Date).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => group.First().DishName!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        issue = duplicates.Count == 0
            ? ""
            : $"use a different recipe on each date; repeated: {string.Join(", ", duplicates)}";
        return duplicates.Count == 0;
    }

    private static bool MeetsExplicitDatedDishRequirements(
        WeekSuggestionsPayload payload,
        MealSuggestionRequest request,
        out string issue)
    {
        var missing = GetExplicitChickenDates(request)
            .Where(date => !(payload.Suggestions ?? []).Any(item =>
                string.Equals(item.Date, date, StringComparison.Ordinal)
                && string.Equals(item.Course ?? "main", "main", StringComparison.OrdinalIgnoreCase)
                && ContainsChicken(item.DishName, item.RecipeTitle)))
            .ToList();
        issue = missing.Count == 0
            ? ""
            : $"place the explicitly requested chicken main on {string.Join(", ", missing)}";
        return missing.Count == 0;
    }

    private WeekSuggestionsPayload RepairExplicitDatedDishRequirements(
        WeekSuggestionsPayload payload,
        MealSuggestionRequest request,
        string requestId)
    {
        var missingDates = GetExplicitChickenDates(request)
            .Where(date => !(payload.Suggestions ?? []).Any(item =>
                string.Equals(item.Date, date, StringComparison.Ordinal)
                && string.Equals(item.Course ?? "main", "main", StringComparison.OrdinalIgnoreCase)
                && ContainsChicken(item.DishName, item.RecipeTitle)))
            .ToList();
        if (missingDates.Count == 0)
        {
            return payload;
        }

        var recipe = request.KnownRecipes.FirstOrDefault(item => ContainsChicken(item.Title, null));
        if (recipe == null)
        {
            return payload;
        }

        var items = (payload.Suggestions ?? []).ToList();
        foreach (var date in missingDates)
        {
            items.RemoveAll(item => string.Equals(item.Date, date, StringComparison.Ordinal)
                && string.Equals(item.Course ?? "main", "main", StringComparison.OrdinalIgnoreCase));
            items.Add(new DaySuggestionPayload(
                date,
                recipe.Title,
                recipe.Title,
                "Explicitly requested chicken dish; review or replace individual portions for dietary preferences.",
                null,
                MealType: "dinner",
                Course: "main",
                AttendeeIds: []));
        }
        logger.LogWarning(
            "[{RequestId}] Deterministically restored {Count} explicitly dated chicken main(s) from the recipe store after model correction displaced them",
            requestId, missingDates.Count);
        return payload with { Suggestions = items };
    }

    private static IReadOnlyList<string> GetExplicitChickenDates(MealSuggestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Instructions))
        {
            return [];
        }
        var text = request.Instructions;
        var chickenMatches = Regex.Matches(text, @"\b(chicken|kip)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (chickenMatches.Count == 0)
        {
            return [];
        }
        var weekdayNames = new Dictionary<DayOfWeek, string[]>
        {
            [DayOfWeek.Monday] = ["monday", "maandag"],
            [DayOfWeek.Tuesday] = ["tuesday", "dinsdag"],
            [DayOfWeek.Wednesday] = ["wednesday", "woensdag"],
            [DayOfWeek.Thursday] = ["thursday", "donderdag"],
            [DayOfWeek.Friday] = ["friday", "vrijdag"],
            [DayOfWeek.Saturday] = ["saturday", "zaterdag"],
            [DayOfWeek.Sunday] = ["sunday", "zondag"]
        };
        var dates = new List<string>();
        foreach (var (day, names) in weekdayNames)
        {
            var weekdayMatches = names.SelectMany(name => Regex.Matches(text, $@"\b{Regex.Escape(name)}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Cast<Match>());
            var linked = weekdayMatches.Any(weekday => chickenMatches.Cast<Match>().Any(chicken =>
            {
                var start = Math.Min(weekday.Index, chicken.Index);
                var end = Math.Max(weekday.Index + weekday.Length, chicken.Index + chicken.Length);
                return end - start <= 80 && !Regex.IsMatch(text[start..end], @"[.!?;\r\n]");
            }));
            if (!linked)
            {
                continue;
            }
            dates.AddRange(request.DaysToFill.Where(date => date.DayOfWeek == day)
                .Select(date => date.ToString("yyyy-MM-dd")));
        }
        return dates.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool ContainsChicken(string? first, string? second)
        => Regex.IsMatch($"{first} {second}", @"\b(chicken|kip)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool InstructionsAllowRepeats(string? instructions)
        => !string.IsNullOrWhiteSpace(instructions)
            && Regex.IsMatch(instructions,
                @"\b(same|repeat|again|every\s+day|each\s+day|elke\s+dag|iedere\s+dag|herhaal|opnieuw)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string NormalizeHost(string host)
    {
        var normalized = host.Trim().ToLowerInvariant();
        return normalized.StartsWith("www.", StringComparison.Ordinal) ? normalized[4..] : normalized;
    }

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
