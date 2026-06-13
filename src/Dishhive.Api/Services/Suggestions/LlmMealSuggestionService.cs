using Microsoft.Extensions.AI;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// LLM-backed week-plan suggestions via a Microsoft.Extensions.AI IChatClient
/// (provider selected by configuration, see ChatClientFactory).
/// Builds a prompt from the household context and demands structured JSON output.
/// Any failure — timeout, HTTP error, malformed response — falls back to the
/// deterministic rules provider: suggestions must never break planning.
/// </summary>
public class LlmMealSuggestionService : IMealSuggestionService
{
    private readonly IChatClient _chatClient;
    private readonly RulesMealSuggestionService _fallback;
    private readonly AiOptions _options;
    private readonly ILogger<LlmMealSuggestionService> _logger;

    public LlmMealSuggestionService(
        IChatClient chatClient,
        RulesMealSuggestionService fallback,
        AiOptions options,
        ILogger<LlmMealSuggestionService> logger)
    {
        _chatClient = chatClient;
        _fallback = fallback;
        _options = options;
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

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var systemPrompt = _options.DisableThinking ? "/no_think\n" + SystemPrompt : SystemPrompt;
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, BuildUserPrompt(request))
            };

            // Plain text completion with the JSON shape described in the prompt, parsed
            // manually. Native response_format is deliberately avoided: LM Studio rejects
            // json_object outright, and with json_schema reasoning models (e.g. Qwen3)
            // emit their answer into the reasoning channel, leaving content empty.
            // Prompted JSON works across all five providers; ParsePayload tolerates
            // fences/think-tags and the rules fallback absorbs anything malformed.
            var response = await _chatClient.GetResponseAsync(
                messages,
                new ChatOptions { MaxOutputTokens = _options.MaxOutputTokens },
                cancellationToken: timeout.Token);

            var payload = ParsePayload(response.Text);
            if (payload?.Suggestions is null)
            {
                var text = response.Text ?? "";
                _logger.LogWarning(
                    "AI suggestion response could not be parsed; using rules fallback. Length={Length}, start: {Snippet}",
                    text.Length, text.Length > 300 ? text[..300] : text);
                return await _fallback.SuggestAsync(request, cancellationToken);
            }

            var suggestions = PostProcess(payload, request);
            _logger.LogInformation("AI proposed {Count} meal suggestions via {Provider}/{Model}",
                suggestions.Count, _options.Provider, _options.Model);
            return suggestions;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller cancelled (request aborted); don't mask it
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI suggestion call failed ({Provider}/{Model}); using rules fallback",
                _options.Provider, _options.Model);
            return await _fallback.SuggestAsync(request, cancellationToken);
        }
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
        - When a day has a vague instruction (e.g. "something with fish" or "vegetarian"),
          every dish you suggest for that day must satisfy it.
        - Instructions may reference a recipe collection as #[Collection Name]. When a
          day's instruction references a collection, the dish for that day MUST be one of
          the recipes listed under "Referenced collections" for it (copy the exact title
          into recipeTitle). When the planner's general instructions reference one, prefer
          its recipes for the matching wish. If a referenced collection has no recipe list
          below, treat the reference as a plain-text hint.
        - When the planner gives additional instructions, they override the other
          preferences (never the allergies/constraints).
        - Prefer recipes from the known-recipes list; when you use one, copy its exact title
          into recipeTitle.
        - Keep each reason to one short sentence.

        Reply with ONLY a JSON object in exactly this shape, no other text:
        {"suggestions":[{"date":"yyyy-MM-dd","dishName":"...","recipeTitle":"exact title or null","reason":"..."}]}
        """;

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Extracts the payload from the model's text: tolerates markdown fences,
    /// reasoning preambles and trailing prose by slicing the outermost JSON object.
    /// </summary>
    internal static WeekSuggestionsPayload? ParsePayload(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WeekSuggestionsPayload>(
                text[start..(end + 1)], PayloadJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // internal for prompt-content tests (like ParsePayload)
    internal static string BuildUserPrompt(MealSuggestionRequest request)
    {
        var sb = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

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

        if (request.RecentDishes.Count > 0)
        {
            sb.AppendLine("Recent history (last 90 days):");
            foreach (var dish in request.RecentDishes.Take(40))
            {
                sb.Append($"- {dish.DishName}: planned {dish.TimesPlanned}x, eaten {dish.TimesEaten}x");
                if (dish.AverageRating.HasValue)
                {
                    sb.Append($", rated {dish.AverageRating.Value.ToString("0.0", culture)}/5");
                }
                sb.AppendLine($", last {dish.LastPlanned:yyyy-MM-dd}");
            }
        }

        if (request.KnownRecipes.Count > 0)
        {
            sb.AppendLine("Known recipes:");
            foreach (var recipe in request.KnownRecipes.Take(60))
            {
                sb.AppendLine(recipe.Category != null
                    ? $"- \"{recipe.Title}\" ({recipe.Category})"
                    : $"- \"{recipe.Title}\"");
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

        sb.AppendLine($"Propose dinners for: {string.Join(", ", request.DaysToFill.Select(d => d.ToString("yyyy-MM-dd")))}");
        return sb.ToString();
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

            suggestions.Add(new MealSuggestion
            {
                Date = date,
                RecipeId = recipeId,
                DishName = item.DishName.Trim(),
                Reason = string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason.Trim()
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
        return suggestions
            .GroupBy(s => (s.Date, Dish: s.DishName!.ToLowerInvariant()))
            .Select(g => g.First())
            .GroupBy(s => s.Date)
            .SelectMany(g => g.Take(3))
            .OrderBy(s => s.Date)
            .ToList();
    }

    /// <summary>Expected JSON shape of the LLM response</summary>
    internal sealed record WeekSuggestionsPayload(List<DaySuggestionPayload>? Suggestions);

    internal sealed record DaySuggestionPayload(string? Date, string? DishName, string? RecipeTitle, string? Reason);
}
