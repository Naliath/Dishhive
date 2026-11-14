using Dishhive.Api.Services.Time;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Ai;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Text.Json;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Which response format the configured model was verified to handle.</summary>
public enum AiResponseMode
{
    /// <summary>No attempt produced parseable JSON â€” the model is unfit for suggestions.</summary>
    None,
    /// <summary>The native json_schema probe returned usable structured output.</summary>
    JsonSchema,
    /// <summary>Only prompt-instructed JSON works (the pre-test default behavior).</summary>
    PromptedJson
}

/// <summary>One verified aspect of the model test, for display on the settings page.</summary>
public record AiModelTestCheck(string Name, bool Passed, string Detail);

/// <summary>Outcome of a capability test run against the configured model.</summary>
public record AiModelTestResult
{
    public required DateTimeOffset TestedAt { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }

    /// <summary>The /models probe answered (advisory â€” completions are the real signal)</summary>
    public bool EndpointReachable { get; init; }

    /// <summary>Configured model id present in /models; null when the list could not be read</summary>
    public bool? ModelListed { get; init; }

    public AiResponseMode ResponseMode { get; init; }

    /// <summary>Whether the medium-complexity evaluation passed; null when never reached</summary>
    public bool? EvaluationPassed { get; init; }

    /// <summary>Whether the model can turn a fuzzy source request into a bounded research plan.</summary>
    public bool? PlanningIntentPassed { get; init; }

    public IReadOnlyList<AiModelTestCheck> Checks { get; init; } = [];

    /// <summary>Rough output speed of the successful completion, for expectation-setting</summary>
    public double? TokensPerSecond { get; init; }

    public long ElapsedMs { get; init; }

    /// <summary>Format evidence only; semantic task eligibility is separate.</summary>
    public bool Viable => ResponseMode != AiResponseMode.None;
    public bool PlanningEligible => Viable && EvaluationPassed == true && PlanningIntentPassed == true;

    /// <summary>failed | warnings | passed â€” the settings page headline</summary>
    public string Verdict => !PlanningEligible
        ? "failed"
        : ModelListed != false ? "passed" : "warnings";
}

/// <summary>
/// The evaluation scenario: a realistic week request with known-correct answers, so the
/// model's reply can be scored. Exposed internal for the tester's unit tests.
/// </summary>
internal sealed record AiModelTestFixture(
    MealSuggestionRequest Request,
    DateOnly CollectionDay,
    IReadOnlyList<string> CollectionTitles,
    DateOnly SpecificDishDay,
    string SpecificDish,
    IReadOnlyList<string> VegetarianTitles);

/// <summary>
/// Probes the configured model's actual fitness for week suggestions (reachable â‰  usable:
/// the /models ping says nothing about JSON discipline or context headroom). Three stages:
/// 1. /models â€” endpoint up, configured model id present (catches "wrong model loaded").
/// 2. A medium-complexity suggestion request â€” first with native json_schema enforcement,
///    then falling back to prompted JSON â€” sized to the full Ai:MaxPromptTokens budget so
///    a too-small context window fails HERE, not on every future planning evening.
/// 3. The reply is scored against known-correct answers: a #[Collection]-constrained day,
///    a specific dish demanded on a specific day, and "two days vegetarian".
/// The verified response mode is reused by LlmMealSuggestionService (schema enforcement
/// when proven, prompted otherwise); a None verdict makes it skip the model entirely.
/// </summary>
public class AiModelTester
{
    private readonly IHouseholdClock _clock;
    private readonly IAiExecutionGate _executionGate;
    private readonly AiOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiModelTester> _logger;

    public AiModelTester(
        IAiExecutionGate executionGate, AiOptions options,
        IHttpClientFactory httpClientFactory, ILogger<AiModelTester> logger, IHouseholdClock clock)
    {
        _executionGate = executionGate;
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _clock = clock;
    }

