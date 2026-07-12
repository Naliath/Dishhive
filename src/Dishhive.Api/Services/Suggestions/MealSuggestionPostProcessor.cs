using Dishhive.Api.Models;
using Dishhive.Api.Services.Freezy;
using System.Globalization;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Deterministic boundary between model output and application suggestions:
/// resolves ids, enforces candidate provenance and source scope, caps freezer use,
/// and adds dietary warnings.</summary>
public sealed class MealSuggestionPostProcessor(ILogger<MealSuggestionPostProcessor> logger)
{
    public List<MealSuggestion> Process(
        WeekSuggestionsPayload payload,
        MealSuggestionRequest request,
        IExternalRecipeSession? externalRecipeSession)
    {
        var validDates = Enumerable.Range(0, 7).Select(request.WeekStart.AddDays).ToHashSet();
        var memberIds = request.Members
            .Where(member => member.Id != Guid.Empty)
            .Select(member => member.Id)
            .ToHashSet();
        var recipesByTitle = request.KnownRecipes
            .GroupBy(recipe => recipe.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
        var frozenById = request.AvailableFrozenItems
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var suggestions = new List<MealSuggestion>();

        foreach (var item in payload.Suggestions ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.DishName)
                || !DateOnly.TryParse(item.Date, CultureInfo.InvariantCulture, out var date)
                || !validDates.Contains(date))
            {
                continue;
            }

            var mealType = ParseEnum(item.MealType, MealType.Dinner);
            var course = ParseEnum(item.Course, Course.Main);
            if (request.WeekPlan.Any(meal => meal.Date == date
                    && meal.MealType == mealType
                    && meal.Course == course
                    && meal.DishName != null))
            {
                continue;
            }

            var attendeeIds = (item.AttendeeIds ?? [])
                .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
                .Where(memberIds.Contains)
                .Distinct()
                .ToList();
            if (item.AttendeeIds is { Count: > 0 } && attendeeIds.Count == 0)
            {
                logger.LogWarning("AI proposed only unknown attendee ids for {Date}; dropping that suggestion", date);
                continue;
            }
            if (item.AttendeeIds is not { Count: > 0 })
            {
                attendeeIds = memberIds.ToList();
            }

            FrozenItem? freezerItem = null;
            if (!string.IsNullOrWhiteSpace(item.FreezerItemId))
            {
                if (frozenById.TryGetValue(item.FreezerItemId.Trim(), out var matched))
                {
                    freezerItem = matched;
                }
                else
                {
                    logger.LogWarning(
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

            ExternalRecipeCandidate? externalCandidate = null;
            if (recipeId is null && !string.IsNullOrWhiteSpace(item.ExternalCandidateId))
            {
                if (externalRecipeSession?.TryResolveCandidate(item.ExternalCandidateId, out externalCandidate) != true)
                {
                    logger.LogWarning(
                        "AI proposed unknown or unfetched externalCandidateId {CandidateId} for {Date}; dropping that suggestion",
                        item.ExternalCandidateId, date);
                    continue;
                }
            }

            var requiresExternalCandidate = request.SourceConstraints.Any(constraint =>
                constraint.Dates.Contains(date));
            if (requiresExternalCandidate && externalCandidate == null)
            {
                logger.LogWarning(
                    "AI did not select a verified external candidate for source-constrained date {Date}; dropping that suggestion",
                    date);
                continue;
            }

            string? sourceUrl = null;
            string? sourceName = null;
            if (externalCandidate != null
                && ResolveExternalSource(externalCandidate.SourceUrl, date, request) is { } resolved)
            {
                sourceUrl = resolved.Url;
                sourceName = resolved.Name;
                dishName = externalCandidate.Title;
            }
            else if (externalCandidate != null)
            {
                continue;
            }

            suggestions.Add(new MealSuggestion
            {
                Date = date,
                MealType = mealType,
                Course = course,
                AttendeeIds = attendeeIds,
                RecipeId = recipeId,
                DishName = dishName,
                Reason = string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason.Trim(),
                SourceUrl = sourceUrl,
                SourceName = sourceName,
                ExternalIngredients = externalCandidate?.Ingredients ?? [],
                FreezyItemRef = freezerItem?.Id
            });
        }

        foreach (var constraint in request.CollectionConstraints.Where(constraint => constraint.Dates.Count > 0))
        {
            var titles = constraint.RecipeTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var offList in suggestions.Where(suggestion =>
                constraint.Dates.Contains(suggestion.Date) && !titles.Contains(suggestion.DishName!)))
            {
                logger.LogWarning(
                    "AI suggested \"{Dish}\" for {Date}, which is not in the referenced collection {Collection}",
                    offList.DishName, offList.Date, constraint.Name);
            }
        }

        var result = suggestions
            .GroupBy(suggestion => (suggestion.Date, suggestion.MealType, suggestion.Course,
                Dish: suggestion.DishName!.ToLowerInvariant()))
            .Select(group => group.First())
            .GroupBy(suggestion => suggestion.Date)
            .SelectMany(group => group.Take(6))
            .OrderBy(suggestion => suggestion.Date)
            .ThenBy(suggestion => suggestion.MealType)
            .ThenBy(suggestion => suggestion.Course)
            .ToList();
        if (!InstructionsAllowRepeats(request.Instructions))
        {
            var unique = new List<MealSuggestion>();
            var seenAcrossDates = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
            foreach (var suggestion in result)
            {
                var key = !string.IsNullOrWhiteSpace(suggestion.SourceUrl)
                    ? $"url:{suggestion.SourceUrl}"
                    : $"dish:{suggestion.DishName?.Trim()}";
                if (seenAcrossDates.TryGetValue(key, out var firstDate)
                    && firstDate != suggestion.Date)
                {
                    logger.LogWarning(
                        "Dropped repeated dish {Dish} on {Date}; it was already proposed on {FirstDate}",
                        suggestion.DishName, suggestion.Date, firstDate);
                    continue;
                }
                seenAcrossDates.TryAdd(key, suggestion.Date);
                unique.Add(suggestion);
            }
            result = unique;
        }
        LinkFreezerItems(result, request);
        return result;
    }

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : fallback;

    private static bool InstructionsAllowRepeats(string? instructions)
        => !string.IsNullOrWhiteSpace(instructions)
            && System.Text.RegularExpressions.Regex.IsMatch(instructions,
                @"\b(same|repeat|again|every\s+day|each\s+day|elke\s+dag|iedere\s+dag|herhaal|opnieuw)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public IReadOnlyList<MealSuggestion> Finalize(
        IReadOnlyList<MealSuggestion> suggestions,
        MealSuggestionRequest request)
    {
        var result = suggestions.ToList();
        FlagConstraintConflicts(result, request);
        return result;
    }

    private (string Url, string Name)? ResolveExternalSource(
        string? sourceUrl,
        DateOnly date,
        MealSuggestionRequest request)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        var match = request.SourceConstraints.FirstOrDefault(constraint =>
            string.Equals(constraint.Host, host, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            logger.LogWarning("AI proposed {Url}, which is not on any referenced source", uri);
            return null;
        }

        var dayConstraint = request.SourceConstraints.FirstOrDefault(constraint => constraint.Dates.Contains(date));
        if (dayConstraint != null
            && !string.Equals(dayConstraint.Host, host, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "AI proposed {Url} for {Date}, which is not on the referenced source {Source} ({Host})",
                uri, date, dayConstraint.Name, dayConstraint.Host);
            return null;
        }

        return (uri.AbsoluteUri, match.Name);
    }

    private static void LinkFreezerItems(
        List<MealSuggestion> suggestions,
        MealSuggestionRequest request)
    {
        if (request.AvailableFrozenItems.Count == 0)
        {
            return;
        }

        var byId = request.AvailableFrozenItems
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var byName = request.AvailableFrozenItems
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var used = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < suggestions.Count; index++)
        {
            var suggestion = suggestions[index];
            if (suggestion.FreezyItemRef != null)
            {
                if (!byId.TryGetValue(suggestion.FreezyItemRef, out var confirmed))
                {
                    continue;
                }
                var taken = used.GetValueOrDefault(confirmed.Id);
                if (taken >= confirmed.Quantity)
                {
                    suggestions[index] = suggestion with { FreezyItemRef = null, FreezyItemQuantity = 0 };
                    continue;
                }
                used[confirmed.Id] = taken + 1;
                suggestions[index] = suggestion with { FreezyItemQuantity = 1 };
                continue;
            }

            if (suggestion.DishName is null || !byName.TryGetValue(suggestion.DishName, out var item))
            {
                continue;
            }
            var nameTaken = used.GetValueOrDefault(item.Id);
            if (nameTaken >= item.Quantity)
            {
                continue;
            }
            used[item.Id] = nameTaken + 1;
            suggestions[index] = suggestion with { FreezyItemRef = item.Id, FreezyItemQuantity = 1 };
        }
    }

