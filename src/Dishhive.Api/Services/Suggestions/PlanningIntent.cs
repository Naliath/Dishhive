using Dishhive.Api.Models;
using Dishhive.Api.Services.Ai;
using Microsoft.Extensions.AI;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// Language-neutral interpretation of the user's free-form planning instructions.
/// This is the only boundary allowed to translate natural language into deterministic
/// constraints; downstream code must not inspect <see cref="MealSuggestionRequest.Instructions"/>.
/// </summary>
public sealed record PlanningIntent
{
    public const int CurrentVersion = 11;
    public int Version { get; init; } = CurrentVersion;
    public bool AllowRepeatedDishes { get; init; }
    public int? MaxFreezerMeals { get; init; }
    public IReadOnlyList<string> PreferredCollectionIds { get; init; } = [];
    public IReadOnlyList<PlanningConstraint> Constraints { get; init; } = [];
}

public enum PlanningOrigin
{
    LocalRecipe,
    ExternalRecipe,
    Freezer,
    UnlinkedDish
}

public sealed record PlanningConstraint
{
    public required string Id { get; init; }
    public IReadOnlyList<DateOnly> Dates { get; init; } = [];
    public MealType MealType { get; init; } = MealType.Dinner;
    public Course Course { get; init; } = Course.Main;
    public string? SourceHost { get; init; }
    public int Count { get; init; } = 1;
    public bool Distinct { get; init; } = true;
    public string? SearchQuery { get; init; }
    public IReadOnlyList<string> SearchQueries { get; init; } = [];
    public string? DishRequest { get; init; }
    public IReadOnlyList<IngredientClass> RequiredClasses { get; init; } = [];
    public IReadOnlyList<IngredientClass> ExcludedClasses { get; init; } = [];
    public bool AllAttendees { get; init; } = true;
    public bool OverrideDietPreferences { get; init; }
    public IReadOnlyList<PlanningOrigin> AllowedOrigins { get; init; } =
        [PlanningOrigin.LocalRecipe, PlanningOrigin.ExternalRecipe, PlanningOrigin.Freezer, PlanningOrigin.UnlinkedDish];
    public int? MaxTotalTimeMinutes { get; init; }
    public IReadOnlyList<Guid> RequiredRecipeIds { get; init; } = [];
    public IReadOnlyList<string> RequiredCollectionIds { get; init; } = [];
    public bool RequiresDishMatch { get; init; }
    public IReadOnlyList<string> VerifiedDishVersions { get; init; } = [];
}

internal static class PlanningIntentContract
{
    internal const string ContractVersion = "planning-intent-v11";
    internal static string SystemPrompt { get; } = BuildSystemPrompt();