    /// <summary>JSON schema for the suggestions payload (strict style: all fields required,
    /// nullability via type unions, no extra properties) â€” shared with the LLM service
    /// when schema mode is verified.</summary>
    /// <summary>
    /// Runs the capability test under the given effective system prompt â€” the same
    /// composed prompt production uses, including any user override, so the verdict
    /// describes what actually runs. Virtual so the capability service's persistence
    /// tests can stub the actual probing.
    /// </summary>
    public virtual async Task<AiModelTestResult> RunAsync(
        string effectiveSystemPrompt, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var checks = new List<AiModelTestCheck>();
        var operationId = $"capability-{Guid.NewGuid():N}"[..23];
        using var operation = _executionGate.BeginOperation(cancellationToken, operationId, independent: true);
        cancellationToken = operation.Token;

        try
        {
            var (endpointReachable, modelListed) = await ProbeModelsAsync(cancellationToken);
            checks.Add(new AiModelTestCheck("Endpoint", endpointReachable,
                endpointReachable ? "The AI endpoint answered the /models probe" : "The /models probe failed â€” endpoint may be down"));
            checks.Add(new AiModelTestCheck("Model available", modelListed != false, modelListed switch
            {
                true => $"'{_options.Model}' is served by the endpoint",
                false => $"'{_options.Model}' is NOT in the endpoint's model list â€” is the right model loaded?",
                null => "Could not read the endpoint's model list to verify (not all providers expose it)"
            }));

            var fixture = CreateFixture(_clock.Today);
            var systemPrompt = effectiveSystemPrompt;
            var userPromptBudget = MealSuggestionPromptBuilder.FitUserPromptTokenBudget(
                _options.MaxPromptTokens, _options.MaxInputTokens, systemPrompt);
            var userPrompt = MealSuggestionPromptBuilder.BuildUserPrompt(fixture.Request, userPromptBudget);

            // Native schema enforcement first (hard guarantee when it works), prompted
            // JSON second (the broadly-compatible default). Whichever succeeds becomes
            // the production response mode.
            var mode = AiResponseMode.None;
            var (payload, tokensPerSecond) = await TryCompleteAsync(
                systemPrompt, userPrompt, fixture, MealSuggestionResponseContract.JsonSchemaFormat,
                "json_schema", operationId, 1, cancellationToken);
            if (payload?.Suggestions is not null)
            {
                mode = AiResponseMode.JsonSchema;
            }
            else
            {
                (payload, tokensPerSecond) = await TryCompleteAsync(
                    systemPrompt, userPrompt, fixture, responseFormat: null,
                    "prompted", operationId, 2, cancellationToken);
                if (payload?.Suggestions is not null)
                {
                    mode = AiResponseMode.PromptedJson;
                }
            }

            checks.Add(new AiModelTestCheck("Structured JSON reply", mode != AiResponseMode.None, mode switch
            {
                AiResponseMode.JsonSchema => "The native json_schema probe returned usable structured output",
                AiResponseMode.PromptedJson => "Prompted JSON works (native json_schema was rejected or came back empty)",
                _ => "No attempt produced parseable JSON â€” suggestions would always fall back to the rules engine"
            }));

            bool? evaluationPassed = null;
            bool? planningIntentPassed = null;
            if (payload?.Suggestions is not null)
            {
                var evaluation = Evaluate(payload, fixture);
                checks.AddRange(evaluation);
                evaluationPassed = evaluation.All(c => c.Passed);

                planningIntentPassed = await TestPlanningIntentAsync(operationId, cancellationToken);
                checks.Add(new AiModelTestCheck(
                    "Multilingual planning instructions",
                    planningIntentPassed.Value,
                    planningIntentPassed.Value
                        ? "The model understood counts, dates, courses and sources in English and Dutch"
                        : "The model could not reliably normalize multilingual planning requirements"));
            }

            stopwatch.Stop();
            var result = new AiModelTestResult
            {
                TestedAt = DateTimeOffset.UtcNow,
                Provider = _options.Provider,
                Model = _options.Model,
                EndpointReachable = endpointReachable,
                ModelListed = modelListed,
                ResponseMode = mode,
                EvaluationPassed = evaluationPassed,
                PlanningIntentPassed = planningIntentPassed,
                Checks = checks,
                TokensPerSecond = tokensPerSecond,
                ElapsedMs = stopwatch.ElapsedMilliseconds
            };
            _logger.LogInformation(
                "AI model test finished in {ElapsedMs}ms: verdict={Verdict}, mode={Mode}, evaluation={Evaluation} ({Provider}/{Model})",
                result.ElapsedMs, result.Verdict, result.ResponseMode, evaluationPassed, _options.Provider, _options.Model);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A test run must never take the capability service down with it
            _logger.LogWarning(ex, "AI model test crashed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            checks.Add(new AiModelTestCheck("Test run", false, $"The test itself failed unexpectedly: {ex.Message}"));
            return new AiModelTestResult
            {
                TestedAt = DateTimeOffset.UtcNow,
                Provider = _options.Provider,
                Model = _options.Model,
                ResponseMode = AiResponseMode.None,
                Checks = checks,
                ElapsedMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<bool> TestPlanningIntentAsync(
        string operationId, CancellationToken cancellationToken)
    {
        try
        {
            var request = new MealSuggestionRequest
            {
                WeekStart = new DateOnly(2099, 1, 5),
                DaysToFill = [new DateOnly(2099, 1, 5), new DateOnly(2099, 1, 11)],
                SourceConstraints = [new SourceConstraint { Name = "Example", Host = "recipes.example" }]
            };
            var prompts = new[]
            {
                "Get 2 desserts from @recipes.example, one Monday and one Sunday.",
                "Plan 2 desserts van @recipes.example, een op maandag en een op zondag."
            };
            for (var promptIndex = 0; promptIndex < prompts.Length; promptIndex++)
            {
                var prompt = prompts[promptIndex];
                var execution = await _executionGate.ExecuteValidatedAsync(new AiExecutionRequest(
                    AiTaskKind.PlanningIntent,
                    [
                        new ChatMessage(ChatRole.System, PlanningIntentContract.SystemPrompt),
                        new ChatMessage(ChatRole.User,
                            "Referenced source: recipes.example. Selected dates: 2099-01-05 and 2099-01-11. " +
                            "Weekday mapping: Monday is 2099-01-05 and Sunday is 2099-01-11. Instructions: " + prompt)
                    ],
                    $"capability-{PlanningIntentContract.ContractVersion}", operationId, promptIndex + 1,
                    ResponseFormatPolicy: AiResponseFormatPolicy.None, CapabilityProbe: true), response =>
                    {
                        var intent = PlanningIntentContract.Parse(response.Text, request);
                        var constraints = intent?.Constraints.Where(constraint => constraint.SourceHost == "recipes.example"
                            && constraint.MealType == MealType.Dinner && constraint.Course == Course.Dessert).ToList() ?? [];
                        var dates = constraints.SelectMany(constraint => constraint.Dates).ToHashSet();
                        return constraints.Sum(constraint => constraint.Count) == 2
                            && dates.Contains(new DateOnly(2099, 1, 5)) && dates.Contains(new DateOnly(2099, 1, 11))
                                ? AiValidation<PlanningIntent>.Valid(intent!)
                                : AiValidation<PlanningIntent>.Invalid(AiTaskOutcome.ValidationFailure,
                                    "Multilingual planning intent did not satisfy the fixture");
                    }, cancellationToken);
                if (!execution.Succeeded)
                {
                    _logger.LogWarning("Multilingual planning capability probe failed for prompt {Prompt}; raw response={Response}",
                        prompt, execution.Response.Text);
                    return false;
                }
            }
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "AI external-recipe intent capability check failed");
            return false;
        }
    }

    /// <summary>One completion attempt under the plain-path timeout; null payload on any failure.</summary>
    private async Task<(WeekSuggestionsPayload? Payload, double? TokensPerSecond)> TryCompleteAsync(
        string systemPrompt, string userPrompt, AiModelTestFixture fixture, ChatResponseFormat? responseFormat,
        string attemptName, string operationId, int attempt, CancellationToken cancellationToken)
    {

        var attemptStopwatch = Stopwatch.StartNew();
        try
        {
            var execution = await _executionGate.ExecuteValidatedAsync(new AiExecutionRequest(
                AiTaskKind.CapabilityPlanning,
                [new ChatMessage(ChatRole.System, systemPrompt), new ChatMessage(ChatRole.User, userPrompt)],
                "capability-week-planning-v1",
                operationId,
                attempt,
                ResponseFormat: responseFormat,
                ResponseFormatPolicy: responseFormat is null
                    ? AiResponseFormatPolicy.None
                    : AiResponseFormatPolicy.Force), response =>
                    {
                        var parsed = MealSuggestionResponseContract.Parse(response.Text);
                        if (parsed?.Suggestions is null) return AiValidation<WeekSuggestionsPayload>.Invalid(
                            AiTaskOutcome.ParseFailure, "Capability response was not parseable");
                        var failures = Evaluate(parsed, fixture).Where(check => !check.Passed).ToList();
                        return failures.Count == 0 ? AiValidation<WeekSuggestionsPayload>.Valid(parsed)
                            : AiValidation<WeekSuggestionsPayload>.Invalid(AiTaskOutcome.ValidationFailure,
                                string.Join("; ", failures.Select(check => check.Detail)), parsed);
                    }, cancellationToken);
            var response = execution.Response;
            attemptStopwatch.Stop();

            double? tokensPerSecond = response.Usage?.OutputTokenCount is { } output && attemptStopwatch.Elapsed.TotalSeconds > 0
                ? output / attemptStopwatch.Elapsed.TotalSeconds
                : null;

            var payload = execution.Value;
            _logger.LogInformation(
                "AI model test {Attempt} attempt: parseable={Parseable} in {ElapsedMs}ms (finish={Finish})",
                attemptName, payload?.Suggestions is not null, attemptStopwatch.ElapsedMilliseconds, response.FinishReason);
            return (payload, tokensPerSecond);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(
                "AI model test {Attempt} attempt failed after {ElapsedMs}ms: {Error}",
                attemptName, attemptStopwatch.ElapsedMilliseconds, ex.Message);
            return (null, null);
        }
    }

    /// <summary>
    /// GET {base}/models: endpoint reachable, and is the configured model id in the list?
    /// Advisory only â€” some gateways block the listing while completions work fine, so a
    /// failure here never aborts the completion stages.
    /// </summary>
    private async Task<(bool Reachable, bool? ModelListed)> ProbeModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = ChatClientFactory.ProbeBaseUrl(_options);
            if (baseUrl is null)
            {
                return (false, null);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var http = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, "models"));
            var apiKey = _options.ResolveApiKey();
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization = new("Bearer", apiKey);
            }

            using var response = await http.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null);
            }

            // OpenAI shape: {"data":[{"id":"..."}]} â€” anything else counts as "can't verify"
            try
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
                if (json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    var listed = data.EnumerateArray().Any(m =>
                        m.TryGetProperty("id", out var id)
                        && string.Equals(id.GetString(), _options.Model, StringComparison.OrdinalIgnoreCase));
                    return (true, listed);
                }
                return (true, null);
            }
            catch (JsonException)
            {
                return (true, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (false, null);
        }
    }

    /// <summary>
    /// A realistic planning request with verifiable expectations: Monâ€“Fri of the next week,
    /// a #[Quick Pasta] collection pinned to Wednesday, "serve Chicken curry on Thursday",
    /// and "two days vegetarian" (the recipes carry a Vegetarian/Meat/Fish category, so
    /// this tests instruction-following, not world knowledge). The known-recipes list is
    /// padded with filler titles so the prompt fills the whole MaxPromptTokens budget â€”
    /// a context window too small for real requests fails here, in the test.
    /// </summary>
    internal static AiModelTestFixture CreateFixture(DateOnly today)
    {
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        var weekStart = today.AddDays(daysUntilMonday == 0 ? 7 : daysUntilMonday);
        var collectionDay = weekStart.AddDays(2);   // Wednesday
        var specificDishDay = weekStart.AddDays(3); // Thursday

        // The named candidates carry assessed dietary facts so the evaluation prompt
        // uses the production "[contains: ...]" annotation format; the filler stays
        // unassessed, like a real half-assessed library. No member exclusions here â€”
        // they would contradict the expected answers (Chicken curry on Thursday).
        (string Title, IngredientClass[] Contains)[] vegetarianRecipes =
        [
            ("Pasta pesto", [IngredientClass.Gluten, IngredientClass.Milk]),
            ("Vegetable curry", []),
            ("Mushroom risotto", [IngredientClass.Milk]),
            ("Spinach lasagne", [IngredientClass.Gluten, IngredientClass.Milk, IngredientClass.Eggs]),
            ("Falafel wraps", [IngredientClass.Gluten, IngredientClass.Sesame])
        ];
        string[] vegetarian = [.. vegetarianRecipes.Select(v => v.Title)];
        (string Title, string Category, IngredientClass[] Contains)[] others =
        [
            ("Chicken curry", "Meat", [IngredientClass.Poultry]),
            ("Beef stew", "Meat", [IngredientClass.RedMeat]),
            ("Meatball spaghetti", "Meat", [IngredientClass.RedMeat, IngredientClass.Gluten]),
            ("Pork schnitzel", "Meat", [IngredientClass.Pork, IngredientClass.Gluten, IngredientClass.Eggs]),
            ("Salmon teriyaki", "Fish", [IngredientClass.Fish, IngredientClass.Soybeans, IngredientClass.Gluten])
        ];

        var recipes = vegetarianRecipes
            .Select(v => new RecipeOption
            {
                Id = Guid.NewGuid(), Title = v.Title, Category = "Vegetarian",
                ContainsClasses = v.Contains, FactsAssessed = true
            })
            .Concat(others.Select(o => new RecipeOption
            {
                Id = Guid.NewGuid(), Title = o.Title, Category = o.Category,
                ContainsClasses = o.Contains, FactsAssessed = true
            }))
            // Filler pushes the prompt to the configured token budget (BuildUserPrompt
            // trims to it); the real candidates come first so they always survive the trim
            .Concat(Enumerable.Range(1, 800).Select(i => new RecipeOption
            {
                Id = Guid.NewGuid(),
                Title = $"Weeknight dish #{i:000}",
                Category = "Meat"
            }))
            .ToList();

        string[] collectionTitles = ["Pasta pesto", "Meatball spaghetti"];
        var request = new MealSuggestionRequest
        {
            WeekStart = weekStart,
            Members = [new MemberProfile { Name = "Alex" }],
            KnownRecipes = recipes,
            DaysToFill = [.. Enumerable.Range(0, 5).Select(weekStart.AddDays)],
            Instructions =
                $"On {specificDishDay.DayOfWeek} {specificDishDay:yyyy-MM-dd} serve Chicken curry. " +
                "Two of the days must be vegetarian dishes.",
            CollectionConstraints =
            [
                new CollectionConstraint
                {
                    Name = "Quick Pasta",
                    Id = "fixture-quick-pasta",
                    RecipeIds = recipes.Where(recipe => collectionTitles.Contains(recipe.Title)).Select(recipe => recipe.Id).ToList(),
                    RecipeTitles = collectionTitles,
                    Dates = [collectionDay]
                }
            ]
        };

        return new AiModelTestFixture(
            request, collectionDay, collectionTitles, specificDishDay, "Chicken curry", vegetarian);
    }

    /// <summary>Scores the model's reply against the fixture's known-correct answers.</summary>
    internal static List<AiModelTestCheck> Evaluate(
        WeekSuggestionsPayload payload, AiModelTestFixture fixture)
    {
        var suggestions = (payload.Suggestions ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.DishName)
                && DateOnly.TryParse(s.Date, System.Globalization.CultureInfo.InvariantCulture, out _))
            .Select(s =>
            {
                var recipe = LocalRecipeResolver.Resolve(
                    fixture.Request.KnownRecipes, s.LocalRecipeId, s.RecipeTitle, s.DishName);
                return (
                    Date: DateOnly.Parse(s.Date!, System.Globalization.CultureInfo.InvariantCulture),
                    Titles: new[]
                    {
                        s.DishName!.Trim(),
                        s.RecipeTitle?.Trim() ?? "",
                        recipe?.Title ?? "",
                        recipe?.OriginalTitle ?? ""
                    });
            })
            .ToList();

        bool Mentions(IEnumerable<string> titles, string candidate) =>
            titles.Any(t => string.Equals(t, candidate, StringComparison.OrdinalIgnoreCase));

        var filledDates = suggestions.Select(s => s.Date).ToHashSet();
        var missingDays = fixture.Request.DaysToFill.Where(d => !filledDates.Contains(d)).ToList();

        var collectionHit = suggestions.Any(s => s.Date == fixture.CollectionDay
            && fixture.CollectionTitles.Any(t => Mentions(s.Titles, t)));

        var specificHit = suggestions.Any(s => s.Date == fixture.SpecificDishDay
            && Mentions(s.Titles, fixture.SpecificDish));

        var vegetarianDays = suggestions
            .Where(s => fixture.VegetarianTitles.Any(t => Mentions(s.Titles, t)))
            .Select(s => s.Date)
            .Distinct()
            .Count();

        return
        [
            new AiModelTestCheck("All days filled", missingDays.Count == 0, missingDays.Count == 0
                ? $"All {fixture.Request.DaysToFill.Count} requested days got a proposal"
                : $"{missingDays.Count} of {fixture.Request.DaysToFill.Count} requested day(s) missing: {string.Join(", ", missingDays.Select(d => d.ToString("yyyy-MM-dd")))}"),
            new AiModelTestCheck("Collection day", collectionHit, collectionHit
                ? $"{fixture.CollectionDay.DayOfWeek}'s dish came from the referenced #[Quick Pasta] collection"
                : $"{fixture.CollectionDay.DayOfWeek} ignored the #[Quick Pasta] collection constraint"),
            new AiModelTestCheck("Specific dish", specificHit, specificHit
                ? $"'{fixture.SpecificDish}' was planned on {fixture.SpecificDishDay.DayOfWeek} as instructed"
                : $"The instruction to serve '{fixture.SpecificDish}' on {fixture.SpecificDishDay.DayOfWeek} was not followed"),
            new AiModelTestCheck("Vegetarian days", vegetarianDays >= 2, vegetarianDays >= 2
                ? $"{vegetarianDays} day(s) use vegetarian dishes (2 were asked)"
                : $"Only {vegetarianDays} day(s) vegetarian â€” the instruction asked for 2")
        ];
    }
}
