using System.Globalization;
using System.Text;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Owns the editable/protected system prompt and deterministic rendering of
/// a planning request into the model's user prompt.</summary>
public static class MealSuggestionPromptBuilder
{
    public const string EditableSystemPromptDefault =
        """
        You are a meal planner for a family household. Propose a dinner for each
        requested date.
        - Prefer variety: avoid dishes eaten in the last two weeks.
        - Favor household favorites and dishes with high ratings; avoid low-rated dishes.
        - Keep each reason to one short sentence.
        """;

    public const string ProtectedSystemPrompt =
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
            * get_recipe(candidateId) — read a candidate and CHECK it meets every constraint
              (time limit, vegetarian, etc.) before choosing it.
          When you propose such an external recipe, copy the candidateId returned by the
          successful get_recipe call into "externalCandidateId", use the recipe's real title
          as dishName, and leave recipeTitle null (it is not in the store yet — it will be
          imported when accepted). Do NOT use these tools for any other day or wish — every
          day without a @[Source] reference must be filled from the known-recipes list below
          or a plain dish name, never a web search.
        - When the planner gives additional instructions, they override the other
          preferences (never the allergies/constraints).
        - Prefer recipes from the known-recipes list; when you use one, copy its exact title
          into recipeTitle.

        Reply with ONLY a JSON object in exactly the documented response shape, no other text:
        """ + "\n" + MealSuggestionResponseContract.ExampleJson;

    public static string ComposeSystemPrompt(string? editableOverride)
    {
        var editable = string.IsNullOrWhiteSpace(editableOverride)
            ? EditableSystemPromptDefault
            : editableOverride.Trim();
        return editable + "\n\n" + ProtectedSystemPrompt;
    }

    public static string BuildUserPrompt(
        MealSuggestionRequest request,
        int maxPromptTokens = int.MaxValue)
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
                sb.AppendLine($"- {group.Key}: {string.Join(", ", group.Select(favorite => favorite.DishName))}");
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
                    ? $"for {string.Join(", ", constraint.Dates.Select(date => date.ToString("yyyy-MM-dd")))}"
                    : "general instructions";
                var titles = constraint.RecipeTitles.Count > 0
                    ? string.Join(", ", constraint.RecipeTitles.Select(title => $"\"{title}\""))
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
                    ? $"- {meal.Date:yyyy-MM-dd}: \"{meal.DishName}\""
                    : $"- {meal.Date:yyyy-MM-dd}: vague: \"{meal.VagueInstruction}\"");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Instructions))
        {
            sb.AppendLine($"Additional instructions from the planner: {request.Instructions}");
        }

        var unlimited = maxPromptTokens >= int.MaxValue / 4;
        var budgetChars = unlimited ? int.MaxValue : maxPromptTokens * 4;
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
                var line = recipe.Category != null
                    ? $"\"{recipe.Title}\" ({recipe.Category})"
                    : $"\"{recipe.Title}\"";
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
            "Known recipes (prefer these; copy the exact title):",
            recipeLines,
            unlimited ? int.MaxValue : Math.Max(0, budgetChars - sb.Length),
            minLines: 10);

        sb.AppendLine($"Propose dinners for: {string.Join(", ", request.DaysToFill.Select(date => date.ToString("yyyy-MM-dd")))}");
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
