using Dishhive.Api.Models;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
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
        // DaysToFill is authoritative. The model sees the whole weekday mapping for
        // language/date interpretation, but must never reintroduce an unselected,
        // already-planned, or passed date that the request builder left out.
        var validDates = request.DaysToFill.ToHashSet();
        var memberIds = request.Members
            .Where(member => member.Id != Guid.Empty)
            .Select(member => member.Id)
            .ToHashSet();
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

            var mealType = item.MealType;
            var course = item.Course;
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

            var modelDishName = item.DishName.Trim();
            var localRecipe = LocalRecipeResolver.ById(request.KnownRecipes, item.LocalRecipeId);
            if (!string.IsNullOrWhiteSpace(item.LocalRecipeId) && localRecipe == null)
            {
                logger.LogWarning(
                    "AI proposed unknown localRecipeId {RecipeId} for {Date}; trying exact title aliases instead",
                    item.LocalRecipeId, date);
            }
            localRecipe ??= LocalRecipeResolver.ByTitle(
                request.KnownRecipes, item.RecipeTitle, modelDishName);
            var recipeId = localRecipe?.Id;
            var dishName = freezerItem?.Name ?? localRecipe?.Title ?? modelDishName;

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

            var claimedConstraints = request.Intent?.Constraints
                .Where(constraint => (item.ConstraintIds ?? []).Contains(
                    constraint.Id, StringComparer.OrdinalIgnoreCase))
                .ToList() ?? [];
            var requiresExternalCandidate = claimedConstraints.Any(constraint =>
                constraint.SourceHost != null);
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
                && ResolveExternalSource(externalCandidate.SourceUrl, date, mealType, course, request) is { } resolved)
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
                ConstraintIds = item.ConstraintIds ?? [],
                ResolvedContainsClasses = externalCandidate?.ContainsClasses
                    ?? request.KnownRecipes.FirstOrDefault(recipe => recipe.Id == recipeId)?.ContainsClasses
                    ?? [],
                ResolvedFactsAssessed = externalCandidate?.FactsAssessed
                    ?? request.KnownRecipes.FirstOrDefault(recipe => recipe.Id == recipeId)?.FactsAssessed
                    ?? false,
                ResolvedTotalTimeMinutes = externalCandidate?.TotalTimeMinutes
                    ?? request.KnownRecipes.FirstOrDefault(recipe => recipe.Id == recipeId)?.TotalTimeMinutes,
                ExternalIngredients = externalCandidate?.Ingredients ?? (recipeId is { } ingredientRecipeId
                    ? request.RecipeAllergens.GetValueOrDefault(ingredientRecipeId)?.Ingredients ?? [] : []),
                ResearchConstraintIds = externalCandidate?.ResearchConstraintIds ?? [],
                CandidateVersion = externalCandidate is not null
                    ? SuggestionCandidateVersion.ForContent(externalCandidate.Title,
                        externalCandidate.Ingredients, steps: externalCandidate.Instructions,
                        totalTimeMinutes: externalCandidate.TotalTimeMinutes)
                    : recipeId is { } localId
                        ? SuggestionCandidateVersion.ForLocal(request.KnownRecipes
                            .First(recipe => recipe.Id == localId))
                        : null,
                FreezyItemRef = freezerItem?.Id
            });
        }

        foreach (var constraint in request.CollectionConstraints)
        {
            foreach (var offList in suggestions.Where(suggestion =>
                constraint.AppliesTo(new(suggestion.Date, suggestion.MealType, suggestion.Course))
                && (suggestion.RecipeId is not { } id || !constraint.RecipeIds.Contains(id))))
            {
                logger.LogWarning(
                    "AI suggested \"{Dish}\" for {Date}, which is not in the referenced collection {Collection}",
                    offList.DishName, offList.Date, constraint.Name);
            }
        }

        var result = suggestions
            .GroupBy(suggestion => (suggestion.Date, suggestion.MealType, suggestion.Course,
                Identity: SuggestionIdentity(suggestion)))
            .Select(group => group.First())
            .GroupBy(suggestion => suggestion.Date)
            .SelectMany(group => group.Take(6))
            .OrderBy(suggestion => suggestion.Date)
            .ThenBy(suggestion => suggestion.MealType)
            .ThenBy(suggestion => suggestion.Course)
            .ToList();
        if (request.Intent?.AllowRepeatedDishes != true)
        {
            var unique = new List<MealSuggestion>();
            var seenAcrossDates = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
            foreach (var suggestion in result)
            {
                var key = SuggestionIdentity(suggestion);
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

    private static string SuggestionIdentity(MealSuggestion suggestion) =>
        suggestion.RecipeId is { } recipeId
            ? $"recipe:{recipeId}"
            : !string.IsNullOrWhiteSpace(suggestion.SourceUrl)
                ? $"url:{suggestion.SourceUrl.Trim()}"
                : $"dish:{suggestion.DishName?.Trim().ToLowerInvariant()}";

    public IReadOnlyList<MealSuggestion> Finalize(
        IReadOnlyList<MealSuggestion> suggestions,
        MealSuggestionRequest request)
    {
        var result = suggestions.Select(suggestion => Normalize(suggestion, request)).ToList();
        FlagCanonicalConstraintConflicts(result, request);
        return result;
    }

    internal static MealSuggestion Normalize(MealSuggestion suggestion, MealSuggestionRequest request)
    {
        var recipe = request.KnownRecipes.FirstOrDefault(recipe => recipe.Id == suggestion.RecipeId);
        return suggestion with
        {
            AttendeeIds = suggestion.AttendeeIds.Count > 0 ? suggestion.AttendeeIds
                : request.Members.Select(member => member.Id).Where(id => id != Guid.Empty).Distinct().ToList(),
            CandidateVersion = recipe is null ? suggestion.CandidateVersion
                : SuggestionCandidateVersion.ForLocal(recipe),
            ResolvedFactsAssessed = recipe?.FactsAssessed ?? suggestion.ResolvedFactsAssessed,
            ResolvedContainsClasses = recipe?.ContainsClasses ?? suggestion.ResolvedContainsClasses,
            ResolvedTotalTimeMinutes = recipe?.TotalTimeMinutes ?? suggestion.ResolvedTotalTimeMinutes,
            ExternalIngredients = recipe is null ? suggestion.ExternalIngredients
                : request.RecipeAllergens.GetValueOrDefault(recipe.Id)?.Ingredients ?? []
        };
    }

    private (string Url, string Name)? ResolveExternalSource(
        string? sourceUrl,
        DateOnly date,
        MealType mealType,
        Course course,
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

        var requiredHost = request.Intent?.Constraints.FirstOrDefault(constraint =>
            constraint.SourceHost != null && constraint.Dates.Contains(date)
                && constraint.MealType == mealType && constraint.Course == course)?.SourceHost;
        if (requiredHost != null
            && !string.Equals(requiredHost, host, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "AI proposed {Url} for {Date}, which is not on the referenced source {Source} ({Host})",
                uri, date, requiredHost, requiredHost);
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

    private void FlagCanonicalConstraintConflicts(
        List<MealSuggestion> suggestions,
        MealSuggestionRequest request)
    {
        for (var index = 0; index < suggestions.Count; index++)
        {
            var suggestion = suggestions[index];
            if (!suggestion.ResolvedFactsAssessed && suggestion.RecipeId is { } recipeId)
            {
                var known = request.KnownRecipes.FirstOrDefault(recipe => recipe.Id == recipeId);
                if (known != null)
                {
                    suggestion = suggestion with
                    {
                        ResolvedFactsAssessed = known.FactsAssessed,
                        ResolvedContainsClasses = known.ContainsClasses,
                        ResolvedTotalTimeMinutes = known.TotalTimeMinutes
                    };
                }
            }
            var attendees = suggestion.AttendeeIds.Count == 0
                ? request.Members
                : request.Members.Where(member => suggestion.AttendeeIds.Contains(member.Id)).ToList();
            var allergyByClass = BuildExclusions(attendees, member => member.Allergies);
            var dietByClass = BuildExclusions(attendees, member => member.Diets);
            var hasAllergyTags = attendees.Any(member => member.Allergies.Count > 0);
            var explicitlyOverridesDiet = request.Intent?.Constraints.Any(constraint =>
                constraint.OverrideDietPreferences
                && suggestion.ConstraintIds.Contains(constraint.Id, StringComparer.OrdinalIgnoreCase)) == true;
            if (!hasAllergyTags && dietByClass.Count == 0) continue;

            if (!suggestion.ResolvedFactsAssessed)
            {
                suggestions[index] = suggestion with
                {
                    AllergyWarning = !hasAllergyTags ? null
                        : "Dietary facts have not been verified; review household allergies",
                    AllergyWarningText = !hasAllergyTags ? null : new("planningWarnings.unverifiedAllergies"),
                    DietWarningText = dietByClass.Count == 0 ? null : new(explicitlyOverridesDiet
                        ? "planningWarnings.unverifiedOverride" : "planningWarnings.unverifiedDiets"),
                    DietWarning = dietByClass.Count == 0 ? null
                        : explicitlyOverridesDiet
                            ? "Explicitly requested for everyone; dietary facts are unverified, so review or replace individual portions"
                            : "Dietary facts have not been verified; review household diets"
                };
                continue;
            }

            var allergyHits = suggestion.ResolvedContainsClasses.Where(allergyByClass.ContainsKey).ToList();
            var dietHits = suggestion.ResolvedContainsClasses.Where(dietByClass.ContainsKey).ToList();
            suggestions[index] = suggestion with
            {
                AllergyWarning = allergyHits.Count == 0 ? null
                    : $"Contains {string.Join(", ", allergyHits)} — conflicts with {allergyByClass[allergyHits[0]]} allergy",
                AllergyWarningText = allergyHits.Count == 0 ? null : new("planningWarnings.allergyConflict",
                    ("classes", allergyHits), ("member", allergyByClass[allergyHits[0]].MemberName),
                    ("tag", allergyByClass[allergyHits[0]].TagName)),
                ResolvedAllergyConflict = allergyHits.Count > 0,
                DietWarning = dietHits.Count == 0 ? null
                    : explicitlyOverridesDiet
                        ? $"Explicitly requested with {string.Join(", ", dietHits)} despite {dietByClass[dietHits[0]]} diet; review or replace individual portions"
                        : $"Contains {string.Join(", ", dietHits)} — conflicts with {dietByClass[dietHits[0]]} diet",
                DietWarningText = dietHits.Count == 0 ? null : new(explicitlyOverridesDiet
                    ? "planningWarnings.dietOverride" : "planningWarnings.dietConflict",
                    ("classes", dietHits), ("member", dietByClass[dietHits[0]].MemberName),
                    ("tag", dietByClass[dietHits[0]].TagName))
            };
            if (allergyHits.Count > 0)
            {
                logger.LogWarning(
                    "Suggested \"{Dish}\" for {Date}; verified facts contain [{Classes}] conflicting with a household allergy",
                    suggestion.DishName, suggestion.Date, string.Join(", ", allergyHits));
            }
        }
    }

    private sealed record DietaryExclusion(string MemberName, string TagName)
    {
        public override string ToString() => $"{MemberName}'s \"{TagName}\"";
    }

    private static Dictionary<IngredientClass, DietaryExclusion> BuildExclusions(
        IEnumerable<MemberProfile> members,
        Func<MemberProfile, IReadOnlyList<DietaryTagProfile>> selectTags)
    {
        var result = new Dictionary<IngredientClass, DietaryExclusion>();
        foreach (var member in members)
        {
            foreach (var tag in selectTags(member))
            {
                foreach (var ingredientClass in tag.ExcludedClasses)
                {
                    result.TryAdd(ingredientClass, new(member.Name, tag.Name));
                }
            }
        }
        return result;
    }
}
