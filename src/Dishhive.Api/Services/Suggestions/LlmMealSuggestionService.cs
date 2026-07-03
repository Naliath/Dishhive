using Dishhive.Api.Models;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.WebSearch;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// LLM-backed week-plan suggestions via a Microsoft.Extensions.AI IChatClient
/// (provider selected by configuration, see ChatClientFactory).
/// Builds a prompt from the household context and demands structured JSON output.
/// Any failure — timeout, HTTP error, malformed response — falls back to the
/// deterministic rules provider: suggestions must never break planning.
/// </summary>
public partial class LlmMealSuggestionService : IMealSuggestionService
{
    private readonly IChatClient _chatClient;
    private readonly RulesMealSuggestionService _fallback;
    private readonly AiOptions _options;
    private readonly IWebSearchClient _webSearch;
    private readonly IRecipeImportService _importService;
    private readonly IAiModelCapabilityService _capability;
    private readonly IAiPromptProvider _promptProvider;
    private readonly int _webSearchMaxResults;
    private readonly ILogger<LlmMealSuggestionService> _logger;

    public LlmMealSuggestionService(
        IChatClient chatClient,
        RulesMealSuggestionService fallback,
        AiOptions options,
        IWebSearchClient webSearch,
        IRecipeImportService importService,
        WebSearchOptions webSearchOptions,
        IAiModelCapabilityService capability,
        IAiPromptProvider promptProvider,
        ILogger<LlmMealSuggestionService> logger)
    {
        _chatClient = chatClient;
        _fallback = fallback;
        _options = options;
        _webSearch = webSearch;
        _importService = importService;
        _capability = capability;
        _promptProvider = promptProvider;
        _webSearchMaxResults = webSearchOptions.MaxResults;
        _logger = logger;
    }

    public bool IsEnabled => true;

    public async Task<IReadOnlyList<MealSuggestion>> SuggestAsync(
        MealSuggestionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.DaysToFill.Count == 0)
        {
            return [];
        }

        // Short id correlating every log line this call produces (completion attempts,
        // tool calls, the final outcome) — the tool loop interleaves with its own HTTP
        // client logging, so a plain "info: ..." stream is otherwise hard to follow.
        var requestId = Guid.NewGuid().ToString("N")[..8];
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // AI work waits for the model capability test (usually already done at
            // startup): a model that never produced parseable JSON in the test is not
            // called at all — every such call is a known-doomed 60s wait — and a model
            // verified for native json_schema gets that hard format guarantee below.
            var capability = await _capability.EnsureTestedAsync(cancellationToken);
            if (!capability.Viable)
            {
                _logger.LogWarning(
                    "[{RequestId}] Model {Provider}/{Model} failed its capability test (verdict={Verdict}); using rules fallback without calling it",
                    requestId, _options.Provider, _options.Model, capability.Verdict);
                return Finalize(await _fallback.SuggestAsync(request, cancellationToken), request);
            }

            // Attach the external-recipe tools ONLY when the planner explicitly referenced
            // an external source via @[Source] (SourceConstraints is populated by
            // SourceMentionResolver) — an unambiguous, discoverable opt-in, mirroring
            // #[Collection] for known recipes. Plain instructions text ("3 days vegetarian")
            // must never trigger a live web search on its own: a tool loop is several full
            // model round-trips (much slower and far more tokens than one completion — see
            // docs/features/ai-week-planning.md), so that cost is only paid when asked for.
            var useTools = _webSearch.IsConfigured && request.SourceConstraints.Count > 0;

