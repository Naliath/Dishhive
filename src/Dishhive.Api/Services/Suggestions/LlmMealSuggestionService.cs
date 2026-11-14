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
    AiPlanningMetricsStore? metricsStore = null,
    IRecipeDishMatcher? dishMatcher = null) : IMealSuggestionService
{
    public bool IsEnabled => true;
    public MealSuggestionResult? LastResult { get; private set; }

    public async Task<IReadOnlyList<MealSuggestion>> SuggestAsync(
        MealSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DaysToFill.Count == 0)
        {
            LastResult = new(PlanCompletionStatus.Complete, [], []);
            return [];
        }

        var requestId = Guid.NewGuid().ToString("N")[..8];
        using var inputSnapshot = AiInputSnapshotScope.ForPlanning(request);
        var stopwatch = Stopwatch.StartNew();
        var run = new AiPlanningRun
        {
            RequestId = requestId,
            StartedAt = DateTime.UtcNow,
            Provider = options.Provider,
            Model = options.Model,
            ConfigFingerprint = options.ExecutionFingerprint,
            Instructions = options.RetainDiagnosticText ? request.Instructions : null,
            RequestedDays = request.DaysToFill.Count
        };
        IExternalRecipeSession? externalRecipeSession = null;
        List<ExternalRecipeTools.RecipeResearchRequest> researchRequirements = [];
        IReadOnlyList<ExternalRecipeTools.GetRecipeResult> verifiedResearchCandidates = [];
        var requiresInterpretation = !string.IsNullOrWhiteSpace(request.Instructions)
            || request.SourceConstraints.Count > 0
            || request.CollectionConstraints.Count > 0
            || request.WeekPlan.Any(meal => request.DaysToFill.Contains(meal.Date)
                && !string.IsNullOrWhiteSpace(meal.VagueInstruction));

        IReadOnlyList<MealSuggestion> Complete(
            IReadOnlyList<MealSuggestion> result,
            string outcome,
            IReadOnlyList<UnmetPlanningRequirement>? additionalUnmet = null)
        {
            var selectionDiagnostics = Array.Empty<UnmetPlanningRequirement>();
            if (request.Intent is not null)
            {
                var candidates = verifiedResearchCandidates.Select(candidate =>
                    externalRecipeSession?.TryResolveCandidate(candidate.CandidateId, out var resolved) == true ? resolved : null)
                    .OfType<ExternalRecipeCandidate>().ToList();
                var selection = PlanningCandidateSelector.Select(Finalize(result, request), request.Intent,
                    request, candidates, cancellationToken);
                result = Finalize(selection.Suggestions, request);
                selectionDiagnostics = selection.Diagnostics.ToArray();
            }
            var validation = PlanningIntentValidator.ValidateFinal(
                result, request.Intent ?? new PlanningIntent(), request);
            var unmet = validation.UnmetRequirements
                .Concat(additionalUnmet ?? [])
                .Concat(selectionDiagnostics)
                .DistinctBy(issue => (issue.Id, issue.Message))
                .ToList();
            result = validation.Suggestions;
            run.Outcome = unmet.Count == 0 ? outcome : result.Count == 0 ? "unable" : "partial";
            run.SuggestedItems = result.Count;
            run.ExternalSuggestions = result.Count(item => item.SourceUrl != null);
            run.FallbackSuggestions = result.Count(item => item.Source == MealSuggestionSource.RulesFallback);
            LastResult = new(
                unmet.Count == 0 ? PlanCompletionStatus.Complete
                    : result.Count == 0 ? PlanCompletionStatus.Unable
                    : PlanCompletionStatus.Partial,
                result,
                unmet) { Intent = request.Intent, OperationId = requestId };
            return result;
        }

        try
        {
            if (request.UnresolvedCollectionNames.Count > 0)
                return Complete([], "unable", request.UnresolvedCollectionNames.Select(name =>
                    new UnmetPlanningRequirement("collection-unresolved", $"Referenced collection #[{name}] could not be found").WithText("planningIssue.collectionUnresolved", ("name", name)))
                    .ToList());

            // Nested research extraction/facts calls inherit this deadline, id and provider-attempt counter.
            using var operation = executionGate.BeginOperation(cancellationToken, requestId);
            var gateDecision = await executionGate.EnsureAvailableAsync(
                AiTaskKind.WeekPlanning, capability, operation.Token);
            run.CapabilityWaitMs = gateDecision.WaitMs;
            if (!gateDecision.Allowed)
            {
                logger.LogWarning(
                    "[{RequestId}] Model {Provider}/{Model} failed its capability test (verdict={Verdict}); using rules fallback without calling it",
                    requestId, options.Provider, options.Model, gateDecision.Reason);
                if (requiresInterpretation)
                    return Complete([], "capabilityFallback",
                        [new UnmetPlanningRequirement("intent-unavailable", "Explicit planning requirements could not be interpreted").WithText("planningIssue.intentUnavailable")]);
                return Complete(Finalize(await fallback.SuggestAsync(request, cancellationToken), request), "capabilityFallback");
            }

            PlanningIntent? intent = requiresInterpretation ? null : new PlanningIntent();
            if (requiresInterpretation)
            {
                var intentStopwatch = Stopwatch.StartNew();
                var intentMessages = new List<ChatMessage>
                {
                    new(ChatRole.System, PlanningIntentContract.SystemPrompt),
                    new(ChatRole.User, BuildPlanningIntentPrompt(request))
                };
                ChatResponse? intentResponse = null;
                var maxIntentAttempts = executionGate.Profile(AiTaskKind.PlanningIntent).MaxRetries + 1;
                for (var intentAttempt = 1; intentAttempt <= maxIntentAttempts && intent is null; intentAttempt++)
                {
                    var intentExecution = await executionGate.ExecuteValidatedAsync(new AiExecutionRequest(
                        AiTaskKind.PlanningIntent, intentMessages, PlanningIntentContract.ContractVersion,
                        requestId, intentAttempt, PlanningIntentContract.JsonSchemaFormat), response =>
                        {
                            var parsed = PlanningIntentContract.ParseWithErrors(response.Text, request);
                            // A counted, undated clause can accidentally be assigned to an
                            // incompatible explicitly dated meal. Recheck that interpretation
                            // within the existing attempt allowance before declaring it impossible.
                            // The final valid interpretation still preserves genuine contradictions.
                            if (parsed.Intent is { } candidate && intentAttempt < maxIntentAttempts)
                            {
                                var conflicts = PlanningIntentValidator.Contradictions(candidate);
                                if (conflicts.Count > 0)
                                {
                                    var conflictIds = conflicts.Select(issue => issue.Id).ToHashSet();
                                    var context = AiJsonContract.Serialize(candidate.Constraints
                                        .Where(constraint => conflictIds.Contains(constraint.Id)).Take(6)
                                        .Select(constraint => new { constraint.Id, constraint.Dates,
                                            constraint.MealType, constraint.Course, constraint.RequiredClasses,
                                            constraint.ExcludedClasses, constraint.AllowedOrigins }));
                                    parsed = new(null, [new("date-allocation",
                                        "Recheck these incompatible interpreted constraints against the original instructions. "
                                        + "Reserve explicit dates before allocating undated counts. Keep every requirement and every explicitly requested date; "
                                        + "if the original request itself is contradictory, preserve that contradiction. Interpreted constraints: " + context)]);
                                }
                            }
                            return new AiValidation<PlanningIntentContract.ParseResult>(parsed,
                                parsed.Intent is not null ? AiTaskOutcome.Success : AiTaskOutcome.ValidationFailure,
                                parsed.Intent is null ? string.Join("; ", parsed.Errors.Select(error => error.Id + ": " + error.Message)) : null);
                        }, cancellationToken);
                    intentResponse = intentExecution.Response;
                    RecordCompletion(run, intentResponse, intentStopwatch.ElapsedMilliseconds);
                    var interpretation = intentExecution.Value!;
                    intent = interpretation.Intent;
                    if (intent is not null) break;
                    run.ParseFailures++;
                    intentMessages.Add(new ChatMessage(ChatRole.User,
                        "Correct these planning-intent validation errors. Preserve every original requirement; return only corrected intent JSON.\n"
                        + AiJsonContract.Serialize(interpretation.Errors.Take(6))));
                }
                intentStopwatch.Stop();
                if (intent == null)
                {
                    logger.LogWarning(
                        "[{RequestId}] AI planning intent remained invalid after correction; visible={Visible}",
                        requestId, FormatForLog(intentResponse?.Text, 2000));
                    return Complete([], "intentFallback",
                        [new UnmetPlanningRequirement("intent-invalid", "Explicit planning requirements could not be normalized safely").WithText("planningIssue.intentInvalid")]);
                }
                logger.LogInformation(
                    "[{RequestId}] AI planning intent completed in {ElapsedMs}ms with {ConstraintCount} constraint(s)",
                    requestId, intentStopwatch.ElapsedMilliseconds, intent.Constraints.Count);
                logger.LogDebug(
                    "[{RequestId}] Normalized planning intent: {Intent}",
                    requestId, PlanningIntentContract.FormatForModel(intent));
            }
            request = request with { Intent = intent };
            var contradictions = PlanningIntentValidator.Contradictions(intent!);
            if (contradictions.Count > 0) return Complete([], "unable", contradictions);
            run.NormalizedIntentJson = options.RetainDiagnosticText
                ? PlanningIntentContract.FormatForModel(intent!) : null;

            intent = await PlanningDishIdentity.ApproveAsync(intent!, request,
                request.KnownRecipes.Select(recipe => new MealSuggestion
                {
                    RecipeId = recipe.Id, DishName = recipe.Title,
                    ExternalIngredients = request.RecipeAllergens.GetValueOrDefault(recipe.Id)?.Ingredients ?? [],
                    CandidateVersion = SuggestionCandidateVersion.ForLocal(recipe)
                }).Concat(request.AvailableFrozenItems.Select(item => new MealSuggestion
                { DishName = item.Name, FreezyItemRef = item.Id })).ToList(), dishMatcher, requestId,
                executionGate.Profile(AiTaskKind.WeekPlanning).MaxRetries + 1, operation.Token);
            request = request with { Intent = intent };

            var useExternalResearch = externalRecipeSessions.IsConfigured
                && intent!.Constraints.Any(item => item.SourceHost != null);
            run.UsedExternalResearch = useExternalResearch;

            logger.LogInformation(
                "[{RequestId}] AI suggestion request starting: {DayCount} day(s) to fill, externalResearch={UseExternalResearch}, {Provider}/{Model}",
                requestId, request.DaysToFill.Count, useExternalResearch, options.Provider, options.Model);

            var effectivePrompt = await promptProvider.GetEffectiveSystemPromptAsync(operation.Token);
            var constraintIdsField = AiJsonContract.PropertyName<DaySuggestionPayload>(
                nameof(DaySuggestionPayload.ConstraintIds));
            var intentPrompt =
                $"Authoritative normalized planning intent follows. Satisfy every constraint and copy each satisfied constraint id into {constraintIdsField} on the corresponding suggestion.\n"
                + PlanningIntentContract.FormatForModel(intent!);
            string? researchPrompt = null;

            if (useExternalResearch)
            {
                externalRecipeSession = externalRecipeSessions.Create(request.SourceConstraints, requestId, logger);
                var researchRequests = intent!.Constraints
                    .Where(item => item.SourceHost != null)
                    .Select(item => new ExternalRecipeTools.RecipeResearchRequest(
                        item.Id,
                        item.SearchQuery ?? item.DishRequest ?? "recipe",
                        item.SourceHost!,
                        item.Count,
                        item.Dates.Select(date => date.ToString("yyyy-MM-dd")).ToList(),
                        AiJsonContract.EnumName(item.Course),
                        item.RequiredClasses,
                        item.ExcludedClasses.Concat(request.Members.SelectMany(member => member.Allergies)
                            .SelectMany(allergy => allergy.ExcludedClasses)).Distinct().ToList(),
                        item.SearchQueries, item.DishRequest ?? item.SearchQuery))
                    .ToList();

                var verifiedCandidates = await externalRecipeSession.ResearchAsync(researchRequests, operation.Token);
                verifiedResearchCandidates = verifiedCandidates;
                researchRequirements = researchRequests;
                intent = PlanningDishIdentity.WithExternalMatches(intent!, verifiedCandidates
                    .Select(candidate => externalRecipeSession.TryResolveCandidate(candidate.CandidateId, out var resolved) ? resolved : null)
                    .OfType<ExternalRecipeCandidate>().ToList());
                request = request with { Intent = intent };
                var sourceSiteField = AiJsonContract.PropertyName<CandidatePromptPayload>(
                    nameof(CandidatePromptPayload.SourceSite));
                var candidateIdField = AiJsonContract.PropertyName<CandidatePromptPayload>(
                    nameof(CandidatePromptPayload.CandidateId));
                var externalCandidateIdField = AiJsonContract.PropertyName<DaySuggestionPayload>(
                    nameof(DaySuggestionPayload.ExternalCandidateId));
                researchPrompt =
                    "Dishhive deterministically completed the required source research. "
                    + $"Use these verified candidates for every source-backed dish, matching {sourceSiteField}, and copy {candidateIdField} into {externalCandidateIdField}. "
                    + "Do not invent candidate ids or replace requested source dishes with local recipes.\n"
                    + FormatCandidates(verifiedCandidates);
            }

            var fixedPrompts = researchPrompt is null
                ? new[] { effectivePrompt, intentPrompt }
                : new[] { effectivePrompt, intentPrompt, researchPrompt };
            var userPromptBudget = MealSuggestionPromptBuilder.FitUserPromptTokenBudget(
                options.MaxPromptTokens, options.MaxInputTokens, fixedPrompts);
            var userPrompt = MealSuggestionPromptBuilder.BuildUserPrompt(request, userPromptBudget);
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, effectivePrompt),
                new(ChatRole.User, userPrompt),
                new(ChatRole.User, intentPrompt)
            };
            if (researchPrompt is not null)
            {
                messages.Add(new ChatMessage(ChatRole.User, researchPrompt));
            }

            WeekSuggestionsPayload? payload = null;
            var maxCompletionRetries = executionGate.Profile(
                AiTaskKind.WeekPlanning, useExternalResearch).MaxRetries;
            var useStructuredOutput = true;
            for (var attempt = 0; attempt <= maxCompletionRetries; attempt++)
            {
                var attemptStopwatch = Stopwatch.StartNew();
                var completion = await executionGate.ExecuteValidatedAsync(new AiExecutionRequest(
                    AiTaskKind.WeekPlanning,
                    messages,
                    MealSuggestionResponseContract.ContractVersion,
                    requestId,
                    attempt + 1,
                    MealSuggestionResponseContract.JsonSchemaFormat,
                    useStructuredOutput ? AiResponseFormatPolicy.WhenSupported : AiResponseFormatPolicy.None,
                    UseExtendedTimeout: useExternalResearch), response =>
                    {
                        var parsed = MealSuggestionResponseContract.Parse(response.Text);
                        if (parsed?.Suggestions is null) return AiValidation<WeekSuggestionsPayload>.Invalid(
                            AiTaskOutcome.ParseFailure, "Unparseable week-suggestions payload");
                        var issues = PlanningIntentValidator.Validate(parsed, intent!, request, externalRecipeSession);
                        return issues.Count == 0 ? AiValidation<WeekSuggestionsPayload>.Valid(parsed)
                            : AiValidation<WeekSuggestionsPayload>.Invalid(AiTaskOutcome.ValidationFailure,
                                string.Join("; ", issues), parsed);
                    }, cancellationToken);
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

                payload = completion.Value;
                if (payload?.Suggestions is not null)
                {
                    if (!completion.Succeeded)
                    {
                        var qualityIssue = completion.Error;
                        logger.LogWarning(
                            "[{RequestId}] AI reply was valid JSON but missed explicit planning requirements: {Issue}",
                            requestId, qualityIssue);
                        if (attempt < maxCompletionRetries)
                        {
                            // Some local OpenAI-compatible servers satisfy a JSON schema
                            // while becoming oddly rigid about the previous field values.
                            // The focused repair already has valid JSON to imitate, so let
                            // prompted JSON prioritize semantic corrections.
                            useStructuredOutput = false;
                            messages.Add(new ChatMessage(ChatRole.Assistant,
                                MealSuggestionResponseContract.BuildRepromptQuote(response.Text ?? string.Empty)));
                            messages.Add(new ChatMessage(
                                ChatRole.User,
                                $"Correct the plan: {qualityIssue}. Re-read the original instructions and authoritative weekday/date mapping. Use the verified candidate ids already supplied, preserve all other requirements, and reply with ONLY the corrected JSON object."));
                            payload = null;
                            continue;
                        }
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
            intent = await PlanningDishIdentity.ApproveAsync(intent!, request, finalized, dishMatcher,
                requestId, 0, operation.Token);
            request = request with { Intent = intent };
            // Model-call validation remains in task telemetry. Product completeness
            // is determined by the resolved domain result after candidate selection.
            return Complete(finalized, finalized.Any(item => item.Source == MealSuggestionSource.RulesFallback)
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
            if (requiresInterpretation && request.Intent is null)
                return Complete([], "errorFallback",
                    [new UnmetPlanningRequirement("intent-unavailable", "Explicit planning requirements could not be interpreted").WithText("planningIssue.intentUnavailable")]);
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
            request.DaysToFill,
            Enumerable.Range(0, 7)
                .Select(request.WeekStart.AddDays)
                .Select(date => new PlanningIntentDate(date.DayOfWeek.ToString(), date))
                .ToList(),
            request.Instructions,
            request.WeekPlan
                .Where(meal => request.DaysToFill.Contains(meal.Date) && !string.IsNullOrWhiteSpace(meal.VagueInstruction))
                .Select(meal => new PlanningIntentDayInstruction(
                    meal.Date, meal.MealType, meal.Course, meal.VagueInstruction!))
                .ToList(),
            request.CollectionConstraints.Select(constraint => new PlanningIntentCollection(
                constraint.Id, constraint.Name, constraint.IsGlobal, constraint.Slots,
                constraint.Dates, constraint.RecipeIds.Count, constraint.RecipeTitles)).ToList(),
            request.KnownRecipes.Select(recipe => new PlanningIntentLocalRecipe(
                recipe.Id, recipe.Title, recipe.OriginalTitle, recipe.TotalTimeMinutes)).ToList(),
            request.Members.SelectMany(member => member.Allergies).SelectMany(allergy => allergy.ExcludedClasses)
                .Distinct().Order().ToList()));
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
                candidate.ResearchConstraintIds,
                candidate.Ingredients?.Take(30).ToList(),
                candidate.Instructions?.Take(10).ToList())));

    private sealed record PlanningIntentPromptInput(
        IReadOnlyList<PlanningIntentSource> ReferencedSources,
        IReadOnlyList<DateOnly> SelectedDates,
        IReadOnlyList<PlanningIntentDate> WeekdayMapping,
        string? PlannerInstructions,
        IReadOnlyList<PlanningIntentDayInstruction> DayInstructions,
        IReadOnlyList<PlanningIntentCollection> ReferencedCollections,
        IReadOnlyList<PlanningIntentLocalRecipe> LocalRecipes,
        IReadOnlyList<IngredientClass> HouseholdAllergyExclusions);

    private sealed record PlanningIntentSource(string Name, string Host);
    private sealed record PlanningIntentDate(string Weekday, DateOnly Date);
    private sealed record PlanningIntentDayInstruction(
        DateOnly Date, MealType MealType, Course Course, string Instruction);
    private sealed record PlanningIntentCollection(
        string Id, string Name, bool IsGlobal, IReadOnlyList<PlanningSlot> Slots,
        IReadOnlyList<DateOnly> Dates, int RecipeCount, IReadOnlyList<string> RecipeTitles);
    private sealed record PlanningIntentLocalRecipe(
        Guid Id, string Title, string? OriginalTitle, int? TotalTimeMinutes);
    private sealed record CandidatePromptPayload(
        string CandidateId,
        string? SourceSite,
        string? Title,
        int? TotalTimeMinutes,
        int? Servings,
        IReadOnlyList<string> ResearchConstraintIds,
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
        var excerpt = value[..half] + $"\nâ€¦ {omitted} character(s) omitted â€¦\n" + value[^half..];
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