    private static string BuildSystemPrompt()
    {
        var example = AiJsonContract.Serialize(new IntentPayload
        {
            Version = PlanningIntent.CurrentVersion,
            AllowRepeatedDishes = false,
            MaxFreezerMeals = null,
            PreferredCollectionIds = [],
            Constraints =
            [
                new ConstraintPayload
                {
                    Id = "constraint-1",
                    Dates = ["yyyy-MM-dd"],
                    MealType = MealType.Dinner,
                    Course = Course.Main,
                    SourceHost = null,
                    Count = 1,
                    Distinct = true,
                    SearchQuery = null,
                    DishRequest = null,
                    RequiredClasses = [],
                    ExcludedClasses = [],
                    AllAttendees = true,
                    OverrideDietPreferences = false,
                    AllowedOrigins = [PlanningOrigin.LocalRecipe, PlanningOrigin.ExternalRecipe,
                        PlanningOrigin.Freezer, PlanningOrigin.UnlinkedDish],
                    MaxTotalTimeMinutes = null,
                    RequiredCollectionIds = []
                }
            ]
        });
        var allowRepeatedDishesField = AiJsonContract.PropertyName<IntentPayload>(
            nameof(IntentPayload.AllowRepeatedDishes));
        var mealTypeField = AiJsonContract.PropertyName<ConstraintPayload>(
            nameof(ConstraintPayload.MealType));
        var courseField = AiJsonContract.PropertyName<ConstraintPayload>(
            nameof(ConstraintPayload.Course));
        var sourceHostField = AiJsonContract.PropertyName<ConstraintPayload>(
            nameof(ConstraintPayload.SourceHost));
        var searchQueryField = AiJsonContract.PropertyName<ConstraintPayload>(
            nameof(ConstraintPayload.SearchQuery));
        var searchQueriesField = AiJsonContract.PropertyName<ConstraintPayload>(nameof(ConstraintPayload.SearchQueries));
        var dishRequestField = AiJsonContract.PropertyName<ConstraintPayload>(
            nameof(ConstraintPayload.DishRequest));
        var requiredClassesField = AiJsonContract.PropertyName<ConstraintPayload>(
            nameof(ConstraintPayload.RequiredClasses));
        var excludedClassesField = AiJsonContract.PropertyName<ConstraintPayload>(
            nameof(ConstraintPayload.ExcludedClasses));
        var allAttendeesField = AiJsonContract.PropertyName<ConstraintPayload>(
            nameof(ConstraintPayload.AllAttendees));
        var overrideDietPreferencesField = AiJsonContract.PropertyName<ConstraintPayload>(
            nameof(ConstraintPayload.OverrideDietPreferences));
        var allowedOriginsField = AiJsonContract.PropertyName<ConstraintPayload>(
            nameof(ConstraintPayload.AllowedOrigins));
        var maxTotalTimeField = AiJsonContract.PropertyName<ConstraintPayload>(
            nameof(ConstraintPayload.MaxTotalTimeMinutes));
        var maxFreezerMealsField = AiJsonContract.PropertyName<IntentPayload>(
            nameof(IntentPayload.MaxFreezerMeals));
        var collectionsField = AiJsonContract.PropertyName<ConstraintPayload>(nameof(ConstraintPayload.RequiredCollectionIds));
        var preferredCollectionsField = AiJsonContract.PropertyName<IntentPayload>(nameof(IntentPayload.PreferredCollectionIds));
        var mealTypes = AiJsonContract.EnumValuesJson<MealType>();
        var courses = AiJsonContract.EnumValuesJson<Course>();
        var classes = AiJsonContract.EnumValuesJson<IngredientClass>();
        var origins = AiJsonContract.EnumValuesJson<PlanningOrigin>();
        var chickenClasses = AiJsonContract.Serialize(new[] { IngredientClass.Poultry });
        var vegetarianClasses = AiJsonContract.Serialize(new[]
        {
            IngredientClass.RedMeat,
            IngredientClass.Poultry,
            IngredientClass.Pork,
            IngredientClass.Fish,
            IngredientClass.Crustaceans,
            IngredientClass.Molluscs,
            IngredientClass.Gelatin
        });

        return $$"""
        Interpret meal-planning instructions written in any language. Do not plan meals.
        Convert the instructions into a language-neutral JSON intent. Use only exact dates
        from the supplied selected-date list (the week mapping is for weekday translation)
        and only source hosts from the supplied source list.

        Reply with ONLY this JSON shape:
        {{example}}

        Rules:
        - Create constraints only for explicit requirements, not general preferences.
        - Preserve every hard requirement even when the request is impossible. Never
          resolve a contradiction by dropping a clause. If the same dish must contain
          and exclude a class, retain that class in BOTH {{requiredClassesField}} and {{excludedClassesField}}
          so the application can report the conflict to the planner.
        - Default to no repeated dishes. Set {{allowRepeatedDishesField}}=true only when repetition
          is explicitly requested.
        - Resolve weekday names in any language through the supplied date mapping.
        - The selected-date list is authoritative. Never emit a date that is not selected.
        - A duration/count such as four days means four distinct dated selections. When
          the user does not name dates, first reserve explicitly dated requirements for
          the same meal and course, then assign the counted selections in selected-date
          order to compatible remaining dates. Do not create a contradiction by assigning
          an undated requirement to a date reserved for an incompatible explicit dish.
          Preserve contradictions that the user explicitly requested on the same date.
        - Valid {{mealTypeField}} values are {{mealTypes}}. A dessert is a
          course, not a meal type; unless explicitly requested otherwise use dinner.
        - Valid {{courseField}} values are {{courses}}.
        - Keep one logical counted requirement as one constraint: two dated desserts
          means count=2 with both dates, not two unrelated count=1 constraints.
          Each date is a required selection, not an optional scheduling window. A
          nonempty date list MUST contain exactly count distinct dates. Never list
          all seven selected dates for a requirement that requests only four meals.
        - Keep a referenced source only on the clause that actually references it.
        - Preserve mandatory collection membership in {{collectionsField}}, using only
          the stable collection ids supplied in ReferencedCollections. Never use a
          collection name or a recipe title as an id. Preserve counts, dates and course.
          A recipe must belong to EVERY required collection on its constraint.
        - Use {{preferredCollectionsField}} only for explicitly soft collection preferences
          such as "prefer" or "if possible". Every globally referenced collection must
          appear either on a requirement or in this preference list, even when empty.
          Dated collection references constrain their exact meal and course.
        - Use {{allowedOriginsField}} to preserve explicit origin restrictions. "Saved in
          Dishhive" or "my recipe store" means ["LocalRecipe"], never a source host.
          Valid origins are {{origins}}. A referenced website requires ExternalRecipe.
        - Put an explicit cooking-time ceiling in {{maxTotalTimeField}}. Put a whole-plan
          freezer-meal cap in {{maxFreezerMealsField}}. Leave them null when absent.
        - {{searchQueryField}} and {{dishRequestField}} preserve the user's food meaning, translated into
          useful search terms when necessary; deterministic code treats them as opaque text.
          {{dishRequestField}} MUST preserve an explicitly named dish or food family even
          when a website is specified; use null only when no particular food is requested.
          Preserve its meaning across spelling errors and language variants. Never replace
          a named dish with an unrelated dietary alternative. Keep it at most 512 characters.
        - Every constraint with {{sourceHostField}} MUST have a concise, non-generic {{searchQueryField}}.
          Include the requested dish, dietary style or course using terms likely to occur
          on that source, in the source website's likely language rather than automatically
          copying the instruction language. Use only discriminating food terms: omit generic
          words meaning "recipe" in any language, because they match nearly every source page.
        - Supply up to three genuinely different fallback searches in {{searchQueriesField}},
          or an empty list when no research is needed. Keep each query at most 256 characters.
          Use the source's likely language and concrete dish families or staple ingredients,
          rather than repeating generic course labels with slightly different wording.
          For a broad request, vary the actual food: for example legumes, rice, soup or a
          vegetable stew where appropriate to the user's requirements. Account for household
          exclusions by choosing suitable food concepts. Combine the requested dietary style
          with HouseholdAllergyExclusions when choosing search terms: vegetarian dishes for
          a milk-allergic household need dairy-free or vegan discovery, rather than ordinary
          cheese-based vegetarian dishes. This changes retrieval wording only, never the
          requested requirements or the ingredient checks. Prefer short staple terms over
          invented compound dish names that the source may not use. Do not repeat literal allergen names
          in every query: ingredient facts enforce exclusions after retrieval. Preserve any
          specifically requested dish and required food classes. Never include household names
          or preference notes. Search wording itself is never proof of dietary suitability.
          For a named food, vary authentic regional preparations and alternate cultural
          names for that SAME food. Do not merely repeat the dish with different milk
          substitutes or invent an adapted recipe that the source may not publish.
          Prefer variants whose usual ingredients naturally fit the supplied exclusions;
          do not add exclusions that the household did not request.
        - Map explicit food semantics to canonical classes when possible. Examples:
          chicken -> {{requiredClassesField}} {{chickenClasses}}; vegetarian ->
          {{excludedClassesField}} {{vegetarianClasses}}.
        - When the user explicitly requests a dish that conflicts with a diet preference,
          keep it for {{allAttendeesField}} and set {{overrideDietPreferencesField}}=true. Allergies are never
          overridden.
        - Valid classes: {{classes}}.
        """;
    }