    private void FlagConstraintConflicts(
        List<MealSuggestion> suggestions,
        MealSuggestionRequest request)
    {
        var recipesById = request.KnownRecipes
            .GroupBy(recipe => recipe.Id)
            .ToDictionary(group => group.Key, group => group.First());
        for (var index = 0; index < suggestions.Count; index++)
        {
            var suggestion = suggestions[index];
            var attendees = suggestion.AttendeeIds.Count == 0
                ? request.Members
                : request.Members.Where(member => suggestion.AttendeeIds.Contains(member.Id)).ToList();
            var allergyByClass = BuildExclusions(attendees, member => member.Allergies);
            var dietByClass = BuildExclusions(attendees, member => member.Diets);
            var allergyTerms = attendees
                .SelectMany(member => member.Allergies.Select(allergy => allergy.Name.Trim()))
                .Where(allergy => allergy.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (allergyByClass.Count == 0 && dietByClass.Count == 0 && allergyTerms.Count == 0)
            {
                continue;
            }
            var inferredClasses = InferClassesFromDishName(suggestion.DishName);
            var inferredAllergyHits = inferredClasses.Where(allergyByClass.ContainsKey).ToList();
            var inferredDietHits = inferredClasses.Where(dietByClass.ContainsKey).ToList();
            if (inferredAllergyHits.Count > 0 || inferredDietHits.Count > 0)
            {
                suggestions[index] = suggestion = suggestion with
                {
                    AllergyWarning = inferredAllergyHits.Count == 0
                        ? suggestion.AllergyWarning
                        : $"Appears to contain {string.Join(", ", inferredAllergyHits)} — review {allergyByClass[inferredAllergyHits[0]]} allergy",
                    DietWarning = inferredDietHits.Count == 0
                        ? suggestion.DietWarning
                        : $"Appears to contain {string.Join(", ", inferredDietHits)} — review or replace portions for {dietByClass[inferredDietHits[0]]} diet"
                };
            }
            if (suggestion.RecipeId is null)
            {
                var externalHit = allergyTerms.FirstOrDefault(term =>
                    suggestion.ExternalIngredients.Any(ingredient =>
                        ingredient.Contains(term, StringComparison.OrdinalIgnoreCase)));
                if (externalHit != null)
                {
                    suggestions[index] = suggestion with
                    {
                        AllergyWarning = $"May contain {externalHit} (household allergy)"
                    };
                    logger.LogWarning(
                        "Suggested external recipe \"{Dish}\" for {Date}; fetched ingredients match the {Allergy} allergy",
                        suggestion.DishName, suggestion.Date, externalHit);
                }
                continue;
            }

            if (recipesById.TryGetValue(suggestion.RecipeId.Value, out var recipe) && recipe.FactsAssessed)
            {
                var allergyHits = recipe.ContainsClasses.Where(allergyByClass.ContainsKey).ToList();
                if (allergyHits.Count > 0)
                {
                    suggestions[index] = suggestion = suggestion with
                    {
                        AllergyWarning =
                            $"Contains {string.Join(", ", allergyHits)} — conflicts with {allergyByClass[allergyHits[0]]} allergy"
                    };
                    logger.LogWarning(
                        "Suggested \"{Dish}\" for {Date}; linked recipe contains [{Classes}] conflicting with a household allergy",
                        suggestion.DishName, suggestion.Date, string.Join(", ", allergyHits));
                }

                var dietHits = recipe.ContainsClasses.Where(dietByClass.ContainsKey).ToList();
                if (dietHits.Count > 0)
                {
                    suggestions[index] = suggestion with
                    {
                        DietWarning =
                            $"Contains {string.Join(", ", dietHits)} — conflicts with {dietByClass[dietHits[0]]} diet"
                    };
                    logger.LogInformation(
                        "Suggested \"{Dish}\" for {Date}; linked recipe contains [{Classes}] conflicting with a diet tag",
                        suggestion.DishName, suggestion.Date, string.Join(", ", dietHits));
                }
                continue;
            }

            if (!request.RecipeAllergens.TryGetValue(suggestion.RecipeId.Value, out var allergens))
            {
                continue;
            }
            var hit = allergyTerms.FirstOrDefault(term =>
                allergens.Ingredients.Any(ingredient =>
                    ingredient.Contains(term, StringComparison.OrdinalIgnoreCase)));
            if (hit != null)
            {
                suggestions[index] = suggestion with
                {
                    AllergyWarning = $"May contain {hit} (household allergy)"
                };
                logger.LogWarning(
                    "Suggested \"{Dish}\" for {Date}; linked recipe has an ingredient matching the {Allergy} allergy",
                    suggestion.DishName, suggestion.Date, hit);
            }
        }
    }

    private static HashSet<IngredientClass> InferClassesFromDishName(string? dishName)
    {
        var result = new HashSet<IngredientClass>();
        var text = $" {dishName} ".ToLowerInvariant();
        if (new[] { "chicken", "kip", "poulet" }.Any(text.Contains)) result.Add(IngredientClass.Poultry);
        if (new[] { "beef", "rund", "steak", "hamburger" }.Any(text.Contains)) result.Add(IngredientClass.RedMeat);
        if (new[] { "pork", "varken", "ham", "bacon" }.Any(text.Contains)) result.Add(IngredientClass.Pork);
        if (new[] { "fish", "vis", "salmon", "zalm", "tuna", "tonijn" }.Any(text.Contains)) result.Add(IngredientClass.Fish);
        return result;
    }

    private static Dictionary<IngredientClass, string> BuildExclusions(
        IEnumerable<MemberProfile> members,
        Func<MemberProfile, IReadOnlyList<DietaryTagProfile>> selectTags)
    {
        var result = new Dictionary<IngredientClass, string>();
        foreach (var member in members)
        {
            foreach (var tag in selectTags(member))
            {
                foreach (var ingredientClass in tag.ExcludedClasses)
                {
                    result.TryAdd(ingredientClass, $"{member.Name}'s \"{tag.Name}\"");
                }
            }
        }
        return result;
    }
}
