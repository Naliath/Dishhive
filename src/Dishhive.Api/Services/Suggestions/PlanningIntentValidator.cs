using Dishhive.Api.Models;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Shared final-domain invariants and structured model-payload validation.</summary>
internal static class PlanningIntentValidator
{
    internal static IReadOnlyList<UnmetPlanningRequirement> Contradictions(PlanningIntent intent)
    {
        var issues = new List<UnmetPlanningRequirement>();
        foreach (var constraint in intent.Constraints)
        {
            var together = intent.Constraints.Where(other => other.Id == constraint.Id
                || (constraint.Dates.Count > 0 && other.Dates.Intersect(constraint.Dates).Any()
                    && other.MealType == constraint.MealType && other.Course == constraint.Course)).ToList();
            if (together.SelectMany(other => other.RequiredClasses)
                .Intersect(together.SelectMany(other => other.ExcludedClasses)).Any())
                issues.Add(new UnmetPlanningRequirement(constraint.Id, "The same meal is required both to contain and to exclude an ingredient class").WithText("planningIssue.classContradiction"));
            if (!together.Select(other => other.AllowedOrigins.AsEnumerable())
                .Aggregate((left, right) => left.Intersect(right)).Any())
                issues.Add(new UnmetPlanningRequirement(constraint.Id, "The allowed origins for this meal contradict one another").WithText("planningIssue.originContradiction"));
        }
        return issues.DistinctBy(issue => (issue.Id, issue.Message)).ToList();
    }

    public sealed record FinalPlanValidation(
        IReadOnlyList<MealSuggestion> Suggestions,
        IReadOnlyList<UnmetPlanningRequirement> UnmetRequirements);

