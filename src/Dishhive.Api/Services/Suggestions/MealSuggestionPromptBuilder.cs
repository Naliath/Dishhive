using Dishhive.Api.Models;
using Dishhive.Api.Services.Ai;
using System.Globalization;
using System.Text;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Owns the editable/protected system prompt and deterministic rendering of
/// a planning request into the model's user prompt.</summary>
public static class MealSuggestionPromptBuilder
{
    private const int FollowUpReserveTokens = 1024;

    public const string EditableSystemPromptDefault =
        """
        You are a meal planner for a family household. Propose a dinner for each requested date.
        - Prefer variety: avoid dishes eaten in the last two weeks.
        - Favor household favorites and dishes with high ratings; avoid low-rated dishes.
        - Keep each reason to one short sentence.
        """;

    public static string ProtectedSystemPrompt { get; } = BuildProtectedSystemPrompt();

    private static string BuildProtectedSystemPrompt()
    {
        var mealTypeField = AiJsonContract.PropertyName<DaySuggestionPayload>(
            nameof(DaySuggestionPayload.MealType));
        var courseField = AiJsonContract.PropertyName<DaySuggestionPayload>(
            nameof(DaySuggestionPayload.Course));
        var attendeeIdsField = AiJsonContract.PropertyName<DaySuggestionPayload>(
            nameof(DaySuggestionPayload.AttendeeIds));
        var dishNameField = AiJsonContract.PropertyName<DaySuggestionPayload>(
            nameof(DaySuggestionPayload.DishName));
        var recipeTitleField = AiJsonContract.PropertyName<DaySuggestionPayload>(
            nameof(DaySuggestionPayload.RecipeTitle));
        var localRecipeIdField = AiJsonContract.PropertyName<DaySuggestionPayload>(
            nameof(DaySuggestionPayload.LocalRecipeId));
        var freezerItemIdField = AiJsonContract.PropertyName<DaySuggestionPayload>(
            nameof(DaySuggestionPayload.FreezerItemId));
        var externalCandidateIdField = AiJsonContract.PropertyName<DaySuggestionPayload>(
            nameof(DaySuggestionPayload.ExternalCandidateId));
        var candidateIdField = AiJsonContract.PropertyName<ExternalRecipeTools.GetRecipeResult>(
            nameof(ExternalRecipeTools.GetRecipeResult.CandidateId));
        var dinner = AiJsonContract.EnumName(MealType.Dinner);
        var main = AiJsonContract.EnumName(Course.Main);
        return $$"""
        Rules that ALWAYS apply, regardless of the guidance above:
        - Default every proposal to {{mealTypeField}}="{{dinner}}" and {{courseField}}="{{main}}". Only add a
          different meal type or course when the planner explicitly asks for it (for
          example soup/appetizer, lunch, or dessert). Multiple courses and alternative
          mains on the same date must be separate suggestion entries.
        - {{attendeeIdsField}}=[] means the dish is for everyone. Treat a dish the planner explicitly
          requests for a date as one shared dish for all attendees, even when it appears to
          conflict with a diet (for example chicken while a vegetarian attends). Do not split
          attendees or invent an alternative; the review UI warns the planner so they can
          replace individual portions manually. Allergies remain hard constraints.
        - Never repeat a dish or external recipe on another date in the same week unless the
          planner explicitly asks for that repetition. A requested count means that many
          distinct recipes, not one recipe scheduled that many times.
        - NEVER suggest dishes that conflict with allergies. Respect dietary constraints
          for dishes you choose yourself; the explicit-request exception above applies only
          when the planner named the dish/type for that date.
        - Household tags may carry exact ingredient classes in brackets, e.g.
          "Vegetarisch [excludes: RedMeat, Poultry, Pork, Fish, ...]", and known
          recipes may carry "[contains: ...]" with the same class names ("[contains:
          none]" = verified to contain none of the tracked classes). A dish whose
          contained classes overlap ANY attendee's excluded classes is forbidden.
          Recipes conflicting with an allergy are already omitted from the known-
          recipes list. When any attendee has machine-checkable allergies, use only
          candidates with current assessed facts that pass those exclusions. A plain
          dish or unmatched freezer item has unknown ingredients and is unavailable.
          If too few distinct assessed meals remain, leave those dates without a
          suggestion. The application reports a partial plan; never invent a dish,
          repeat a meal, or relax an allergy to make the week appear complete.
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
          id EXACTLY into "{{freezerItemIdField}}" — this is how the app links it back to that
          stock; {{dishNameField}} does not need to match the item's name, a short label is fine
          (the app fills in the item's real name for tracking). Leave "{{freezerItemIdField}}"
          null for every dish that is not a freezer item. Never use an item more times
          across the week than the quantity listed for it (the stock is already reserved
          for what you plan).
        - When a day has a vague instruction (e.g. "something with fish" or "vegetarian"),
          every dish you suggest for that day must satisfy it.
        - Instructions may reference a recipe collection as #[Collection Name]. When a
          day's instruction references a collection, the dish for that day MUST be one of
          its member recipes. The names under "Referenced collections" are a ranked
          sample; membership is enforced by stable ids. Find the selected recipe in the
          known-recipes list and copy its exact current title into {{recipeTitleField}} and its
          exact id into {{localRecipeIdField}}. Honor mandatory collection ids in the
          normalized intent, including global requirements. Only explicitly soft
          preferences are optional. An empty required collection leaves an unmet requirement.
        - Instructions may reference an EXTERNAL website as @[Source] (listed under
          "Referenced sources" with its host). Dishhive performs that research before
          this planning call and supplies a bounded set of verified candidates. For every
          requested source-backed dish, choose only from those candidates, check its
          ingredients, time and type against every constraint, and copy its {{candidateIdField}}
          into "{{externalCandidateIdField}}". Use the recipe's real title as {{dishNameField}} and leave
          {{recipeTitleField}} and {{localRecipeIdField}} null (it is not in the store yet — it will be
          imported when accepted).
          For dates without a @[Source] reference, use eligible known recipes below.
          Plain dish names are allowed only when assessed ingredient facts are not
          required by an attendee's allergies. Never use a supplied external candidate
          for an unrelated source requirement. Leave an unavailable date empty.
        - When the planner gives additional instructions, they override the other
          preferences (never the allergies/constraints).
        - Treat the explicit weekday/date table in the request as authoritative. Never
          calculate weekday dates yourself or shift a weekday request to another date.
        - When recipe research returns verified candidates for a requested source, every
          requested source-backed dish must use one of those candidates. Never silently
          substitute a known/local recipe while describing it as coming from that source.
        - Prefer recipes from the known-recipes list. Every entry has a stable id and a
          currentTitle in the application's active language; some also have an
          originalTitleAlias for recognition only. When you use a known recipe, copy its
          exact id into {{localRecipeIdField}} and its exact currentTitle into BOTH
          {{recipeTitleField}} and {{dishNameField}}. Never output originalTitleAlias as the dish title.
          Leave {{localRecipeIdField}} null for freezer, external, and plain free-text dishes.

        Reply with ONLY a JSON object in exactly the documented response shape, no other text:
        {{MealSuggestionResponseContract.ExampleJson}}
        """;
    }

