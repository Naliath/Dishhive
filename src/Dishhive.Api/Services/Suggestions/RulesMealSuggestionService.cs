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
/// rule that this provider ignores free-text instructions.
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
            suggestions.Add(new MealSuggestion
            {
                Date = date,
                DishName = item.Name,
                RecipeId = MatchRecipe(request, item.Name),
                Reason = $"From the freezer, expires {item.ExpirationDate:d MMMM}"
            });
        }

        var historyByDish = request.RecentDishes.ToDictionary(d => d.DishName, StringComparer.OrdinalIgnoreCase);
        var usedDishes = new HashSet<string>(
            suggestions.Select(s => s.DishName!), StringComparer.OrdinalIgnoreCase);

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
                    suggestions.Add(new MealSuggestion
                    {
                        Date = date,
                        DishName = pick,
                        RecipeId = MatchRecipe(request, pick),
                        Reason = $"From your #[{constraintsByDate[date].Name}] collection"
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
            .Where(f => !usedDishes.Contains(f.DishName))
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
                        suggestions.Add(new MealSuggestion
                        {
                            Date = date,
                            DishName = favorite.DishName,
                            RecipeId = MatchRecipe(request, favorite.DishName),
                            Reason = history?.AverageRating >= 4
                                ? $"{favorite.MemberName}'s favorite, rated {history.AverageRating:0.0}/5"
                                : $"{favorite.MemberName}'s favorite"
                        });
                        break;
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<MealSuggestion>>(
            suggestions.OrderBy(s => s.Date).ToList());
    }

    private static Guid? MatchRecipe(MealSuggestionRequest request, string dishName)
    {
        return request.KnownRecipes
            .FirstOrDefault(r => r.Title.Equals(dishName, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }
}