    /// <summary>
    /// Last invariant boundary. Invalid items are removed rather than returned with
    /// a warning; the resulting gaps are represented as unmet requirements.
    /// </summary>
    public static FinalPlanValidation ValidateFinal(
        IReadOnlyList<MealSuggestion> proposed,
        PlanningIntent intent,
        MealSuggestionRequest request)
    {
        var issues = new List<UnmetPlanningRequirement>();
        var accepted = new List<MealSuggestion>();
        var freezerUsed = 0;
        foreach (var suggestion in proposed)
        {
            var itemIssues = ValidateFinalItem(suggestion, intent, request).ToList();
            if (suggestion.FreezyItemRef != null && intent.MaxFreezerMeals is { } maxFreezer)
            {
                if (freezerUsed >= maxFreezer)
                    itemIssues.Add(new UnmetPlanningRequirement("freezer-cap", $"At most {maxFreezer} freezer meal(s) were requested").WithText("planningIssue.freezerCap", ("p0", maxFreezer)));
            }
            if (itemIssues.Count == 0)
            {
                accepted.Add(suggestion);
                if (suggestion.FreezyItemRef != null) freezerUsed++;
            }
            else issues.AddRange(itemIssues);
        }

        if (!intent.AllowRepeatedDishes)
        {
            var repeated = accepted.GroupBy(Identity, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(item => item.Date).Distinct().Count() > 1).ToList();
            foreach (var group in repeated)
            {
                foreach (var duplicate in group.Where(item => item.Date != group.First().Date).ToList())
                    accepted.Remove(duplicate);
                issues.Add(new UnmetPlanningRequirement("distinct-dishes", $"Repeated dish {group.First().DishName} was removed").WithText("planningIssue.repeatedDish", ("p0", group.First().DishName)));
            }
        }

        foreach (var constraint in intent.Constraints)
        {
            var claimed = accepted.Where(item => item.ConstraintIds.Contains(
                constraint.Id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (claimed.Count != constraint.Count)
                issues.Add(new UnmetPlanningRequirement(constraint.Id, $"Expected {constraint.Count} valid selection(s), but {claimed.Count} remain").WithText("planningIssue.selectionCount", ("p0", constraint.Count), ("p1", claimed.Count)));
            if (constraint.Distinct && claimed.Select(Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count() != claimed.Count)
                issues.Add(new UnmetPlanningRequirement(constraint.Id, "The required selections must be distinct").WithText("planningIssue.distinctSelections"));
            foreach (var date in constraint.Dates.Where(date => claimed.All(item => item.Date != date)))
                issues.Add(new UnmetPlanningRequirement(constraint.Id, $"No valid selection remains for {date:yyyy-MM-dd}").WithText("planningIssue.missingDate", ("p0", date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))));
        }

        foreach (var date in request.DaysToFill)
        {
            if (!accepted.Any(item => item.Date == date
                    && item.MealType == MealType.Dinner && item.Course == Course.Main))
                issues.Add(new UnmetPlanningRequirement($"day-{date:yyyy-MM-dd}", $"No assessed dinner main could be produced for {date:yyyy-MM-dd}")
                    .WithText("planningIssue.missingDinner", ("p0", new Dishhive.Api.Services.Localization.LocalizedUserValue(date, "dddd d MMMM yyyy"))));
        }
        foreach (var collection in request.CollectionConstraints)
        {
            var slots = collection.Slots.Count > 0 ? collection.Slots
                : collection.Dates.Select(date => new PlanningSlot(date)).ToList();
            foreach (var slot in slots.Where(slot => request.DaysToFill.Contains(slot.Date)))
            {
                if (!accepted.Any(item => item.Date == slot.Date && item.MealType == slot.MealType && item.Course == slot.Course
                        && item.RecipeId is { } id && collection.RecipeIds.Contains(id))
                    && !intent.Constraints.Any(constraint => constraint.RequiredCollectionIds.Contains(collection.Id)
                        && constraint.Dates.Contains(slot.Date) && constraint.MealType == slot.MealType && constraint.Course == slot.Course))
                    issues.Add(new UnmetPlanningRequirement("collection-" + collection.Name, $"No selection remains from required collection {collection.Name} for {slot.Date:yyyy-MM-dd}").WithText("planningIssue.missingCollection", ("p0", collection.Name), ("p1", slot.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))));
            }
        }

        return new FinalPlanValidation(
            accepted,
            issues.DistinctBy(issue => (issue.Id, issue.Message)).ToList());
    }

    internal static IEnumerable<UnmetPlanningRequirement> ValidateFinalItem(
        MealSuggestion suggestion,
        PlanningIntent intent,
        MealSuggestionRequest request)
    {
        if (!request.DaysToFill.Contains(suggestion.Date))
            yield return new UnmetPlanningRequirement("selected-dates", $"Suggestion for unselected date {suggestion.Date:yyyy-MM-dd} was removed").WithText("planningIssue.unselectedDate", ("p0", suggestion.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        if (suggestion.ResolvedAllergyConflict)
            yield return new UnmetPlanningRequirement("allergy-safety", $"{suggestion.DishName}: {suggestion.AllergyWarning}").WithText("planningIssue.allergyWarning", ("p0", suggestion.DishName), ("p1", (object?)suggestion.AllergyWarningText ?? suggestion.AllergyWarning));
        foreach (var unknown in suggestion.ConstraintIds.Where(id =>
                     !intent.Constraints.Any(constraint => string.Equals(constraint.Id, id, StringComparison.OrdinalIgnoreCase))))
            yield return new UnmetPlanningRequirement(unknown, "The suggestion claims an unknown requirement").WithText("planningIssue.unknownRequirement");

        if (suggestion.SourceUrl is not null && intent.Constraints.Any(constraint => constraint.SourceHost is not null && constraint.RequiresDishMatch)
            && !suggestion.ConstraintIds.Intersect(suggestion.ResearchConstraintIds, StringComparer.OrdinalIgnoreCase).Any())
            yield return new UnmetPlanningRequirement("source-scope", "The source recipe has no matching researched requirement for this slot")
                .WithText("planningIssue.wrongResearchScope", ("p0", suggestion.DishName ?? ""));

        foreach (var collection in request.CollectionConstraints.Where(c => c.AppliesTo(
                     new(suggestion.Date, suggestion.MealType, suggestion.Course))))
        {
            if (suggestion.RecipeId is not { } memberId || !collection.RecipeIds.Contains(memberId))
                yield return new UnmetPlanningRequirement($"collection-{collection.Name}", $"{suggestion.DishName} is not in required collection {collection.Name}").WithText("planningIssue.notInCollection", ("p0", suggestion.DishName), ("p1", collection.Name));
        }

        var known = suggestion.RecipeId is { } recipeId
            ? request.KnownRecipes.FirstOrDefault(recipe => recipe.Id == recipeId) : null;
        var assessed = suggestion.ResolvedFactsAssessed || known?.FactsAssessed == true;
        var facts = suggestion.ResolvedFactsAssessed ? suggestion.ResolvedContainsClasses : known?.ContainsClasses ?? [];
        var allergyClasses = request.Members.Where(member => suggestion.AttendeeIds.Count == 0 || suggestion.AttendeeIds.Contains(member.Id))
            .SelectMany(member => member.Allergies).SelectMany(tag => tag.ExcludedClasses).Distinct().ToList();
        if (!assessed && allergyClasses.Count > 0)
            yield return new UnmetPlanningRequirement("allergy-facts", $"Dietary facts for {suggestion.DishName} are not current").WithText("planningIssue.factsNotCurrent", ("p0", suggestion.DishName));
        if (assessed && allergyClasses.Any(facts.Contains))
            yield return new UnmetPlanningRequirement("allergy-safety", $"{suggestion.DishName} conflicts with an attendee's allergy").WithText("planningIssue.allergyConflict", ("p0", suggestion.DishName));
        if (suggestion.RecipeId is { } excludedId && request.AllergyExcludedRecipeIds.Contains(excludedId))
            yield return new UnmetPlanningRequirement("allergy-safety", $"{suggestion.DishName} is excluded for an attendee's allergy").WithText("planningIssue.allergyExcluded", ("p0", suggestion.DishName));

        var constraintsForSlot = intent.Constraints.Where(constraint =>
                suggestion.ConstraintIds.Contains(constraint.Id, StringComparer.OrdinalIgnoreCase)
                || (constraint.Dates.Contains(suggestion.Date)
                    && constraint.MealType == suggestion.MealType
                    && constraint.Course == suggestion.Course))
            .ToList();
        foreach (var constraint in constraintsForSlot)
        {
            if (!suggestion.ConstraintIds.Contains(constraint.Id, StringComparer.OrdinalIgnoreCase))
            {
                yield return new UnmetPlanningRequirement(constraint.Id, $"{suggestion.DishName} was not linked to the required constraint for this slot").WithText("planningIssue.missingConstraintLink", ("p0", suggestion.DishName));
                continue;
            }
            if (constraint.MealType != suggestion.MealType || constraint.Course != suggestion.Course
                || (constraint.Dates.Count > 0 && !constraint.Dates.Contains(suggestion.Date)))
            {
                yield return new UnmetPlanningRequirement(constraint.Id, $"{suggestion.DishName} does not occupy the required date, meal and course").WithText("planningIssue.wrongSlot", ("p0", suggestion.DishName));
                continue;
            }
            var origin = OriginOf(suggestion);
            if (!constraint.AllowedOrigins.Contains(origin))
                yield return new UnmetPlanningRequirement(constraint.Id, $"Origin {origin} is not allowed for {suggestion.DishName}").WithText("planningIssue.originExcluded", ("p0", origin), ("p1", suggestion.DishName));
            if (constraint.SourceHost is not null
                && (!Uri.TryCreate(suggestion.SourceUrl, UriKind.Absolute, out var uri)
                    || !string.Equals(NormalizeHost(uri.Host), NormalizeHost(constraint.SourceHost),
                        StringComparison.OrdinalIgnoreCase)))
                yield return new UnmetPlanningRequirement(constraint.Id, $"{suggestion.DishName} is not from {constraint.SourceHost}").WithText("planningIssue.wrongSource", ("p0", suggestion.DishName), ("p1", constraint.SourceHost));
            if (constraint.SourceHost is not null && suggestion.ResearchConstraintIds.Count > 0
                && !suggestion.ResearchConstraintIds.Contains(constraint.Id, StringComparer.OrdinalIgnoreCase))
                yield return new UnmetPlanningRequirement(constraint.Id, $"{suggestion.DishName} was researched for another requirement or course").WithText("planningIssue.wrongResearchScope", ("p0", suggestion.DishName));
            if (constraint.RequiredRecipeIds.Count > 0
                && (!suggestion.RecipeId.HasValue || !constraint.RequiredRecipeIds.Contains(suggestion.RecipeId.Value)))
                yield return new UnmetPlanningRequirement(constraint.Id, $"The specifically requested saved recipe was not selected").WithText("planningIssue.missingSavedRecipe");
            foreach (var collectionId in constraint.RequiredCollectionIds)
            {
                var collection = request.CollectionConstraints.FirstOrDefault(collection =>
                    string.Equals(collection.Id, collectionId, StringComparison.OrdinalIgnoreCase));
                if (collection is null || suggestion.RecipeId is not { } selectedId || !collection.RecipeIds.Contains(selectedId))
                    yield return new UnmetPlanningRequirement(constraint.Id, $"{suggestion.DishName} is not a member of required collection {collection?.Name ?? collectionId}").WithText("planningIssue.missingCollectionMember", ("p0", suggestion.DishName), ("p1", collection?.Name ?? collectionId));
            }
            if (constraint.RequiresDishMatch && !constraint.VerifiedDishVersions.Contains(PlanningDishIdentity.Key(suggestion), StringComparer.Ordinal))
                yield return new UnmetPlanningRequirement(constraint.Id, "The requested food identity has not been established for this candidate")
                    .WithText("planningIssue.missingDish", ("p0", constraint.DishRequest ?? constraint.SearchQuery ?? ""));
            if (!constraint.RequiresDishMatch && constraint.DishRequest is not null && constraint.RequiredRecipeIds.Count == 0
                && !(suggestion.DishName?.Contains(constraint.DishRequest,
                    StringComparison.OrdinalIgnoreCase) ?? false))
                yield return new UnmetPlanningRequirement(constraint.Id, $"The requested dish '{constraint.DishRequest}' was not selected").WithText("planningIssue.missingDish", ("p0", constraint.DishRequest));

            if ((constraint.RequiredClasses.Count > 0 || constraint.ExcludedClasses.Count > 0) && !assessed)
                yield return new UnmetPlanningRequirement(constraint.Id, $"Dietary facts for {suggestion.DishName} are not current").WithText("planningIssue.factsNotCurrent", ("p0", suggestion.DishName));
            else if (constraint.RequiredClasses.Any(value => !facts.Contains(value)))
                yield return new UnmetPlanningRequirement(constraint.Id, $"{suggestion.DishName} misses a required ingredient class").WithText("planningIssue.missingClass", ("p0", suggestion.DishName));
            else if (constraint.ExcludedClasses.Any(facts.Contains))
                yield return new UnmetPlanningRequirement(constraint.Id, $"{suggestion.DishName} contains an excluded ingredient class").WithText("planningIssue.excludedClass", ("p0", suggestion.DishName));

            var totalTime = suggestion.ResolvedTotalTimeMinutes ?? known?.TotalTimeMinutes;
            if (constraint.MaxTotalTimeMinutes is { } maxTime
                && (totalTime is null || totalTime > maxTime))
                yield return new UnmetPlanningRequirement(constraint.Id, $"{suggestion.DishName} is not verified within {maxTime} minutes").WithText("planningIssue.timeUnverified", ("p0", suggestion.DishName), ("p1", maxTime));
            if (constraint.AllAttendees
                && request.Members.Where(member => member.Id != Guid.Empty)
                    .Any(member => !suggestion.AttendeeIds.Contains(member.Id)))
                yield return new UnmetPlanningRequirement(constraint.Id, $"The shared requirement was not assigned to all attendees").WithText("planningIssue.missingAttendees");
        }
    }

    private static PlanningOrigin OriginOf(MealSuggestion suggestion) =>
        suggestion.FreezyItemRef is not null ? PlanningOrigin.Freezer
        : suggestion.RecipeId.HasValue ? PlanningOrigin.LocalRecipe
        : suggestion.SourceUrl is not null ? PlanningOrigin.ExternalRecipe
        : PlanningOrigin.UnlinkedDish;

    private static string Identity(MealSuggestion item) =>
        item.RecipeId is { } recipeId ? $"recipe:{recipeId}"
        : item.SourceUrl is not null ? $"url:{item.SourceUrl.Trim()}"
        : $"dish:{item.DishName?.Trim()}";

    public static IReadOnlyList<string> Validate(
        WeekSuggestionsPayload payload,
        PlanningIntent intent,
        MealSuggestionRequest request,
        IExternalRecipeSession? session)
    {
        var issues = new List<string>();
        var suggestions = payload.Suggestions ?? [];
        foreach (var constraint in intent.Constraints)
        {
            var claimed = suggestions.Where(item => (item.ConstraintIds ?? [])
                    .Contains(constraint.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (claimed.Count != constraint.Count)
            {
                issues.Add($"constraint {constraint.Id}: expected {constraint.Count} selection(s), got {claimed.Count}");
            }
            foreach (var date in constraint.Dates)
            {
                if (!claimed.Any(item => string.Equals(item.Date, date.ToString("yyyy-MM-dd"), StringComparison.Ordinal)))
                {
                    issues.Add($"constraint {constraint.Id}: missing date {date:yyyy-MM-dd}");
                }
            }
            foreach (var item in claimed)
            {
                if (item.MealType != constraint.MealType || item.Course != constraint.Course)
                {
                    issues.Add($"constraint {constraint.Id}: wrong meal type or course on {item.Date}");
                }
                if (constraint.AllAttendees && item.AttendeeIds is { Count: > 0 })
                {
                    issues.Add($"constraint {constraint.Id}: explicitly shared meal was split across attendees");
                }
                if (!TryResolveFacts(item, request, session, out var host, out var facts, out var assessed))
                {
                    host = null;
                    facts = [];
                    assessed = false;
                }
                if (constraint.SourceHost != null
                    && !string.Equals(NormalizeHost(host), NormalizeHost(constraint.SourceHost), StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"constraint {constraint.Id}: selection is not a verified candidate from {constraint.SourceHost}");
                }
                if (constraint.RequiresDishMatch && constraint.SourceHost is not null
                    && (session?.TryResolveCandidate(item.ExternalCandidateId, out var matchedFood) != true
                        || !constraint.VerifiedDishVersions.Contains(SuggestionCandidateVersion.ForContent(
                            matchedFood!.Title, matchedFood.Ingredients, steps: matchedFood.Instructions,
                            totalTimeMinutes: matchedFood.TotalTimeMinutes), StringComparer.Ordinal)))
                    issues.Add($"constraint {constraint.Id}: the source candidate does not have a verified match to the requested food");
                if (constraint.RequiredClasses.Count > 0 || constraint.ExcludedClasses.Count > 0)
                {
                    if (!assessed && !(constraint.OverrideDietPreferences && constraint.SourceHost == null))
                    {
                        issues.Add($"constraint {constraint.Id}: selected recipe has unverified dietary facts");
                    }
                    else if (assessed)
                    {
                        var missing = constraint.RequiredClasses.Where(value => !facts.Contains(value)).ToList();
                        var forbidden = constraint.ExcludedClasses.Where(facts.Contains).ToList();
                        if (missing.Count > 0)
                            issues.Add($"constraint {constraint.Id}: missing required classes [{string.Join(", ", missing)}]");
                        if (forbidden.Count > 0)
                            issues.Add($"constraint {constraint.Id}: contains excluded classes [{string.Join(", ", forbidden)}]");
                    }
                }
            }
            if (constraint.Distinct)
            {
                var distinct = claimed.Select(Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (distinct != claimed.Count)
                    issues.Add($"constraint {constraint.Id}: selections must be distinct");
            }
        }

        foreach (var item in suggestions.Where(item => !string.IsNullOrWhiteSpace(item.LocalRecipeId)))
        {
            if (!Guid.TryParse(item.LocalRecipeId?.Trim(), out var localRecipeId)
                || request.KnownRecipes.All(recipe => recipe.Id != localRecipeId))
            {
                issues.Add($"plan: unknown local recipe id {item.LocalRecipeId} on {item.Date}");
            }
            else if (request.AllergyExcludedRecipeIds.Contains(localRecipeId))
            {
                issues.Add($"plan: local recipe {localRecipeId} on {item.Date} conflicts with a household allergy");
            }

            if (!string.IsNullOrWhiteSpace(item.ExternalCandidateId)
                || !string.IsNullOrWhiteSpace(item.FreezerItemId))
            {
                issues.Add($"plan: local recipe id on {item.Date} cannot also identify an external or freezer item");
            }
        }

        foreach (var item in suggestions.Where(item => !string.IsNullOrWhiteSpace(item.ExternalCandidateId)))
        {
            var claimsSourceConstraint = intent.Constraints.Any(constraint =>
                constraint.SourceHost != null
                && (item.ConstraintIds ?? []).Contains(constraint.Id, StringComparer.OrdinalIgnoreCase));
            if (!claimsSourceConstraint)
            {
                issues.Add($"plan: external candidate on {item.Date} does not claim a source constraint");
            }
        }

        if (!intent.AllowRepeatedDishes)
        {
            var repeated = suggestions
                .Where(item => !string.IsNullOrWhiteSpace(item.DishName))
                .GroupBy(Identity, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(item => item.Date).Distinct(StringComparer.Ordinal).Count() > 1)
                .Select(group => group.First().DishName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (repeated.Count > 0)
                issues.Add($"plan: repeated recipes are not allowed [{string.Join(", ", repeated)}]");
        }
        return issues.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool TryResolveFacts(
        DaySuggestionPayload suggestion,
        MealSuggestionRequest request,
        IExternalRecipeSession? session,
        out string? sourceHost,
        out IReadOnlyList<IngredientClass> facts,
        out bool assessed)
    {
        if (session?.TryResolveCandidate(suggestion.ExternalCandidateId, out var candidate) == true
            && candidate != null)
        {
            sourceHost = new Uri(candidate.SourceUrl).Host;
            facts = candidate.ContainsClasses;
            assessed = candidate.FactsAssessed;
            return true;
        }
        var recipe = LocalRecipeResolver.Resolve(
            request.KnownRecipes,
            suggestion.LocalRecipeId,
            suggestion.RecipeTitle,
            suggestion.DishName);
        sourceHost = null;
        facts = recipe?.ContainsClasses ?? [];
        assessed = recipe?.FactsAssessed == true;
        return recipe != null;
    }

    private static string Identity(DaySuggestionPayload item) =>
        !string.IsNullOrWhiteSpace(item.ExternalCandidateId)
            ? $"candidate:{item.ExternalCandidateId.Trim()}"
            : !string.IsNullOrWhiteSpace(item.LocalRecipeId)
                ? $"recipe:{item.LocalRecipeId.Trim()}"
                : $"dish:{item.DishName?.Trim()}";

    private static string? NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        var value = host.Trim().ToLowerInvariant();
        return value.StartsWith("www.", StringComparison.Ordinal) ? value[4..] : value;
    }
}