    public static string ComposeSystemPrompt(string? editableOverride)
    {
        var editable = string.IsNullOrWhiteSpace(editableOverride)
            ? EditableSystemPromptDefault
            : editableOverride.Trim();
        return editable + "\n\n" + ProtectedSystemPrompt;
    }

    /// <summary>
    /// Caps the variable user prompt so fixed system/intent/research messages and one
    /// bounded corrective turn still fit inside the execution gate's total input budget.
    /// </summary>
    public static int FitUserPromptTokenBudget(
        int requestedTokens,
        int maxInputTokens,
        params string[] fixedMessages)
    {
        var fixedCharacters = fixedMessages.Sum(message => (long)message.Length);
        var maxInputCharacters = (long)maxInputTokens
            * AiTokenBudget.ConservativeCharactersPerToken;
        var reserveCharacters = (long)FollowUpReserveTokens
            * AiTokenBudget.ConservativeCharactersPerToken;
        var availableUserCharacters = Math.Max(
            AiTokenBudget.PromptBuilderCharactersPerToken,
            maxInputCharacters - fixedCharacters - reserveCharacters);
        var availableBuilderTokens = availableUserCharacters
            / AiTokenBudget.PromptBuilderCharactersPerToken;
        return (int)Math.Min(Math.Max(1, requestedTokens), availableBuilderTokens);
    }

