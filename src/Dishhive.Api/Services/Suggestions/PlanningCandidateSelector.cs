using Dishhive.Api.Models;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Bounded domain selection over resolved candidates, never over dish-name guesses.</summary>
internal static class PlanningCandidateSelector
{
    private const int MaxSearchNodes = 25_000;
    internal sealed record Result(IReadOnlyList<MealSuggestion> Suggestions,
        IReadOnlyList<UnmetPlanningRequirement> Diagnostics);
    private sealed record Option(MealSuggestion Suggestion, IReadOnlyList<int> Required, IReadOnlyList<int> Optional);

    public static Result Select(IReadOnlyList<MealSuggestion> proposed, PlanningIntent intent,
        MealSuggestionRequest request, IReadOnlyList<ExternalRecipeCandidate> externalCandidates,
        CancellationToken cancellationToken = default)
    {
        // A structurally complete proposal can still contain unverified plain dishes.
        // Prefer assessed saved alternatives for machine-checkable allergies before
        // accepting that proposal unchanged. Unknown dishes remain reviewable when
        // suitable alternatives are unavailable; no ingredient inference is made here.
        var preferAssessedAlternatives = request.Members.SelectMany(member => member.Allergies)
            .Any(tag => tag.ExcludedClasses.Count > 0)
            && request.KnownRecipes.Any(recipe => recipe.FactsAssessed)
            && proposed.Any(item => !item.ResolvedFactsAssessed
                && !request.KnownRecipes.Any(recipe => recipe.Id == item.RecipeId && recipe.FactsAssessed));
        if (intent.Constraints.Count == 0 && intent.MaxFreezerMeals is null
            && request.CollectionConstraints.All(collection => collection.Dates.Count == 0 && collection.Slots.Count == 0)
            && !preferAssessedAlternatives)
            return new(proposed, []);
        if (!preferAssessedAlternatives && PlanningIntentValidator.ValidateFinal(proposed, intent, request).UnmetRequirements.Count == 0)
            return new(proposed, []);

        var attendees = request.Members.Select(member => member.Id).Where(id => id != Guid.Empty).Distinct().ToList();
        var local = request.KnownRecipes.Select(recipe => new MealSuggestion
        {
            RecipeId = recipe.Id, DishName = recipe.Title, AttendeeIds = attendees,
            ExternalIngredients = request.RecipeAllergens.GetValueOrDefault(recipe.Id)?.Ingredients ?? [],
            ResolvedContainsClasses = recipe.ContainsClasses, ResolvedFactsAssessed = recipe.FactsAssessed,
            ResolvedTotalTimeMinutes = recipe.TotalTimeMinutes, CandidateVersion = SuggestionCandidateVersion.ForLocal(recipe),
            Reason = "Selected deterministically to satisfy planning requirements"
        });
        var external = externalCandidates.Select(candidate => new MealSuggestion
        {
            DishName = candidate.Title, SourceUrl = candidate.SourceUrl,
            SourceName = new Uri(candidate.SourceUrl).Host, AttendeeIds = attendees,
            ResolvedContainsClasses = candidate.ContainsClasses, ResolvedFactsAssessed = candidate.FactsAssessed,
            ResolvedTotalTimeMinutes = candidate.TotalTimeMinutes, ExternalIngredients = candidate.Ingredients,
            ResearchConstraintIds = candidate.ResearchConstraintIds,
            CandidateVersion = SuggestionCandidateVersion.ForContent(candidate.Title, candidate.Ingredients,
                steps: candidate.Instructions, totalTimeMinutes: candidate.TotalTimeMinutes),
            Reason = "Selected deterministically from a resolved source recipe"
        });
        var freezer = request.AvailableFrozenItems.Select(item => new MealSuggestion
        {
            DishName = item.Name, FreezyItemRef = item.Id, FreezyItemQuantity = 1, AttendeeIds = attendees,
            Reason = "Selected deterministically within the freezer limit"
        });
        var candidates = local.Concat(external).Concat(freezer).ToList();
        var constraints = intent.Constraints;
        var slots = request.DaysToFill.Select(date => new PlanningSlot(date))
            .Concat(constraints.SelectMany(constraint => (constraint.Dates.Count > 0 ? constraint.Dates : request.DaysToFill)
                .Where(request.DaysToFill.Contains).Select(date => new PlanningSlot(date, constraint.MealType, constraint.Course))))
            .Concat(proposed.Select(item => new PlanningSlot(item.Date, item.MealType, item.Course)))
            .Distinct().Where(slot => request.DaysToFill.Contains(slot.Date)).ToList();
        var unknownFacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var choices = new Dictionary<PlanningSlot, List<Option>>();
        foreach (var slot in slots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var required = Enumerable.Range(0, constraints.Count).Where(index => constraints[index].Dates.Contains(slot.Date)
                && constraints[index].MealType == slot.MealType && constraints[index].Course == slot.Course).ToList();
            var flexible = Enumerable.Range(0, constraints.Count).Where(index => constraints[index].Dates.Count == 0
                && constraints[index].MealType == slot.MealType && constraints[index].Course == slot.Course).ToList();
            var slotProposals = proposed.Where(item => item.Date == slot.Date && item.MealType == slot.MealType && item.Course == slot.Course);
            var options = new List<Option>();
            foreach (var candidate in slotProposals.Concat(candidates)
                .OrderByDescending(item => item.ResolvedFactsAssessed)
                .ThenByDescending(item => request.CollectionConstraints.Any(collection =>
                    intent.PreferredCollectionIds.Contains(collection.Id, StringComparer.OrdinalIgnoreCase)
                    && item.RecipeId is { } id && collection.RecipeIds.Contains(id))))
            {
                var suggestion = candidate with
                {
                    Date = slot.Date, MealType = slot.MealType, Course = slot.Course,
                    ConstraintIds = required.Select(index => constraints[index].Id).ToList()
                };
                var errors = PlanningIntentValidator.ValidateFinalItem(suggestion, intent, request).ToList();
                RememberUnknownFacts(errors);
                if (errors.Count > 0) continue;
                var optional = new List<int>();
                foreach (var index in flexible)
                {
                    var issues = PlanningIntentValidator.ValidateFinalItem(suggestion with
                    {
                        ConstraintIds = suggestion.ConstraintIds.Append(constraints[index].Id).ToList()
                    }, intent, request).ToList();
                    RememberUnknownFacts(issues);
                    if (issues.Count == 0) optional.Add(index);
                }
                // A researched recipe may only be introduced for a matching source requirement.
                if (suggestion.SourceUrl is not null && !required.Concat(optional).Any(index => constraints[index].SourceHost is not null))
                    continue;
                options.Add(new(suggestion, required, optional));
            }
            choices[slot] = options.DistinctBy(option => (Identity(option.Suggestion),
                Attendees: string.Join(',', option.Suggestion.AttendeeIds.Order()))).ToList();
        }

        slots = slots.OrderBy(slot => choices[slot].Count).ThenBy(slot => slot.Date)
            .ThenBy(slot => slot.MealType).ThenBy(slot => slot.Course).ToList();
        var counts = new int[constraints.Count];
        var identities = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        var distinct = Enumerable.Range(0, constraints.Count).Select(_ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)).ToArray();
        var stock = new Dictionary<string, int>(StringComparer.Ordinal);
        var selected = new List<MealSuggestion>();
        List<MealSuggestion> best = [];
        var bestScore = -1;
        var visited = 0;
        var freezerUsed = 0;
        var targetScore = constraints.Sum(constraint => constraint.Count) * 100
            + request.DaysToFill.Distinct().Count() * 10;
        Search(0);