    internal static ChatResponseFormat JsonSchemaFormat { get; } =
        AiJsonContract.CreateResponseFormat<IntentPayload>("planning_intent");

    internal sealed record ParseResult(PlanningIntent? Intent, IReadOnlyList<UnmetPlanningRequirement> Errors);

    public static PlanningIntent? Parse(string? text, MealSuggestionRequest request) => ParseWithErrors(text, request).Intent;

    public static ParseResult ParseWithErrors(string? text, MealSuggestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Invalid("json", "A JSON planning intent is required");
        }
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return Invalid("json", "A JSON object is required");
        }

        IntentPayload? payload;
        try
        {
            payload = AiJsonContract.Deserialize<IntentPayload>(text[start..(end + 1)]);
        }
        catch (JsonException)
        {
            return Invalid("json", "The JSON shape or an enum value is invalid");
        }
        // Older persisted/evaluation intents remain readable.
        if (payload is null || payload.Version is not (2 or 3 or 7 or 8 or 9 or 10 or PlanningIntent.CurrentVersion)
            || payload.Constraints is null)
        {
            return Invalid("version", $"Expected planning intent version {PlanningIntent.CurrentVersion} and constraints");
        }

        if (payload.MaxFreezerMeals is < 0 or > 7) return Invalid("maxFreezerMeals", "The freezer cap must be between 0 and 7");
        if (payload.PreferredCollectionIds is null || payload.Constraints.Any(item =>
                item is null || item.RequiredCollectionIds is null || item.AllowedOrigins is null))
            return Invalid("constraints", "Collection and origin arrays cannot be null");
        if (payload.Constraints.Count > 24) return Invalid("constraints", "At most 24 constraints are supported");
        var selectedDates = request.DaysToFill.ToHashSet();
        var allowedHosts = request.SourceConstraints.Select(source => NormalizeHost(source.Host))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var constraints = new List<PlanningConstraint>();
        var collectionIds = request.CollectionConstraints.Select(collection => collection.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (payload.PreferredCollectionIds.Any(id => !collectionIds.Contains(id)))
            return Invalid("preferredCollectionIds", "Use only ids from ReferencedCollections");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in payload.Constraints)
        {
            var id = item.Id?.Trim();
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)
                || item.Count is < 1 or > 7)
            {
                return Invalid("constraints", "Every constraint needs a unique nonempty id and a count between 1 and 7");
            }
            var dates = new List<DateOnly>();
            foreach (var value in item.Dates ?? [])
            {
                if (!DateOnly.TryParse(value, out var date) || !selectedDates.Contains(date))
                {
                    return Invalid(id + ".dates", "Use only dates from SelectedDates");
                }
                if (!dates.Contains(date)) dates.Add(date);
            }
            if (dates.Count > 0 && dates.Count != item.Count)
                return Invalid(id + ".dates", "Each listed date requires one selection. Supply exactly count distinct selected dates, not a broader scheduling window; preserve the original requested count and explicit dates.");
            var host = string.IsNullOrWhiteSpace(item.SourceHost) ? null : NormalizeHost(item.SourceHost);
            if (host != null && !allowedHosts.Contains(host))
            {
                return Invalid(id + ".sourceHost", "Use only hosts from ReferencedSources; local recipes use allowedOrigins");
            }
            if (host is not null && string.IsNullOrWhiteSpace(item.SearchQuery))
                return Invalid(id + ".searchQuery", "A source requirement needs a nonempty, specific search query");
            if (item.SearchQueries is null || item.SearchQueries.Count > 3
                || item.SearchQueries.Any(query => string.IsNullOrWhiteSpace(query) || query.Length > 256)
                || item.SearchQuery?.Length > 256)
                return Invalid(id + ".searchQueries", "Use at most three nonempty fallback searches of at most 256 characters");
            if (item.DishRequest?.Length > 512)
                return Invalid(id + ".dishRequest", "Keep the requested food meaning within 512 characters");
            if (item.RequiredCollectionIds.Any(collectionId => !collectionIds.Contains(collectionId)))
                return Invalid(id + ".requiredCollectionIds", "Use only ids from ReferencedCollections");
            if (!TryParseClasses(item.RequiredClasses, out var required)
                || !TryParseClasses(item.ExcludedClasses, out var excluded))
            {
                return Invalid(id + ".classes", "Use only canonical ingredient classes");
            }
            var allowedOrigins = item.AllowedOrigins.Count == 0
                ? Enum.GetValues<PlanningOrigin>().ToList()
                : item.AllowedOrigins;
            if (allowedOrigins.Any(origin => !Enum.IsDefined(origin))
                || item.MaxTotalTimeMinutes is < 1 or > 1440)
            {
                return Invalid(id + ".limits", "Origins must be valid; a cooking-time limit must be between 1 and 1440 minutes");
            }
            constraints.Add(new PlanningConstraint
            {
                Id = id,
                Dates = dates,
                MealType = item.MealType,
                Course = item.Course,
                SourceHost = host,
                Count = item.Count,
                Distinct = item.Distinct,
                SearchQuery = NullIfBlank(item.SearchQuery),
                SearchQueries = item.SearchQueries.Select(query => query.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                DishRequest = NullIfBlank(item.DishRequest),
                RequiredClasses = required,
                ExcludedClasses = excluded,
                AllAttendees = item.AllAttendees,
                // Canonical classes let code safely recognize the structural case without
                // inspecting chicken/kip/poulet or any other language-specific words. An
                // explicitly required food class for everyone takes precedence over a diet
                // preference it conflicts with; allergies remain non-overridable elsewhere.
                OverrideDietPreferences = item.OverrideDietPreferences
                    || (item.AllAttendees && required.Any(requiredClass => request.Members
                        .SelectMany(member => member.Diets)
                        .SelectMany(diet => diet.ExcludedClasses)
                        .Contains(requiredClass))),
                AllowedOrigins = allowedOrigins.Distinct().ToList(),
                MaxTotalTimeMinutes = item.MaxTotalTimeMinutes,
                RequiredRecipeIds = host is null ? ResolveRequestedRecipes(item.DishRequest, request) : [],
                RequiresDishMatch = host is not null || !string.IsNullOrWhiteSpace(item.DishRequest),
                RequiredCollectionIds = item.RequiredCollectionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            });
        }

        // Every explicit @[Source] must survive interpretation. Silently omitting one
        // would be worse than declaring the intent unparseable and falling back visibly.
        if (allowedHosts.Any(host => constraints.All(item =>
                !string.Equals(item.SourceHost, host, StringComparison.OrdinalIgnoreCase))))
        {
            return Invalid("sources", "Every referenced source must be preserved in a constraint");
        }

        foreach (var collection in request.CollectionConstraints.Where(collection => collectionIds.Contains(collection.Id)))
        {
            if ((collection.IsGlobal || (collection.Dates.Count == 0 && collection.Slots.Count == 0))
                && !payload.PreferredCollectionIds.Contains(collection.Id, StringComparer.OrdinalIgnoreCase)
                && constraints.All(constraint => !constraint.RequiredCollectionIds.Contains(collection.Id, StringComparer.OrdinalIgnoreCase)))
                return Invalid("collections", $"Preserve referenced collection id {collection.Id} as a requirement or explicit preference");

            var slots = collection.Slots.Count > 0 ? collection.Slots
                : collection.Dates.Select(date => new PlanningSlot(date)).ToList();
            foreach (var slot in slots.Where(slot => selectedDates.Contains(slot.Date)))
            {
                var matching = constraints.Where(constraint => constraint.Dates.Contains(slot.Date)
                    && constraint.MealType == slot.MealType && constraint.Course == slot.Course).ToList();
                if (matching.Count == 0)
                    constraints.Add(new PlanningConstraint
                    {
                        Id = $"collection-{collection.Id}-{slot.Date:yyyy-MM-dd}-{slot.MealType}-{slot.Course}",
                        Dates = [slot.Date], MealType = slot.MealType, Course = slot.Course,
                        RequiredCollectionIds = [collection.Id], AllowedOrigins = [PlanningOrigin.LocalRecipe]
                    });
                else foreach (var constraint in matching)
                    constraints[constraints.IndexOf(constraint)] = constraint with
                    {
                        RequiredCollectionIds = constraint.RequiredCollectionIds.Append(collection.Id)
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    };
            }
        }

        return new(new PlanningIntent
        {
            AllowRepeatedDishes = payload.AllowRepeatedDishes,
            MaxFreezerMeals = payload.MaxFreezerMeals,
            PreferredCollectionIds = payload.PreferredCollectionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Constraints = constraints
        }, []);
    }

    private static ParseResult Invalid(string id, string message) => new(null,
        [new(id, message[..Math.Min(message.Length, 512)])]);

    public static string FormatForModel(PlanningIntent intent) =>
        AiJsonContract.Serialize(intent);

    private static bool TryParseClasses(
        IEnumerable<IngredientClass>? values,
        out List<IngredientClass> classes)
    {
        classes = [];
        foreach (var value in values ?? [])
        {
            if (!Enum.IsDefined(value)) return false;
            if (!classes.Contains(value)) classes.Add(value);
        }
        return true;
    }

    private static string NormalizeHost(string host)
    {
        var normalized = host.Trim().ToLowerInvariant();
        return normalized.StartsWith("www.", StringComparison.Ordinal) ? normalized[4..] : normalized;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<Guid> ResolveRequestedRecipes(
        string? dishRequest,
        MealSuggestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(dishRequest)) return [];
        var requested = dishRequest.Trim();
        return request.KnownRecipes.Where(recipe =>
                string.Equals(recipe.Title, requested, StringComparison.OrdinalIgnoreCase)
                || string.Equals(recipe.OriginalTitle, requested, StringComparison.OrdinalIgnoreCase))
            .Select(recipe => recipe.Id)
            .Distinct()
            .ToList();
    }

    private sealed record IntentPayload
    {
        public int Version { get; init; }
        public bool AllowRepeatedDishes { get; init; }
        [Range(0, 7)]
        public int? MaxFreezerMeals { get; init; }
        public List<string> PreferredCollectionIds { get; init; } = [];
        public List<ConstraintPayload> Constraints { get; init; } = [];
    }

    private sealed record ConstraintPayload
    {
        public string Id { get; init; } = "";
        public List<string> Dates { get; init; } = [];
        public MealType MealType { get; init; } = MealType.Dinner;
        public Course Course { get; init; } = Course.Main;
        public string? SourceHost { get; init; }
        [Range(1, 7)]
        public int Count { get; init; } = 1;
        public bool Distinct { get; init; } = true;
        public string? SearchQuery { get; init; }
        public List<string> SearchQueries { get; init; } = [];
        public string? DishRequest { get; init; }
        public List<IngredientClass> RequiredClasses { get; init; } = [];
        public List<IngredientClass> ExcludedClasses { get; init; } = [];
        public bool AllAttendees { get; init; } = true;
        public bool OverrideDietPreferences { get; init; }
        public List<PlanningOrigin> AllowedOrigins { get; init; } = [];
        [Range(1, 1440)]
        public int? MaxTotalTimeMinutes { get; init; }
        public List<string> RequiredCollectionIds { get; init; } = [];
    }
}
