using Dishhive.Api.Models;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Text.Json;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Which response format the configured model was verified to handle.</summary>
public enum AiResponseMode
{
    /// <summary>No attempt produced parseable JSON — the model is unfit for suggestions.</summary>
    None,
    /// <summary>Native json_schema structured output works: hard format guarantee.</summary>
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

    /// <summary>The /models probe answered (advisory — completions are the real signal)</summary>
    public bool EndpointReachable { get; init; }

    /// <summary>Configured model id present in /models; null when the list could not be read</summary>
    public bool? ModelListed { get; init; }

    public AiResponseMode ResponseMode { get; init; }

    /// <summary>Whether the medium-complexity evaluation passed; null when never reached</summary>
    public bool? EvaluationPassed { get; init; }

    /// <summary>Whether the model completed a synthetic get_recipe tool round-trip.</summary>
    public bool? ToolCallingPassed { get; init; }

    public IReadOnlyList<AiModelTestCheck> Checks { get; init; } = [];

    /// <summary>Rough output speed of the successful completion, for expectation-setting</summary>
    public double? TokensPerSecond { get; init; }

    public long ElapsedMs { get; init; }

    /// <summary>The model can produce parseable suggestions at all; gates the LLM path</summary>
    public bool Viable => ResponseMode != AiResponseMode.None;

    /// <summary>failed | warnings | passed — the settings page headline</summary>
    public string Verdict => !Viable
        ? "failed"
        : EvaluationPassed == true && ModelListed != false && ToolCallingPassed != false ? "passed" : "warnings";
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
/// Probes the configured model's actual fitness for week suggestions (reachable ≠ usable:
/// the /models ping says nothing about JSON discipline or context headroom). Three stages:
/// 1. /models — endpoint up, configured model id present (catches "wrong model loaded").
/// 2. A medium-complexity suggestion request — first with native json_schema enforcement,
///    then falling back to prompted JSON — sized to the full Ai:MaxPromptTokens budget so
///    a too-small context window fails HERE, not on every future planning evening.
/// 3. The reply is scored against known-correct answers: a #[Collection]-constrained day,
///    a specific dish demanded on a specific day, and "two days vegetarian".
/// The verified response mode is reused by LlmMealSuggestionService (schema enforcement
/// when proven, prompted otherwise); a None verdict makes it skip the model entirely.
/// </summary>
public class AiModelTester
{
    private readonly IChatClient _chatClient;
    private readonly AiOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiModelTester> _logger;

