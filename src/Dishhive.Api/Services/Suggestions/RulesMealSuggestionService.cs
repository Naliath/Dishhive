namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// Deterministic suggestion rules, used as fallback when the LLM provider is
/// unavailable or fails. Pure function over the request, no I/O:
/// 1. Soonest-expiring freezer items (within 10 days) are slotted first.
/// 2. Days whose instruction references a #[Collection] get that collection's
///    least-recently-planned recipe (titles arrive in that order).
/// 3. Remaining days rotate favorites: skip dishes planned in the last 14 days
///    and dishes the household rated below 3; prefer loved (≥4), then
///    least-recently-planned; round-robin across members for fairness.
/// Global-instruction collection references are ignored, consistent with the
/// rule that this provider ignores free-text instructions. Never re-suggests a dish
/// name already present in the request's WeekPlan — that covers both concretely
/// planned days elsewhere in the week and (via LlmMealSuggestionService's backfill)
/// the AI's own picks on the same call, so a partial-week backfill can't duplicate one.
///
/// Deliberately does NOT combine multiple freezer items onto one day: this provider
/// has no signal at all for portion size (Freezy notes are free text, not structured),
/// so it can't judge whether stacking items actually adds up to enough food for the
/// household — guessing would risk under- or over-feeding. That judgment call belongs
/// to the LLM path (LlmMealSuggestionService), which can read the notes and household
/// size and combine only when it actually makes sense; see docs/features/ai-week-planning.md.
/// </summary>
public class RulesMealSuggestionService : IMealSuggestionService
{
    private const int FreezerExpiryWindowDays = 10;
    private const int VarietyWindowDays = 14;

    public bool IsEnabled => true;