            _logger.LogInformation(
                "[{RequestId}] AI suggestion request starting: {DayCount} day(s) to fill, tools={UseTools}, {Provider}/{Model}",
                requestId, request.DaysToFill.Count, useTools, _options.Provider, _options.Model);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(useTools ? _options.AgentTimeoutSeconds : _options.TimeoutSeconds));

            // Editable section (user override or shipped default) + protected machinery.
            // Reasoning models need to think to plan tool calls, so /no_think is skipped
            // on the agentic path even when DisableThinking is set.
            var effectivePrompt = await _promptProvider.GetEffectiveSystemPromptAsync(cancellationToken);
            var systemPrompt = _options.DisableThinking && !useTools ? "/no_think\n" + effectivePrompt : effectivePrompt;
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, BuildUserPrompt(request, _options.MaxPromptTokens))
            };

            IChatClient chatClient = _chatClient;
            IList<AITool>? tools = null;
            if (useTools)
            {
                var hosts = request.SourceConstraints.Select(c => c.Host).Distinct().ToList();
                var toolset = new ExternalRecipeTools(
                    _webSearch, _importService, _webSearchMaxResults,
                    defaultSite: hosts.Count == 1 ? hosts[0] : null, requestId, _logger);
                tools = toolset.Build();
                chatClient = _chatClient.AsBuilder()
                    .UseFunctionInvocation(configure: c => c.MaximumIterationsPerRequest = _options.MaxToolIterations)
                    .Build();
            }

            // Response format follows what the capability test PROVED works for this
            // model: native json_schema when verified (hard format guarantee — the
            // parse/retry machinery below becomes a safety net), otherwise prompted
            // JSON described in the system prompt. A blanket choice is wrong in both
            // directions: LM Studio rejects json_object outright and reasoning models
            // (e.g. Qwen3) emit their answer into the reasoning channel under a schema,
            // while capable cloud models give guaranteed-valid JSON for free.
            // ParsePayload tolerates fences/think-tags either way.
            var chatOptions = new ChatOptions
            {
                MaxOutputTokens = _options.MaxOutputTokens,
                Temperature = (float)_options.Temperature,
                Tools = tools,
                ResponseFormat = capability.ResponseMode == AiResponseMode.JsonSchema
                    ? AiModelTester.JsonSchemaFormat
                    : null
            };

            // One model can stumble on the JSON once; a corrective reprompt (the bad
            // reply quoted back) recovers far more often than dropping straight to the
            // rules fallback. All attempts share the single TimeoutSeconds budget.
            WeekSuggestionsPayload? payload = null;
            for (var attempt = 0; attempt <= Math.Max(0, _options.MaxRetries); attempt++)
            {
                var attemptStopwatch = Stopwatch.StartNew();
                var response = await chatClient.GetResponseAsync(
                    messages, chatOptions, cancellationToken: timeout.Token);
                attemptStopwatch.Stop();
                LogUsage(requestId, response, attempt, attemptStopwatch.Elapsed);

                payload = ParsePayload(response.Text);
                if (payload?.Suggestions is not null)
                {
                    break;
                }

                var text = response.Text ?? "";
                var truncated = response.FinishReason == ChatFinishReason.Length;
                _logger.LogWarning(
                    "[{RequestId}] AI reply unparseable (attempt {Attempt}/{Max}, truncated={Truncated}). Length={Length}, start: {Snippet}",
                    requestId, attempt + 1, _options.MaxRetries + 1, truncated, text.Length,
                    text.Length > 300 ? text[..300] : text);

                if (attempt < _options.MaxRetries)
                {
                    messages.Add(new ChatMessage(ChatRole.Assistant, BuildRepromptQuote(text)));
                    messages.Add(new ChatMessage(ChatRole.User, truncated
                        ? "Your reply was cut off before the JSON was complete. Reply again with ONLY the JSON object and keep every reason to a few words."
                        : "That was not valid JSON. Reply with ONLY the JSON object in the required shape, no other text."));
                }
            }

            if (payload?.Suggestions is null)
            {
                _logger.LogWarning(
                    "[{RequestId}] AI suggestions unparseable after {Attempts} attempt(s) in {ElapsedMs}ms; using rules fallback",
                    requestId, _options.MaxRetries + 1, stopwatch.ElapsedMilliseconds);
                return Finalize(await _fallback.SuggestAsync(request, cancellationToken), request);
            }

            var suggestions = PostProcess(payload, request);
            // Weak models sometimes return fewer days than asked; fill the holes from
            // the deterministic rules rather than leaving the planner with empty days.
            suggestions = await BackfillMissingDaysAsync(requestId, suggestions, request, cancellationToken);
            _logger.LogInformation(
                "[{RequestId}] AI proposed {Count} meal suggestions via {Provider}/{Model} in {ElapsedMs}ms",
                requestId, suggestions.Count, _options.Provider, _options.Model, stopwatch.ElapsedMilliseconds);
            return Finalize(suggestions, request);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "[{RequestId}] AI suggestion request cancelled by caller after {ElapsedMs}ms",
                requestId, stopwatch.ElapsedMilliseconds);
            throw; // caller cancelled (request aborted); don't mask it
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[{RequestId}] AI suggestion call failed ({Provider}/{Model}) after {ElapsedMs}ms; using rules fallback",
                requestId, _options.Provider, _options.Model, stopwatch.ElapsedMilliseconds);
            return Finalize(await _fallback.SuggestAsync(request, cancellationToken), request);
        }
    }

    /// <summary>
    /// Runs the constraint-conflict flagging over the final answer, whatever path
    /// produced it. This must be the single last step of every SuggestAsync exit:
    /// an earlier version flagged inside PostProcess only, so full rules-fallback
    /// answers (capability gate, unparseable reply, exception) and rules-backfilled
    /// days were returned with checkable allergy conflicts unflagged.
    /// </summary>
    private IReadOnlyList<MealSuggestion> Finalize(
        IReadOnlyList<MealSuggestion> suggestions, MealSuggestionRequest request)
    {
        var result = suggestions.ToList();
        FlagConstraintConflicts(result, request);
        return result;
    }

    private void LogUsage(string requestId, ChatResponse response, int attempt, TimeSpan elapsed)
    {
        // Always logged (even when the provider doesn't report token usage) so a slow
        // attempt is visible regardless of provider — local models routinely omit usage.
        if (response.Usage is { } usage)
        {
            _logger.LogInformation(
                "[{RequestId}] AI completion (attempt {Attempt}) in {ElapsedMs}ms: input={Input}, output={Output}, total={Total} tokens; finish={Finish}",
                requestId, attempt + 1, elapsed.TotalMilliseconds, usage.InputTokenCount, usage.OutputTokenCount,
                usage.TotalTokenCount, response.FinishReason);
        }
        else
        {
            _logger.LogInformation(
                "[{RequestId}] AI completion (attempt {Attempt}) in {ElapsedMs}ms; finish={Finish} (no usage reported)",
                requestId, attempt + 1, elapsed.TotalMilliseconds, response.FinishReason);
        }
    }

    /// <summary>
    /// Fills any DaysToFill the model left empty from the deterministic rules
    /// provider (marked as fallback-sourced), so the planner never gets a partial week.
    /// </summary>
    private async Task<List<MealSuggestion>> BackfillMissingDaysAsync(
        string requestId, List<MealSuggestion> suggestions, MealSuggestionRequest request, CancellationToken cancellationToken)
    {
        var filled = suggestions.Select(s => s.Date).ToHashSet();
        var missing = request.DaysToFill.Where(d => !filled.Contains(d)).ToList();
        if (missing.Count == 0)
        {
            return suggestions;
        }

        _logger.LogInformation("[{RequestId}] AI left {Count} day(s) unfilled; backfilling from rules",
            requestId, missing.Count);

        // Don't let the rules backfill reuse freezer stock the AI suggestions already reserved
        var usedByItem = suggestions
            .Where(s => s.FreezyItemRef != null)
            .GroupBy(s => s.FreezyItemRef!)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.FreezyItemQuantity));
        var remainingFrozen = request.AvailableFrozenItems
            .Select(i => usedByItem.TryGetValue(i.Id, out var used) ? i with { Quantity = i.Quantity - used } : i)
            .Where(i => i.Quantity > 0)
            .ToList();

        // Also don't let the rules backfill re-suggest a dish name the AI already picked
        // for another day this call — RulesMealSuggestionService excludes anything already
        // in WeekPlan, so folding the AI's own picks in here (as synthetic entries) is
        // enough to cover it without any extra dedup logic on this end.
        var aiPicksAsExisting = suggestions
            .Where(s => s.DishName != null)
            .Select(s => new ExistingMeal { Date = s.Date, DishName = s.DishName });
        var fallbackRequest = request with
        {
            DaysToFill = missing,
            AvailableFrozenItems = remainingFrozen,
            WeekPlan = request.WeekPlan.Concat(aiPicksAsExisting).ToList()
        };
        var filler = await _fallback.SuggestAsync(fallbackRequest, cancellationToken);
        return suggestions.Concat(filler).OrderBy(s => s.Date).ToList();
    }

    /// <summary>
    /// The user-tweakable part of the system prompt: persona and soft preferences only.
    /// A stored override (see AiPromptService) replaces this text verbatim; everything
    /// mechanical lives in <see cref="ProtectedSystemPrompt"/> and is always appended,
    /// because post-processing depends on it (exact recipeTitle/freezer-name matching,
    /// sourceUrl contract, the JSON shape).
    /// </summary>
    internal const string EditableSystemPromptDefault =
        """
        You are a meal planner for a family household. Propose a dinner for each
        requested date.
        - Prefer variety: avoid dishes eaten in the last two weeks.
        - Favor household favorites and dishes with high ratings; avoid low-rated dishes.
        - Keep each reason to one short sentence.
        """;

    /// <summary>
    /// The non-negotiable part of the system prompt. Note this is textual protection
    /// only — a hostile editable section can still talk the model out of these rules;
    /// what actually guards the app is post-processing, the rules fallback and the
    /// model capability test (which re-runs whenever the effective prompt changes).
    /// </summary>
    internal const string ProtectedSystemPrompt =
        """
        Rules that ALWAYS apply, regardless of the guidance above:
        - NEVER suggest dishes that conflict with the listed allergies or dietary constraints.
        - Household tags may carry exact ingredient classes in brackets, e.g.
          "Vegetarisch [excludes: RedMeat, Poultry, Pork, Fish, ...]", and known
          recipes may carry "[contains: ...]" with the same class names ("[contains:
          none]" = verified to contain none of the tracked classes). A dish whose
          contained classes overlap ANY attendee's excluded classes is forbidden.
          Recipes conflicting with an allergy are already omitted from the known-
          recipes list; apply the same exclusions yourself to any free-text dish you
          propose.
        - Use expiring freezer items ONLY when the item is a complete meal by itself —
          a frozen pizza, lasagna, soup, stew, or a container of home-made leftovers are
          fine. A raw ingredient or side component (e.g. a bag of peas, frozen corn,
          butter, shredded cheese, flour) is NOT a dish; never invent a "dinner" around
          one just because it is expiring — leave it for the rest of the week's cooking
          instead. The goal is enough food for everyone in "Household" on that date, not
          maximizing freezer use. Check each item's notes for its portion size; if none is
          given, ASSUME it is a household-sized portion (the normal case for home-made
          leftovers in a container) and propose it alone. Only add a second freezer item
          for the SAME date when the notes explicitly say the first one's portion is
          smaller than the household — as an ADDITIONAL, SEPARATE suggestion entry, never
          merged into one (e.g. a frozen pizza noted "for 2" and a frozen lasagna noted
          "for 2" together cover a household of 4: two separate entries, dated the same).
          Each freezer item in the list below has an id. When a dish uses one, copy that
          id EXACTLY into "freezerItemId" — this is how the app links it back to that
          stock; dishName does not need to match the item's name, a short label is fine
          (the app fills in the item's real name for tracking). Leave "freezerItemId"
          null for every dish that is not a freezer item. Never use an item more times
          across the week than the quantity listed for it (the stock is already reserved
          for what you plan).
        - When a day has a vague instruction (e.g. "something with fish" or "vegetarian"),
          every dish you suggest for that day must satisfy it.
        - Instructions may reference a recipe collection as #[Collection Name]. When a
          day's instruction references a collection, the dish for that day MUST be one of
          the recipes listed under "Referenced collections" for it (copy the exact title
          into recipeTitle). When the planner's general instructions reference one, prefer
          its recipes for the matching wish. If a referenced collection has no recipe list
          below, treat the reference as a plain-text hint.
        - Instructions may reference an EXTERNAL website as @[Source] (listed under
          "Referenced sources" with its host). ONLY for the day(s)/wish tied to such a
          reference, use the tools to find and verify a real page there:
            * search_recipes(query, site) — pass the referenced source's host as site,
            * get_recipe(url) — read a candidate and CHECK it meets every constraint
              (time limit, vegetarian, etc.) before choosing it.
          When you propose such an external recipe, put its page URL in "sourceUrl", use the
          recipe's real title as dishName, and leave recipeTitle null (it is not in the store
          yet — it will be imported when accepted). Only propose a sourceUrl you actually
          fetched with get_recipe and confirmed fits. Do NOT use these tools for any other
          day or wish — every day without a @[Source] reference must be filled from the
          known-recipes list below or a plain dish name, never a web search.
        - When the planner gives additional instructions, they override the other
          preferences (never the allergies/constraints).
        - Prefer recipes from the known-recipes list; when you use one, copy its exact title
          into recipeTitle.

        Reply with ONLY a JSON object in exactly this shape, no other text:
        {"suggestions":[{"date":"yyyy-MM-dd","dishName":"...","recipeTitle":"exact title or null","freezerItemId":"exact id from the freezer items list or null","sourceUrl":"external recipe url or null","reason":"..."}]}
        """;

    /// <summary>
    /// The effective system prompt: the stored editable override (or the shipped
    /// default) with the protected machinery always appended.
    /// </summary>
    internal static string ComposeSystemPrompt(string? editableOverride)
    {
        var editable = string.IsNullOrWhiteSpace(editableOverride)
            ? EditableSystemPromptDefault
            : editableOverride.Trim();
        return editable + "\n\n" + ProtectedSystemPrompt;
    }

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Strips &lt;think&gt;/&lt;reasoning&gt; channels (closed or truncated) some
    /// models emit, so the JSON slice below can't pick up a brace from inside them.</summary>
    [GeneratedRegex(@"<(?:think|reasoning)>.*?(?:</(?:think|reasoning)>|$)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ReasoningBlockRegex();

    /// <summary>Upper bound on the bad reply quoted back into a corrective reprompt.</summary>
    private const int MaxRepromptQuoteChars = 2000;

    /// <summary>
    /// Builds the assistant-turn content for a corrective reprompt after an unparseable
    /// reply. The dominant local-model failure is burning the whole output budget on
    /// reasoning before any JSON appears — quoting that back verbatim would roughly
    /// double an already-overflowing context, making the retry fail for the same reason
    /// as the original attempt. Strips the reasoning channel and keeps only a tail
    /// window (where a real, if malformed, answer attempt usually sits) — plenty for
    /// the model to see what needs fixing without re-spending the budget that broke it.
    /// </summary>
    private static string BuildRepromptQuote(string text)
    {
        var stripped = ReasoningBlockRegex().Replace(text, "").Trim();
        if (stripped.Length == 0)
        {
            return "(your reply contained only reasoning, no visible answer)";
        }

        return stripped.Length > MaxRepromptQuoteChars
            ? stripped[^MaxRepromptQuoteChars..]
            : stripped;
    }

    /// <summary>
    /// Extracts the payload from the model's text. Tolerates reasoning channels,
    /// markdown fences and trailing prose by stripping &lt;think&gt; blocks and slicing
    /// the outermost JSON; accepts both the wrapping object and a bare suggestions array.
    /// </summary>
    internal static WeekSuggestionsPayload? ParsePayload(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = ReasoningBlockRegex().Replace(text, "");

        // Prefer the documented object form when a parseable suggestions object is present
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                var obj = JsonSerializer.Deserialize<WeekSuggestionsPayload>(
                    text[start..(end + 1)], PayloadJsonOptions);
                if (obj?.Suggestions is not null)
                {
                    return obj;
                }
            }
            catch (JsonException)
            {
                // fall through to the bare-array form
            }
        }

        // Some models answer with a bare array ([{...}]) instead of the wrapping object
        var arrStart = text.IndexOf('[');
        var arrEnd = text.LastIndexOf(']');
        if (arrStart >= 0 && arrEnd > arrStart)
        {
            try
            {
                var items = JsonSerializer.Deserialize<List<DaySuggestionPayload>>(
                    text[arrStart..(arrEnd + 1)], PayloadJsonOptions);
                if (items is not null)
                {
                    return new WeekSuggestionsPayload(items);
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Renders a member tag for the prompt: the display name plus its excluded
    /// classes in brackets when the tag is machine-checkable, so the model matches
    /// against exact classes instead of guessing what a (possibly Dutch) tag name
    /// means — e.g. "Noten [excludes: TreeNuts]", "Vegetarisch [excludes: RedMeat,
    /// Poultry, Pork, Fish, Crustaceans, Molluscs, Gelatin]". The same class names
    /// appear in the known-recipes "[contains: ...]" annotations.
    /// </summary>
    private static string FormatTag(DietaryTagProfile tag) => tag.ExcludedClasses.Count == 0
        ? tag.Name
        : $"{tag.Name} [excludes: {string.Join(", ", tag.ExcludedClasses)}]";

    // internal for prompt-content tests (like ParsePayload)
    internal static string BuildUserPrompt(MealSuggestionRequest request, int maxPromptTokens = int.MaxValue)
    {
        var sb = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

        // --- Fixed-size context first, so the variable blocks below can be sized
        // against whatever budget remains ---
        sb.AppendLine($"Week starting: {request.WeekStart:yyyy-MM-dd}");

        sb.AppendLine("Household:");
        foreach (var member in request.Members)
        {
            sb.Append($"- {member.Name}");
            if (member.Allergies.Count > 0)
            {
                sb.Append($"; allergies: {string.Join(", ", member.Allergies.Select(FormatTag))}");
            }
            if (member.Diets.Count > 0)
            {
                sb.Append($"; constraints: {string.Join(", ", member.Diets.Select(FormatTag))}");
            }
            if (!string.IsNullOrWhiteSpace(member.PreferenceNotes))
            {
                sb.Append($"; preferences: {member.PreferenceNotes}");
            }
            sb.AppendLine();
        }

        if (request.Favorites.Count > 0)
        {
            sb.AppendLine("Favorites:");
            foreach (var group in request.Favorites.GroupBy(f => f.MemberName))
            {
                sb.AppendLine($"- {group.Key}: {string.Join(", ", group.Select(f => f.DishName))}");
            }
        }

        if (request.AvailableFrozenItems.Count > 0)
        {
            sb.AppendLine("Freezer items (soonest expiring first; copy the id exactly into freezerItemId):");
            foreach (var item in request.AvailableFrozenItems.Take(10))
            {
                sb.Append($"- id={item.Id}: {item.Name} ({item.Quantity} {item.Unit ?? "x"})");
                if (item.ExpirationDate.HasValue)
                {
                    sb.Append($", expires {item.ExpirationDate:yyyy-MM-dd}");
                }
                if (!string.IsNullOrWhiteSpace(item.Notes))
                {
                    sb.Append($", notes: {item.Notes}");
                }
                sb.AppendLine();
            }
        }

        if (request.CollectionConstraints.Count > 0)
        {
            sb.AppendLine("Referenced collections:");
            foreach (var constraint in request.CollectionConstraints)
            {
                var scope = constraint.Dates.Count > 0
                    ? $"for {string.Join(", ", constraint.Dates.Select(d => d.ToString("yyyy-MM-dd")))}"
                    : "general instructions";
                var titles = constraint.RecipeTitles.Count > 0
                    ? string.Join(", ", constraint.RecipeTitles.Select(t => $"\"{t}\""))
                    : "(no recipes in this collection)";
                sb.AppendLine($"- \"{constraint.Name}\" ({scope}): {titles}");
            }
        }

        if (request.SourceConstraints.Count > 0)
        {
            sb.AppendLine("Referenced sources (external websites — use search_recipes with the host, then get_recipe):");
            foreach (var constraint in request.SourceConstraints)
            {
                var scope = constraint.Dates.Count > 0
                    ? $"for {string.Join(", ", constraint.Dates.Select(d => d.ToString("yyyy-MM-dd")))}"
                    : "general instructions";
                sb.AppendLine($"- \"{constraint.Name}\" → {constraint.Host} ({scope})");
            }
        }

        var existing = request.WeekPlan
            .Where(m => m.DishName != null || m.VagueInstruction != null)
            .OrderBy(m => m.Date)
            .ToList();
        if (existing.Count > 0)
        {
            sb.AppendLine("Existing plan this week:");
            foreach (var meal in existing)
            {
                sb.AppendLine(meal.DishName != null
                    ? $"- {meal.Date:yyyy-MM-dd}: \"{meal.DishName}\""
                    : $"- {meal.Date:yyyy-MM-dd}: vague: \"{meal.VagueInstruction}\"");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Instructions))
        {
            sb.AppendLine($"Additional instructions from the planner: {request.Instructions}");
        }

        // --- Variable-size blocks: history then the ranked recipe list, each
        // trimmed to the token budget (~4 chars/token). History is split into two
        // compact lists — what to avoid for variety, and what's liked/disliked —
        // dropping the verbose per-dish counts the model doesn't use. Recipes are
        // already relevance-ranked by the request builder, so the budget keeps the
        // most useful titles. A minimum number of lines is always kept so recipe
        // linking and variety still work on a tight budget. ---
        var unlimited = maxPromptTokens >= int.MaxValue / 4;
        var budgetChars = unlimited ? int.MaxValue : maxPromptTokens * 4;

        var recentLines = request.RecentDishes
            .Select(d => $"{d.DishName} ({d.LastPlanned:yyyy-MM-dd})")
            .ToList();
        var ratedLines = request.RecentDishes
            .Where(d => d.AverageRating.HasValue)
            .OrderByDescending(d => d.AverageRating)
            .Select(d => $"{d.DishName}: {d.AverageRating!.Value.ToString("0.0", culture)}/5")
            .ToList();
        // Recipes conflicting with an attendee's allergy exclusions are omitted —
        // the model can't pick what it never sees (free-text dishes remain guarded
        // by the system-prompt rule and the post-hoc net). Assessed recipes carry
        // their contains-classes so the model matches against facts instead of
        // guessing from (often Dutch) titles.
        var recipeLines = request.KnownRecipes
            .Where(r => !request.AllergyExcludedRecipeIds.Contains(r.Id))
            .Select(r =>
            {
                var line = r.Category != null ? $"\"{r.Title}\" ({r.Category})" : $"\"{r.Title}\"";
                if (r.FactsAssessed)
                {
                    line += r.ContainsClasses.Count > 0
                        ? $" [contains: {string.Join(", ", r.ContainsClasses)}]"
                        : " [contains: none]";
                }
                return line;
            })
            .ToList();

        var historyBudget = unlimited ? int.MaxValue : (int)((budgetChars - sb.Length) * 0.4);
        var consumed = AppendBudgeted(sb, "Recent dinners (avoid repeating soon):", recentLines, historyBudget, minLines: 5);
        AppendBudgeted(sb, "Ratings (favor high, avoid low):", ratedLines,
            unlimited ? int.MaxValue : Math.Max(0, historyBudget - consumed), minLines: 5);

        var recipeBudget = unlimited ? int.MaxValue : Math.Max(0, budgetChars - sb.Length);
        AppendBudgeted(sb, "Known recipes (prefer these; copy the exact title):", recipeLines, recipeBudget, minLines: 10);

        sb.AppendLine($"Propose dinners for: {string.Join(", ", request.DaysToFill.Select(d => d.ToString("yyyy-MM-dd")))}");
        return sb.ToString();
    }

    /// <summary>
    /// Appends a header and as many "- {line}" entries as fit in the char budget,
    /// but never fewer than <paramref name="minLines"/> (so essential context
    /// survives a tight budget). Returns the characters appended.
    /// </summary>
    private static int AppendBudgeted(
        StringBuilder sb, string header, IReadOnlyList<string> lines, int budgetChars, int minLines)
    {
        if (lines.Count == 0)
        {
            return 0;
        }

        var start = sb.Length;
        sb.AppendLine(header);
        var taken = 0;
        foreach (var line in lines)
        {
            if (taken >= minLines && sb.Length - start >= budgetChars)
            {
                break;
            }
            sb.Append("- ").AppendLine(line);
            taken++;
        }
        return sb.Length - start;
    }

    private List<MealSuggestion> PostProcess(WeekSuggestionsPayload payload, MealSuggestionRequest request)
    {
        var validDates = request.DaysToFill.ToHashSet();
        var recipesByTitle = request.KnownRecipes
            .GroupBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var frozenById = request.AvailableFrozenItems
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var suggestions = new List<MealSuggestion>();
        foreach (var item in payload.Suggestions!)
        {
            if (string.IsNullOrWhiteSpace(item.DishName)
                || !DateOnly.TryParse(item.Date, CultureInfo.InvariantCulture, out var date)
                || !validDates.Contains(date))
            {
                continue;
            }

            // An id-confirmed freezer item is authoritative for the dish's display name —
            // the model only has to copy a short id, not reproduce the (possibly long)
            // item name verbatim, and the app always shows/links the real item either way.
            // An id that doesn't match anything available (hallucinated, or the item was
            // reserved elsewhere between prompt build and reply) is logged and ignored;
            // the dish falls through to LinkFreezerItems' name-based fallback below.
            FrozenItem? freezerItem = null;
            if (!string.IsNullOrWhiteSpace(item.FreezerItemId))
            {
                if (frozenById.TryGetValue(item.FreezerItemId.Trim(), out var matched))
                {
                    freezerItem = matched;
                }
                else
                {
                    _logger.LogWarning(
                        "AI proposed freezerItemId \"{Id}\" for {Date}, which is not an available freezer item",
                        item.FreezerItemId, date);
                }
            }

            var dishName = freezerItem?.Name ?? item.DishName.Trim();

            Guid? recipeId = null;
            if (!string.IsNullOrWhiteSpace(item.RecipeTitle)
                && recipesByTitle.TryGetValue(item.RecipeTitle.Trim(), out var byTitle))
            {
                recipeId = byTitle;
            }
            else if (recipesByTitle.TryGetValue(dishName, out var byName))
            {
                recipeId = byName;
            }

            // An external suggestion (found via the tools) carries a page URL to import on
            // accept — only honored when it's a valid http(s) URL and not already a known recipe.
            string? sourceUrl = null;
            string? sourceName = null;
            if (recipeId is null && ResolveExternalSource(item.SourceUrl, date, request) is { } resolved)
            {
                sourceUrl = resolved.Url;
                sourceName = resolved.Name;
            }

            suggestions.Add(new MealSuggestion
            {
                Date = date,
                RecipeId = recipeId,
                DishName = dishName,
                Reason = string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason.Trim(),
                SourceUrl = sourceUrl,
                SourceName = sourceName,
                FreezyItemRef = freezerItem?.Id
            });
        }

        // Collection constraints are enforced softly: an off-list pick is kept (the
        // review dialog lets the user discard it) but logged for diagnosis —
        // rejecting it would leave the day empty, which is worse
        foreach (var constraint in request.CollectionConstraints.Where(c => c.Dates.Count > 0))
        {
            var titles = constraint.RecipeTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var offList in suggestions.Where(s =>
                constraint.Dates.Contains(s.Date) && !titles.Contains(s.DishName!)))
            {
                _logger.LogWarning(
                    "AI suggested \"{Dish}\" for {Date}, which is not in the referenced collection {Collection}",
                    offList.DishName, offList.Date, constraint.Name);
            }
        }

        // A day may hold several dishes (e.g. two small leftovers making one dinner),
        // but never duplicates and at most three proposals per date
        var result = suggestions
            .GroupBy(s => (s.Date, Dish: s.DishName!.ToLowerInvariant()))
            .Select(g => g.First())
            .GroupBy(s => s.Date)
            .SelectMany(g => g.Take(3))
            .OrderBy(s => s.Date)
            .ToList();

        LinkFreezerItems(result, request);
        return result;
    }

    /// <summary>
    /// Validates a model-supplied external recipe URL and resolves its display name from
    /// the referenced sources (falling back to the host). Returns null for a missing or
    /// non-http(s) URL. Soft-enforces day-scoped @[Source] references: an off-source host
    /// is logged but kept (the review dialog lets the user discard it).
    /// </summary>
    private (string Url, string Name)? ResolveExternalSource(
        string? sourceUrl, DateOnly date, MealSuggestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl)
            || !Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www."))
        {
            host = host[4..];
        }

        var match = request.SourceConstraints.FirstOrDefault(c =>
            string.Equals(c.Host, host, StringComparison.OrdinalIgnoreCase));

        var dayConstraint = request.SourceConstraints.FirstOrDefault(c => c.Dates.Contains(date));
        if (dayConstraint != null && !string.Equals(dayConstraint.Host, host, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "AI proposed {Url} for {Date}, which is not on the referenced source {Source} ({Host})",
                uri, date, dayConstraint.Name, dayConstraint.Host);
        }

        return (uri.AbsoluteUri, match?.Name ?? host);
    }

    /// <summary>
    /// Finalizes freezer-item reservations and enforces the per-item quantity cap across
    /// the week. Two ways a suggestion gets here already carrying a FreezyItemRef, or not:
    /// - Confirmed by a validated freezerItemId in PostProcess (the primary path — exact
    ///   by design, immune to the model paraphrasing the item's name).
    /// - Not confirmed (older/weaker models that ignore freezerItemId): falls back to
    ///   matching the dish name against a freezer item's name exactly, same as before
    ///   freezerItemId existed.
    /// Either way, quantity is capped by what's actually available — the model is told
    /// the amount, this enforces it. An id-confirmed suggestion that loses out on a
    /// cap (the model over-used one item) is unlinked rather than dropped — it stays a
    /// valid, if unlinked, dish suggestion.
    /// </summary>
    private static void LinkFreezerItems(List<MealSuggestion> suggestions, MealSuggestionRequest request)
    {
        if (request.AvailableFrozenItems.Count == 0)
        {
            return;
        }

        var byId = request.AvailableFrozenItems
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var byName = request.AvailableFrozenItems
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var used = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < suggestions.Count; i++)
        {
            var s = suggestions[i];

            if (s.FreezyItemRef != null)
            {
                if (!byId.TryGetValue(s.FreezyItemRef, out var confirmed))
                {
                    continue; // defensive; PostProcess only ever sets an id it validated
                }
                var takenById = used.GetValueOrDefault(confirmed.Id);
                if (takenById >= confirmed.Quantity)
                {
                    suggestions[i] = s with { FreezyItemRef = null, FreezyItemQuantity = 0 };
                    continue;
                }
                used[confirmed.Id] = takenById + 1;
                suggestions[i] = s with { FreezyItemQuantity = 1 };
                continue;
            }

            if (s.DishName is null || !byName.TryGetValue(s.DishName, out var item))
            {
                continue;
            }

            var taken = used.GetValueOrDefault(item.Id);
            if (taken >= item.Quantity)
            {
                continue; // already reserved everything available for this item
            }
            used[item.Id] = taken + 1;
            suggestions[i] = s with { FreezyItemRef = item.Id, FreezyItemQuantity = 1 };
        }
    }

    /// <summary>
    /// Post-hoc constraint net: flags (never drops) a suggestion whose linked
    /// recipe conflicts with an attendee's dietary tags. Only verifiable when a
    /// recipe is linked. Two tiers:
    /// - Assessed recipes: exact intersection of the recipe's contained classes
    ///   with each member's excluded classes — allergy tags set AllergyWarning,
    ///   diet tags set DietWarning (diets have no heuristic tier; guessing a
    ///   lifestyle violation from substrings would be pure noise).
    /// - Unassessed recipes: the legacy ingredient-name substring heuristic for
    ///   allergy tag names (language-fragile — "Ei" matches "prei" — but better
    ///   than nothing until the recipe gets assessed).
    /// </summary>
    private void FlagConstraintConflicts(List<MealSuggestion> suggestions, MealSuggestionRequest request)
    {
        // First member+tag per excluded class, for the warning texts
        var allergyByClass = new Dictionary<IngredientClass, string>();
        var dietByClass = new Dictionary<IngredientClass, string>();
        foreach (var member in request.Members)
        {
            foreach (var (tags, byClass) in new[] { (member.Allergies, allergyByClass), (member.Diets, dietByClass) })
            {
                foreach (var tag in tags)
                {
                    foreach (var cls in tag.ExcludedClasses)
                    {
                        byClass.TryAdd(cls, $"{member.Name}'s \"{tag.Name}\"");
                    }
                }
            }
        }

        var allergyTerms = request.Members
            .SelectMany(m => m.Allergies.Select(a => a.Name))
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (allergyByClass.Count == 0 && dietByClass.Count == 0 && allergyTerms.Count == 0)
        {
            return;
        }

        var recipesById = request.KnownRecipes
            .GroupBy(r => r.Id)
            .ToDictionary(g => g.Key, g => g.First());

        for (var i = 0; i < suggestions.Count; i++)
        {
            var s = suggestions[i];
            if (s.RecipeId is null)
            {
                continue;
            }

            if (recipesById.TryGetValue(s.RecipeId.Value, out var recipe) && recipe.FactsAssessed)
            {
                var allergyHits = recipe.ContainsClasses.Where(allergyByClass.ContainsKey).ToList();
                if (allergyHits.Count > 0)
                {
                    suggestions[i] = s = s with
                    {
                        AllergyWarning =
                            $"Contains {string.Join(", ", allergyHits)} — conflicts with {allergyByClass[allergyHits[0]]} allergy"
                    };
                    _logger.LogWarning(
                        "Suggested \"{Dish}\" for {Date}; linked recipe contains [{Classes}] conflicting with a household allergy",
                        s.DishName, s.Date, string.Join(", ", allergyHits));
                }

                var dietHits = recipe.ContainsClasses.Where(dietByClass.ContainsKey).ToList();
                if (dietHits.Count > 0)
                {
                    suggestions[i] = s with
                    {
                        DietWarning =
                            $"Contains {string.Join(", ", dietHits)} — conflicts with {dietByClass[dietHits[0]]} diet"
                    };
                    _logger.LogInformation(
                        "Suggested \"{Dish}\" for {Date}; linked recipe contains [{Classes}] conflicting with a diet tag",
                        s.DishName, s.Date, string.Join(", ", dietHits));
                }
                continue;
            }

            // Unassessed (or unknown) recipe: legacy substring heuristic, allergies only
            if (!request.RecipeAllergens.TryGetValue(s.RecipeId.Value, out var allergens))
            {
                continue;
            }
            var hit = allergyTerms.FirstOrDefault(term =>
                allergens.Ingredients.Any(ing => ing.Contains(term, StringComparison.OrdinalIgnoreCase)));
            if (hit != null)
            {
                suggestions[i] = s with { AllergyWarning = $"May contain {hit} (household allergy)" };
                _logger.LogWarning(
                    "Suggested \"{Dish}\" for {Date}; linked recipe has an ingredient matching the {Allergy} allergy",
                    s.DishName, s.Date, hit);
            }
        }
    }

    /// <summary>Expected JSON shape of the LLM response</summary>
    internal sealed record WeekSuggestionsPayload(List<DaySuggestionPayload>? Suggestions);

    /// <param name="FreezerItemId">Exact id (copied from the "Freezer items" prompt block)
    /// of the freezer item this dish uses, or null. Authoritative over DishName for
    /// freezer linking — see PostProcess, which resolves the item and overrides DishName
    /// with its real name, so the model doesn't need to reproduce (potentially long) item
    /// names verbatim to get a working match.</param>
    internal sealed record DaySuggestionPayload(
        string? Date, string? DishName, string? RecipeTitle, string? Reason, string? SourceUrl,
        string? FreezerItemId = null);
}