    public static string BuildUserPrompt(
        MealSuggestionRequest request,
        int maxPromptTokens = int.MaxValue)
    {
        var sb = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;
        var freezerItemIdField = AiJsonContract.PropertyName<DaySuggestionPayload>(
            nameof(DaySuggestionPayload.FreezerItemId));
        var localRecipeIdField = AiJsonContract.PropertyName<DaySuggestionPayload>(
            nameof(DaySuggestionPayload.LocalRecipeId));
        sb.AppendLine($"Week starting: {request.WeekStart:yyyy-MM-dd}");
        sb.AppendLine("Authoritative weekday/date mapping (weekday words in instructions map exactly to these dates):");
        foreach (var date in Enumerable.Range(0, 7).Select(request.WeekStart.AddDays))
        {
            sb.AppendLine($"- {date.DayOfWeek}: {date:yyyy-MM-dd}");
        }

        sb.AppendLine("Household:");
        foreach (var member in request.Members)
        {
            sb.Append($"- id={member.Id}: {member.Name}");
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
            foreach (var group in request.Favorites.GroupBy(favorite => favorite.MemberName))
            {
                sb.AppendLine($"- {group.Key}: {string.Join(", ", group.Select(favorite =>
                    favorite.RecipeId is { } recipeId
                        ? $"\"{favorite.DishName}\" [linked localRecipeId={recipeId}]"
                        : $"\"{favorite.DishName}\" [free text; localRecipeId must stay null]"))}");
            }
        }

        if (request.AvailableFrozenItems.Count > 0)
        {
            sb.AppendLine($"Freezer items (soonest expiring first; copy the id exactly into {freezerItemIdField}):");
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
                    ? $"for {string.Join(", ", constraint.Dates.Select(date => date.ToString("yyyy-MM-dd")))}"
                    : "general instructions";
                var titles = constraint.RecipeTitles.Count > 0
                    ? string.Join(", ", constraint.RecipeTitles.Select(title => $"\"{title}\""))
                    : "(no recipes in this collection)";
                sb.AppendLine($"- \"{constraint.Name}\" ({scope}): {titles}");
                sb.AppendLine($"  Collection id: {constraint.Id}; member count: {constraint.RecipeIds.Count}");
            }
        }

        if (request.SourceConstraints.Count > 0)
        {
            sb.AppendLine("Referenced sources (Dishhive researches these before final planning):");
            foreach (var constraint in request.SourceConstraints)
            {
                var scope = constraint.Dates.Count > 0
                    ? $"for {string.Join(", ", constraint.Dates.Select(date => date.ToString("yyyy-MM-dd")))}"
                    : "general instructions";
                sb.AppendLine($"- \"{constraint.Name}\" → {constraint.Host} ({scope})");
            }
        }

        var existing = request.WeekPlan
            .Where(meal => meal.DishName != null || meal.VagueInstruction != null)
            .OrderBy(meal => meal.Date)
            .ToList();
        if (existing.Count > 0)
        {
            sb.AppendLine("Existing plan this week:");
            foreach (var meal in existing)
            {
                sb.AppendLine(meal.DishName != null
                    ? $"- {meal.Date:yyyy-MM-dd} {meal.MealType}/{meal.Course}: \"{meal.DishName}\""
                    : $"- {meal.Date:yyyy-MM-dd} {meal.MealType}/{meal.Course}: vague: \"{meal.VagueInstruction}\"");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Instructions))
        {
            sb.AppendLine($"Additional instructions from the planner: {request.Instructions}");
        }

        var unlimited = maxPromptTokens >= int.MaxValue / AiTokenBudget.ConservativeCharactersPerToken;
        var budgetChars = unlimited ? int.MaxValue : maxPromptTokens * AiTokenBudget.ConservativeCharactersPerToken;
        var recentLines = request.RecentDishes
            .Select(dish => $"{dish.DishName} ({dish.LastPlanned:yyyy-MM-dd})")
            .ToList();
        var ratedLines = request.RecentDishes
            .Where(dish => dish.AverageRating.HasValue)
            .OrderByDescending(dish => dish.AverageRating)
            .Select(dish => $"{dish.DishName}: {dish.AverageRating!.Value.ToString("0.0", culture)}/5")
            .ToList();
        var recipeLines = request.KnownRecipes
            .Where(recipe => !request.AllergyExcludedRecipeIds.Contains(recipe.Id))
            .Select(recipe =>
            {
                var line = $"id={recipe.Id}; currentTitle=\"{recipe.Title}\"";
                if (!string.IsNullOrWhiteSpace(recipe.OriginalTitle)
                    && !string.Equals(recipe.OriginalTitle, recipe.Title, StringComparison.OrdinalIgnoreCase))
                {
                    line += $"; originalTitleAlias=\"{recipe.OriginalTitle}\"";
                }
                if (recipe.Category != null)
                {
                    line += $"; category=\"{recipe.Category}\"";
                }
                if (recipe.FactsAssessed)
                {
                    line += recipe.ContainsClasses.Count > 0
                        ? $" [contains: {string.Join(", ", recipe.ContainsClasses)}]"
                        : " [contains: none]";
                }
                return line;
            })
            .ToList();

        var historyBudget = unlimited ? int.MaxValue : (int)((budgetChars - sb.Length) * 0.4);
        var consumed = AppendBudgeted(
            sb,
            "Recent dinners (avoid repeating soon):",
            recentLines,
            historyBudget,
            minLines: 5);
        AppendBudgeted(
            sb,
            "Ratings (favor high, avoid low):",
            ratedLines,
            unlimited ? int.MaxValue : Math.Max(0, historyBudget - consumed),
            minLines: 5);
        AppendBudgeted(
            sb,
            $"Known recipes (copy id to {localRecipeIdField}; always display currentTitle):",
            recipeLines,
            unlimited ? int.MaxValue : Math.Max(0, budgetChars - sb.Length),
            minLines: 10);

        sb.AppendLine($"Selected dates are authoritative. Propose meals only for these dates, aiming for a dinner/main unless another slot was requested. Leave a date empty when no distinct eligible assessed meal can satisfy the allergy requirements: {string.Join(", ", request.DaysToFill.Select(date => date.ToString("yyyy-MM-dd")))}");
        return sb.ToString();
    }

    private static string FormatTag(DietaryTagProfile tag) => tag.ExcludedClasses.Count == 0
        ? tag.Name
        : $"{tag.Name} [excludes: {string.Join(", ", tag.ExcludedClasses)}]";

    private static int AppendBudgeted(
        StringBuilder sb,
        string header,
        IReadOnlyList<string> lines,
        int budgetChars,
        int minLines)
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
}