    public Task<IReadOnlyList<MealSuggestion>> SuggestAsync(
        MealSuggestionRequest request, CancellationToken cancellationToken = default)
    {
        var suggestions = new List<MealSuggestion>();
        var remainingDays = new Queue<DateOnly>(request.DaysToFill.OrderBy(d => d));
        var today = request.WeekStart;

        // 1. Expiring freezer items first
        foreach (var item in request.AvailableFrozenItems
            .Where(i => i.ExpirationDate.HasValue
                && DateOnly.FromDateTime(i.ExpirationDate.Value) <= today.AddDays(6 + FreezerExpiryWindowDays))
            .OrderBy(i => i.ExpirationDate))
        {
            if (remainingDays.Count == 0)
            {
                break;
            }

            var date = remainingDays.Dequeue();
            var matchedRecipe = MatchRecipe(request, item.Name);
            suggestions.Add(new MealSuggestion
            {
                Date = date,
                DishName = matchedRecipe?.Title ?? item.Name,
                RecipeId = matchedRecipe?.Id,
                FreezyItemRef = item.Id,
                FreezyItemQuantity = 1,
                Reason = $"From the freezer, expires {item.ExpirationDate:d MMMM}",
                ReasonText = new("planningReason.freezer", ("date", new Dishhive.Api.Services.Localization.LocalizedUserValue(item.ExpirationDate!.Value, "d MMMM")))
            });
        }

        var historyByDish = request.RecentDishes.ToDictionary(d => d.DishName, StringComparer.OrdinalIgnoreCase);
        // Seeded from this call's own freezer picks AND the week's existing plan (concrete
        // dishes already on other days, and — when this is an AI backfill — the AI's own
        // picks passed in as WeekPlan entries too) so rules never re-suggests a dish that's
        // already elsewhere in the same week.
        var usedDishes = new HashSet<string>(
            suggestions.Select(s => s.DishName!), StringComparer.OrdinalIgnoreCase);
        foreach (var meal in request.WeekPlan)
        {
            if (meal.DishName != null)
            {
                usedDishes.Add(meal.DishName);
            }
        }

        // 2. Days constrained to a referenced collection pick from its titles
        // (least-recently-planned first), still honoring the variety window
        var constraintsByDate = request.CollectionConstraints
            .SelectMany(c => c.Dates.Select(date => (Date: date, Constraint: c)))
            .GroupBy(x => x.Date)
            .ToDictionary(g => g.Key, g => g.First().Constraint);

        if (constraintsByDate.Count > 0)
        {
            var unconstrained = new Queue<DateOnly>();
            while (remainingDays.Count > 0)
            {
                var date = remainingDays.Dequeue();
                var pick = constraintsByDate.TryGetValue(date, out var constraint)
                    ? constraint.RecipeTitles.FirstOrDefault(title =>
                        !usedDishes.Contains(title)
                        && !(historyByDish.TryGetValue(title, out var history)
                            && history.LastPlanned >= today.AddDays(-VarietyWindowDays)))
                    : null;

                if (pick != null)
                {
                    usedDishes.Add(pick);
                    var matchedRecipe = MatchRecipe(request, pick);
                    suggestions.Add(new MealSuggestion
                    {
                        Date = date,
                        DishName = matchedRecipe?.Title ?? pick,
                        RecipeId = matchedRecipe?.Id,
                        Reason = $"From your #[{constraintsByDate[date].Name}] collection",
                        ReasonText = new("planningReason.collection", ("name", constraintsByDate[date].Name))
                    });
                }
                else
                {
                    unconstrained.Enqueue(date);
                }
            }
            remainingDays = unconstrained;
        }

        // 3. Rotate favorites for the remaining days

        var candidates = request.Favorites
            .Select(favorite =>
            {
                var recipe = MatchFavoriteRecipe(request, favorite);
                return recipe == null ? favorite : favorite with { DishName = recipe.Title };
            })
            .Where(f => !usedDishes.Contains(f.DishName))
            // A favorite matching a recipe whose assessed facts conflict with an
            // attendee's allergy is skipped ENTIRELY — proposing the dish name
            // unlinked would dodge the exclusion, not honor it. (Collection picks
            // are covered upstream: excluded titles never reach the constraint.)
            .Where(f =>
            {
                // A linked favorite is resolved by its id. For a free-text favorite,
                // exact title/alias matching is used only as an allergy safety net;
                // it does not turn that favorite into a recipe link.
                var recipe = MatchFavoriteRecipe(request, f) ?? MatchRecipe(request, f.DishName);
                return recipe == null || !request.AllergyExcludedRecipeIds.Contains(recipe.Id);
            })
            .Where(f =>
            {
                if (!historyByDish.TryGetValue(f.DishName, out var history))
                {
                    return true; // never planned: fine
                }
                var recentlyPlanned = history.LastPlanned >= today.AddDays(-VarietyWindowDays);
                var disliked = history.AverageRating is < 3;
                return !recentlyPlanned && !disliked;
            })
            // Loved dishes first, then least recently planned
            .OrderByDescending(f => historyByDish.TryGetValue(f.DishName, out var h) && h.AverageRating >= 4)
            .ThenBy(f => historyByDish.TryGetValue(f.DishName, out var h) ? h.LastPlanned : DateOnly.MinValue)
            .ToList();

        // Round-robin across members so one person's favorites don't dominate the week
        var byMember = candidates
            .GroupBy(f => f.MemberName)
            .Select(g => new Queue<FavoriteDish>(g))
            .ToList();

        while (remainingDays.Count > 0 && byMember.Any(q => q.Count > 0))
        {
            foreach (var memberQueue in byMember)
            {
                if (remainingDays.Count == 0)
                {
                    break;
                }

                while (memberQueue.Count > 0)
                {
                    var favorite = memberQueue.Dequeue();
                    if (usedDishes.Add(favorite.DishName))
                    {
                        var date = remainingDays.Dequeue();
                        var history = historyByDish.GetValueOrDefault(favorite.DishName);
                        var matchedRecipe = MatchFavoriteRecipe(request, favorite);
                        suggestions.Add(new MealSuggestion
                        {
                            Date = date,
                            DishName = matchedRecipe?.Title ?? favorite.DishName,
                            RecipeId = matchedRecipe?.Id,
                            Reason = history?.AverageRating >= 4
                                ? $"{favorite.MemberName}'s favorite, rated {history.AverageRating:0.0}/5"
                                : $"{favorite.MemberName}'s favorite",
                            ReasonText = history?.AverageRating >= 4
                                ? new("planningReason.ratedFavorite", ("name", favorite.MemberName),
                                    ("rating", new Dishhive.Api.Services.Localization.LocalizedUserValue(history.AverageRating.Value, "0.0")))
                                : new("planningReason.favorite", ("name", favorite.MemberName))
                        });
                        break;
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<MealSuggestion>>(
            suggestions
                .Select(s => MealSuggestionPostProcessor.Normalize(
                    s with { Source = MealSuggestionSource.RulesFallback }, request))
                .OrderBy(s => s.Date)
                .ToList());
    }

    private static RecipeOption? MatchRecipe(MealSuggestionRequest request, string dishName) =>
        LocalRecipeResolver.ByTitle(request.KnownRecipes, dishName);

    private static RecipeOption? MatchFavoriteRecipe(
        MealSuggestionRequest request,
        FavoriteDish favorite) =>
        favorite.RecipeId is { } recipeId
            ? request.KnownRecipes.FirstOrDefault(recipe => recipe.Id == recipeId)
            : null;
}
