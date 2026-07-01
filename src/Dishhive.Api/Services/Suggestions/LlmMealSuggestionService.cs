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
    private readonly int _webSearchMaxResults;
    private readonly ILogger<LlmMealSuggestionService> _logger;

    public LlmMealSuggestionService(
        IChatClient chatClient,
        RulesMealSuggestionService fallback,
        AiOptions options,
        IWebSearchClient webSearch,
        IRecipeImportService importService,
        WebSearchOptions webSearchOptions,
        ILogger<LlmMealSuggestionService> logger)
    {
        _chatClient = chatClient;
        _fallback = fallback;
        _options = options;
        _webSearch = webSearch;
        _importService = importService;
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

            // Reasoning models need to think to plan tool calls, so /no_think is skipped
            // on the agentic path even when DisableThinking is set.
            var systemPrompt = _options.DisableThinking && !useTools ? "/no_think\n" + SystemPrompt : SystemPrompt;
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

            // Plain text completion with the JSON shape described in the prompt, parsed
            // manually. Native response_format is deliberately avoided: LM Studio rejects
            // json_object outright, and with json_schema reasoning models (e.g. Qwen3)
            // emit their answer into the reasoning channel, leaving content empty.
            // Prompted JSON works across all five providers; ParsePayload tolerates
            // fences/think-tags and the rules fallback absorbs anything malformed.
            var chatOptions = new ChatOptions
            {
                MaxOutputTokens = _options.MaxOutputTokens,
                Temperature = (float)_options.Temperature,
                Tools = tools
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
                    messages.Add(new ChatMessage(ChatRole.Assistant, text));
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
                return await _fallback.SuggestAsync(request, cancellationToken);
            }

            var suggestions = PostProcess(payload, request);
            // Weak models sometimes return fewer days than asked; fill the holes from
            // the deterministic rules rather than leaving the planner with empty days.
            suggestions = await BackfillMissingDaysAsync(requestId, suggestions, request, cancellationToken);
            _logger.LogInformation(
                "[{RequestId}] AI proposed {Count} meal suggestions via {Provider}/{Model} in {ElapsedMs}ms",
                requestId, suggestions.Count, _options.Provider, _options.Model, stopwatch.ElapsedMilliseconds);
            return suggestions;
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
            return await _fallback.SuggestAsync(request, cancellationToken);
        }
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

        var fallbackRequest = request with { DaysToFill = missing, AvailableFrozenItems = remainingFrozen };
        var filler = await _fallback.SuggestAsync(fallbackRequest, cancellationToken);
        return suggestions.Concat(filler).OrderBy(s => s.Date).ToList();
    }

    private const string SystemPrompt =
        """
        You are a meal planner for a family household. Propose a dinner for each
        requested date. Rules:
        - NEVER suggest dishes that conflict with the listed allergies or dietary constraints.
        - Prefer variety: avoid dishes eaten in the last two weeks.
        - Favor household favorites and dishes with high ratings; avoid low-rated dishes.
        - Use expiring freezer items where sensible. Freezer leftovers may not feed the whole
          household — check their notes for portion hints; you may propose two or three small
          leftovers for the SAME date (one suggestion entry per dish) to make a full dinner.
          Each freezer item lists the quantity available; never use an item more times across
          the week than that quantity (the stock is already reserved for what you plan).
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
        - Keep each reason to one short sentence.

        Reply with ONLY a JSON object in exactly this shape, no other text:
        {"suggestions":[{"date":"yyyy-MM-dd","dishName":"...","recipeTitle":"exact title or null","sourceUrl":"external recipe url or null","reason":"..."}]}
        """;

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Strips &lt;think&gt;/&lt;reasoning&gt; channels (closed or truncated) some
    /// models emit, so the JSON slice below can't pick up a brace from inside them.</summary>
    [GeneratedRegex(@"<(?:think|reasoning)>.*?(?:</(?:think|reasoning)>|$)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ReasoningBlockRegex();

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
                sb.Append($"; allergies: {string.Join(", ", member.Allergies)}");
            }
            if (member.Diets.Count > 0)
            {
                sb.Append($"; constraints: {string.Join(", ", member.Diets)}");
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
            sb.AppendLine("Freezer items (soonest expiring first):");
            foreach (var item in request.AvailableFrozenItems.Take(10))
            {
                sb.Append($"- {item.Name} ({item.Quantity} {item.Unit ?? "x"})");
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
        var recipeLines = request.KnownRecipes
            .Select(r => r.Category != null ? $"\"{r.Title}\" ({r.Category})" : $"\"{r.Title}\"")
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

        var suggestions = new List<MealSuggestion>();
        foreach (var item in payload.Suggestions!)
        {
            if (string.IsNullOrWhiteSpace(item.DishName)
                || !DateOnly.TryParse(item.Date, CultureInfo.InvariantCulture, out var date)
                || !validDates.Contains(date))
            {
                continue;
            }

            Guid? recipeId = null;
            if (!string.IsNullOrWhiteSpace(item.RecipeTitle)
                && recipesByTitle.TryGetValue(item.RecipeTitle.Trim(), out var byTitle))
            {
                recipeId = byTitle;
            }
            else if (recipesByTitle.TryGetValue(item.DishName.Trim(), out var byName))
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
                DishName = item.DishName.Trim(),
                Reason = string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason.Trim(),
                SourceUrl = sourceUrl,
                SourceName = sourceName
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
        FlagAllergyConflicts(result, request);
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
    /// Links a suggestion to an available freezer item when its dish name matches one,
    /// so accepting it reserves that stock. Capped per item by its remaining quantity —
    /// the model is told the available amount, this enforces it so the same stock isn't
    /// reserved more than it holds within one week.
    /// </summary>
    private static void LinkFreezerItems(List<MealSuggestion> suggestions, MealSuggestionRequest request)
    {
        if (request.AvailableFrozenItems.Count == 0)
        {
            return;
        }

        var byName = request.AvailableFrozenItems
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var used = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < suggestions.Count; i++)
        {
            var s = suggestions[i];
            if (s.FreezyItemRef != null || s.DishName is null
                || !byName.TryGetValue(s.DishName, out var item))
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
    /// Best-effort allergy net: flags (never drops) a suggestion whose linked recipe
    /// lists an ingredient name containing a household allergy term. Heuristic and
    /// secondary to the prompt instruction; only verifiable when a recipe is linked.
    /// </summary>
    private void FlagAllergyConflicts(List<MealSuggestion> suggestions, MealSuggestionRequest request)
    {
        var allergyTerms = request.Members
            .SelectMany(m => m.Allergies)
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (allergyTerms.Count == 0)
        {
            return;
        }

        for (var i = 0; i < suggestions.Count; i++)
        {
            var s = suggestions[i];
            if (s.RecipeId is null
                || !request.RecipeAllergens.TryGetValue(s.RecipeId.Value, out var allergens))
            {
                continue;
            }

            var hit = allergyTerms.FirstOrDefault(term =>
                allergens.Ingredients.Any(ing => ing.Contains(term, StringComparison.OrdinalIgnoreCase)));
            if (hit != null)
            {
                suggestions[i] = s with { AllergyWarning = $"May contain {hit} (household allergy)" };
                _logger.LogWarning(
                    "AI suggested \"{Dish}\" for {Date}; linked recipe has an ingredient matching the {Allergy} allergy",
                    s.DishName, s.Date, hit);
            }
        }
    }

    /// <summary>Expected JSON shape of the LLM response</summary>
    internal sealed record WeekSuggestionsPayload(List<DaySuggestionPayload>? Suggestions);

    internal sealed record DaySuggestionPayload(
        string? Date, string? DishName, string? RecipeTitle, string? Reason, string? SourceUrl);
}
