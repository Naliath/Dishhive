using Dishhive.Api.Models;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Validates and repairs model output exclusively against structured intent.</summary>
internal static class PlanningIntentValidator
{
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
                if (!string.Equals(item.MealType ?? "dinner", constraint.MealType.ToString(), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(item.Course ?? "main", constraint.Course.ToString(), StringComparison.OrdinalIgnoreCase))
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

    public static WeekSuggestionsPayload Repair(
        WeekSuggestionsPayload payload,
        PlanningIntent intent,
        MealSuggestionRequest request,
        IReadOnlyList<ExternalRecipeTools.GetRecipeResult> externalCandidates)
    {
        var items = (payload.Suggestions ?? []).ToList();
        foreach (var constraint in intent.Constraints)
        {
            var targetDates = constraint.Dates.Count > 0
                ? constraint.Dates.Take(constraint.Count).ToList()
                : request.DaysToFill.Take(constraint.Count).ToList();
            if (targetDates.Count == 0) continue;

            var external = constraint.SourceHost == null ? [] : externalCandidates
                .Where(candidate => candidate.Error == null
                    && string.Equals(NormalizeHost(candidate.SourceSite), NormalizeHost(constraint.SourceHost), StringComparison.OrdinalIgnoreCase)
                    && FactsMatch(candidate.FactsAssessed, candidate.ContainsClasses, constraint))
                .DistinctBy(candidate => candidate.CandidateId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var local = constraint.SourceHost != null ? [] : request.KnownRecipes
                .Where(recipe => recipe.FactsAssessed
                    && FactsMatch(true, recipe.ContainsClasses, constraint))
                .DistinctBy(recipe => recipe.Id)
                .ToList();
            if (external.Count == 0 && local.Count == 0) continue;

            items.RemoveAll(item => (item.ConstraintIds ?? [])
                .Contains(constraint.Id, StringComparer.OrdinalIgnoreCase));
            for (var index = 0; index < targetDates.Count; index++)
            {
                DaySuggestionPayload replacement;
                if (external.Count > 0)
                {
                    var candidate = external[index % external.Count];
                    replacement = new DaySuggestionPayload(
                        targetDates[index].ToString("yyyy-MM-dd"), candidate.Title, null,
                        $"Verified selection for {constraint.Id}", candidate.CandidateId,
                        MealType: constraint.MealType.ToString().ToLowerInvariant(),
                        Course: constraint.Course.ToString().ToLowerInvariant(), AttendeeIds: [],
                        ConstraintIds: [constraint.Id]);
                }
                else
                {
                    var recipe = local[index % local.Count];
                    replacement = new DaySuggestionPayload(
                        targetDates[index].ToString("yyyy-MM-dd"), recipe.Title, recipe.Title,
                        $"Verified selection for {constraint.Id}", null,
                        MealType: constraint.MealType.ToString().ToLowerInvariant(),
                        Course: constraint.Course.ToString().ToLowerInvariant(), AttendeeIds: [],
                        ConstraintIds: [constraint.Id]);
                }
                items.RemoveAll(item => string.Equals(item.Date, replacement.Date, StringComparison.Ordinal)
                    && string.Equals(item.MealType ?? "dinner", replacement.MealType, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.Course ?? "main", replacement.Course, StringComparison.OrdinalIgnoreCase));
                items.Add(replacement);
            }
        }
        return new WeekSuggestionsPayload(items);
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
        var title = suggestion.RecipeTitle ?? suggestion.DishName;
        var recipe = request.KnownRecipes.FirstOrDefault(item =>
            string.Equals(item.Title, title, StringComparison.OrdinalIgnoreCase));
        sourceHost = null;
        facts = recipe?.ContainsClasses ?? [];
        assessed = recipe?.FactsAssessed == true;
        return recipe != null;
    }

    private static bool FactsMatch(bool assessed, IReadOnlyList<IngredientClass> facts, PlanningConstraint constraint)
    {
        if (constraint.RequiredClasses.Count == 0 && constraint.ExcludedClasses.Count == 0) return true;
        return assessed
            && constraint.RequiredClasses.All(facts.Contains)
            && !constraint.ExcludedClasses.Any(facts.Contains);
    }

    private static string Identity(DaySuggestionPayload item) =>
        !string.IsNullOrWhiteSpace(item.ExternalCandidateId)
            ? $"candidate:{item.ExternalCandidateId.Trim()}"
            : $"dish:{item.DishName?.Trim()}";

    private static string? NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        var value = host.Trim().ToLowerInvariant();
        return value.StartsWith("www.", StringComparison.Ordinal) ? value[4..] : value;
    }
}