        var diagnostics = constraints.Where(constraint => unknownFacts.Contains(constraint.Id)
                && best.Count(item => item.ConstraintIds.Contains(constraint.Id)) < constraint.Count)
            .Select(constraint => new UnmetPlanningRequirement("facts-" + constraint.Id,
                "Some candidates cannot satisfy this requirement until their dietary facts are assessed").WithText("planningIssue.factsNotReady"))
            .ToList();
        return new(best.OrderBy(item => item.Date).ThenBy(item => item.MealType).ThenBy(item => item.Course).ToList(), diagnostics);

        void RememberUnknownFacts(IEnumerable<UnmetPlanningRequirement> issues)
        {
            foreach (var issue in issues.Where(issue => issue.DisplayText?.Key == "planningIssue.factsNotCurrent"))
                unknownFacts.Add(issue.Id);
        }

        void Search(int position)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visited > MaxSearchNodes || bestScore == targetScore) return;
            var score = counts.Sum() * 100 + selected.Where(item => item.MealType == MealType.Dinner && item.Course == Course.Main)
                .Select(item => item.Date).Distinct().Count() * 10;
            if (score > bestScore) { bestScore = score; best = selected.ToList(); }
            if (position == slots.Count) return;
            foreach (var option in choices[slots[position]])
            {
                var item = option.Suggestion;
                var claims = option.Required.Concat(option.Optional.Where(index => counts[index] < constraints[index].Count)).ToList();
                if (claims.Any(index => counts[index] >= constraints[index].Count)) continue;
                var identity = Identity(item);
                if (!intent.AllowRepeatedDishes && identities.TryGetValue(identity, out var firstDate) && firstDate != item.Date) continue;
                if (claims.Any(index => constraints[index].Distinct && distinct[index].Contains(identity))) continue;
                if (item.FreezyItemRef is { } freezerId)
                {
                    var available = request.AvailableFrozenItems.FirstOrDefault(value => value.Id == freezerId)?.Quantity ?? 0;
                    if (freezerUsed >= (intent.MaxFreezerMeals ?? int.MaxValue) || stock.GetValueOrDefault(freezerId) >= available) continue;
                    freezerUsed++;
                    stock[freezerId] = stock.GetValueOrDefault(freezerId) + 1;
                }
                var hadIdentity = identities.ContainsKey(identity);
                identities.TryAdd(identity, item.Date);
                foreach (var index in claims) { counts[index]++; distinct[index].Add(identity); }
                selected.Add(item with { ConstraintIds = claims.Select(index => constraints[index].Id).ToList() });
                Search(position + 1);
                selected.RemoveAt(selected.Count - 1);
                foreach (var index in claims)
                {
                    counts[index]--;
                    if (!selected.Any(value => Identity(value) == identity && value.ConstraintIds.Contains(constraints[index].Id))) distinct[index].Remove(identity);
                }
                if (!hadIdentity) identities.Remove(identity);
                if (item.FreezyItemRef is { } usedId) { freezerUsed--; stock[usedId]--; }
                if (bestScore == targetScore || visited > MaxSearchNodes) return;
            }
            Search(position + 1); // A visible gap is preferable to a violating candidate.
        }
    }

    private static string Identity(MealSuggestion item) => item.RecipeId is { } id ? $"recipe:{id}"
        : item.SourceUrl is not null ? $"url:{item.SourceUrl.Trim()}"
        : item.FreezyItemRef is not null ? $"freezer:{item.FreezyItemRef}"
        : $"dish:{item.DishName?.Trim()}";
}