    public AiModelTester(
        IChatClient chatClient, AiOptions options,
        IHttpClientFactory httpClientFactory, ILogger<AiModelTester> logger)
    {
        _chatClient = chatClient;
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>JSON schema for the suggestions payload (strict style: all fields required,
    /// nullability via type unions, no extra properties) — shared with the LLM service
    /// when schema mode is verified.</summary>
    /// <summary>
    /// Runs the capability test under the given effective system prompt — the same
    /// composed prompt production uses, including any user override, so the verdict
    /// describes what actually runs. Virtual so the capability service's persistence
    /// tests can stub the actual probing.
    /// </summary>
    public virtual async Task<AiModelTestResult> RunAsync(
        string effectiveSystemPrompt, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var checks = new List<AiModelTestCheck>();

        try
        {
            var (endpointReachable, modelListed) = await ProbeModelsAsync(cancellationToken);
            checks.Add(new AiModelTestCheck("Endpoint", endpointReachable,
                endpointReachable ? "The AI endpoint answered the /models probe" : "The /models probe failed — endpoint may be down"));
            checks.Add(new AiModelTestCheck("Model available", modelListed != false, modelListed switch
            {
                true => $"'{_options.Model}' is served by the endpoint",
                false => $"'{_options.Model}' is NOT in the endpoint's model list — is the right model loaded?",
                null => "Could not read the endpoint's model list to verify (not all providers expose it)"
            }));

            var fixture = CreateFixture(DateOnly.FromDateTime(DateTime.Today));
            var userPrompt = MealSuggestionPromptBuilder.BuildUserPrompt(fixture.Request, _options.MaxPromptTokens);
            var systemPrompt = _options.DisableThinking
                ? "/no_think\n" + effectiveSystemPrompt
                : effectiveSystemPrompt;

            // Native schema enforcement first (hard guarantee when it works), prompted
            // JSON second (the broadly-compatible default). Whichever succeeds becomes
            // the production response mode.
            var mode = AiResponseMode.None;
            var (payload, tokensPerSecond) = await TryCompleteAsync(
                systemPrompt, userPrompt, MealSuggestionResponseContract.JsonSchemaFormat, "json_schema", cancellationToken);
            if (payload?.Suggestions is not null)
            {
                mode = AiResponseMode.JsonSchema;
            }
            else
            {
                (payload, tokensPerSecond) = await TryCompleteAsync(
                    systemPrompt, userPrompt, responseFormat: null, "prompted", cancellationToken);
                if (payload?.Suggestions is not null)
                {
                    mode = AiResponseMode.PromptedJson;
                }
            }

            checks.Add(new AiModelTestCheck("Structured JSON reply", mode != AiResponseMode.None, mode switch
            {
                AiResponseMode.JsonSchema => "Native json_schema output works — format is guaranteed",
                AiResponseMode.PromptedJson => "Prompted JSON works (native json_schema was rejected or came back empty)",
                _ => "No attempt produced parseable JSON — suggestions would always fall back to the rules engine"
            }));

            bool? evaluationPassed = null;
            bool? toolCallingPassed = null;
            if (payload?.Suggestions is not null)
            {
                var evaluation = Evaluate(payload, fixture);
                checks.AddRange(evaluation);
                evaluationPassed = evaluation.All(c => c.Passed);

                toolCallingPassed = await TestToolCallingAsync(
                    effectiveSystemPrompt,
                    mode == AiResponseMode.JsonSchema ? MealSuggestionResponseContract.JsonSchemaFormat : null,
                    cancellationToken);
                checks.Add(new AiModelTestCheck(
                    "External recipe tools",
                    toolCallingPassed.Value,
                    toolCallingPassed.Value
                        ? "The model called get_recipe and copied its candidateId into the final response"
                        : "The model did not complete a tool call round-trip; @[Source] requests will use the rules fallback"));
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
                ToolCallingPassed = toolCallingPassed,
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

    private async Task<bool> TestToolCallingAsync(
        string systemPrompt,
        ChatResponseFormat? responseFormat,
        CancellationToken cancellationToken)
    {
        const string expectedCandidateId = "c-test";
        var invoked = false;
        var tool = AIFunctionFactory.Create(
            (string candidateId) =>
            {
                invoked = candidateId == expectedCandidateId;
                return new
                {
                    candidateId,
                    scrapable = true,
                    title = "Tool test soup",
                    ingredients = new[] { "1 onion" },
                    instructions = new[] { "Cook." }
                };
            },
            name: "get_recipe");
        var client = _chatClient.AsBuilder()
            .UseFunctionInvocation(configure: options => options.MaximumIterationsPerRequest = 3)
            .Build();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            var response = await client.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User,
                        $"Capability check: call get_recipe with candidateId '{expectedCandidateId}', then reply with one suggestion for 2099-01-01 using externalCandidateId '{expectedCandidateId}'.")
                ],
                new ChatOptions
                {
                    MaxOutputTokens = Math.Min(_options.MaxOutputTokens, 1000),
                    Temperature = 0,
                    Tools = [tool],
                    ResponseFormat = responseFormat
                },
                timeout.Token);
            var payload = MealSuggestionResponseContract.Parse(response.Text);
            return invoked && payload?.Suggestions?.Any(s => s.ExternalCandidateId == expectedCandidateId) == true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "AI external-tool capability check failed");
            return false;
        }
    }

    /// <summary>One completion attempt under the plain-path timeout; null payload on any failure.</summary>
    private async Task<(WeekSuggestionsPayload? Payload, double? TokensPerSecond)> TryCompleteAsync(
        string systemPrompt, string userPrompt, ChatResponseFormat? responseFormat,
        string attemptName, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var attemptStopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.System, systemPrompt), new ChatMessage(ChatRole.User, userPrompt)],
                new ChatOptions
                {
                    MaxOutputTokens = _options.MaxOutputTokens,
                    Temperature = (float)_options.Temperature,
                    ResponseFormat = responseFormat
                },
                cancellationToken: timeout.Token);
            attemptStopwatch.Stop();

            double? tokensPerSecond = response.Usage?.OutputTokenCount is { } output && attemptStopwatch.Elapsed.TotalSeconds > 0
                ? output / attemptStopwatch.Elapsed.TotalSeconds
                : null;

            var payload = MealSuggestionResponseContract.Parse(response.Text);
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
    /// Advisory only — some gateways block the listing while completions work fine, so a
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

            // OpenAI shape: {"data":[{"id":"..."}]} — anything else counts as "can't verify"
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
    /// A realistic planning request with verifiable expectations: Mon–Fri of the next week,
    /// a #[Quick Pasta] collection pinned to Wednesday, "serve Chicken curry on Thursday",
    /// and "two days vegetarian" (the recipes carry a Vegetarian/Meat/Fish category, so
    /// this tests instruction-following, not world knowledge). The known-recipes list is
    /// padded with filler titles so the prompt fills the whole MaxPromptTokens budget —
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
        // unassessed, like a real half-assessed library. No member exclusions here —
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
            .Select(s => (
                Date: DateOnly.Parse(s.Date!, System.Globalization.CultureInfo.InvariantCulture),
                Titles: new[] { s.DishName!.Trim(), s.RecipeTitle?.Trim() ?? "" }))
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
                : $"Only {vegetarianDays} day(s) vegetarian — the instruction asked for 2")
        ];
    }
}
