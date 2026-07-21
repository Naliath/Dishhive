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
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public bool AllowRepeatedDishes { get; init; }
    public IReadOnlyList<PlanningConstraint> Constraints { get; init; } = [];
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
    public string? DishRequest { get; init; }
    public IReadOnlyList<IngredientClass> RequiredClasses { get; init; } = [];
    public IReadOnlyList<IngredientClass> ExcludedClasses { get; init; } = [];
    public bool AllAttendees { get; init; } = true;
    public bool OverrideDietPreferences { get; init; }
}

internal static class PlanningIntentContract
{
    internal static string SystemPrompt { get; } = BuildSystemPrompt();

    private static string BuildSystemPrompt()
    {
        var example = AiJsonContract.Serialize(new IntentPayload
        {
            Version = PlanningIntent.CurrentVersion,
            AllowRepeatedDishes = false,
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
                    OverrideDietPreferences = false
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
        var mealTypes = AiJsonContract.EnumValuesJson<MealType>();
        var courses = AiJsonContract.EnumValuesJson<Course>();
        var classes = AiJsonContract.EnumValuesJson<IngredientClass>();
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
        from the supplied week mapping and only source hosts from the supplied source list.

        Reply with ONLY this JSON shape:
        {{example}}

        Rules:
        - Create constraints only for explicit requirements, not general preferences.
        - Default to no repeated dishes. Set {{allowRepeatedDishesField}}=true only when repetition
          is explicitly requested.
        - Resolve weekday names in any language through the supplied date mapping.
        - A duration/count such as four days means four distinct dated selections.
        - Valid {{mealTypeField}} values are {{mealTypes}}. A dessert is a
          course, not a meal type; unless explicitly requested otherwise use dinner.
        - Valid {{courseField}} values are {{courses}}.
        - Keep one logical counted requirement as one constraint: two dated desserts
          means count=2 with both dates, not two unrelated count=1 constraints.
        - Keep a referenced source only on the clause that actually references it.
        - {{searchQueryField}} and {{dishRequestField}} preserve the user's food meaning, translated into
          useful search terms when necessary; deterministic code treats them as opaque text.
        - Every constraint with {{sourceHostField}} MUST have a concise, non-generic {{searchQueryField}}.
          Include the requested dish, dietary style or course using terms likely to occur
          on that source, in the source website's likely language rather than automatically
          copying the instruction language. Use only discriminating food terms: omit generic
          words meaning "recipe" in any language, because they match nearly every source page.
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

    public static PlanningIntent? Parse(string? text, MealSuggestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        IntentPayload? payload;
        try
        {
            payload = AiJsonContract.Deserialize<IntentPayload>(text[start..(end + 1)]);
        }
        catch (JsonException)
        {
            return null;
        }
        if (payload?.Version != PlanningIntent.CurrentVersion || payload.Constraints is null)
        {
            return null;
        }

        var weekDates = Enumerable.Range(0, 7).Select(request.WeekStart.AddDays).ToHashSet();
        var allowedHosts = request.SourceConstraints.Select(source => NormalizeHost(source.Host))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var constraints = new List<PlanningConstraint>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in payload.Constraints.Take(12))
        {
            var id = item.Id?.Trim();
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)
                || item.Count is < 1 or > 6)
            {
                return null;
            }
            var dates = new List<DateOnly>();
            foreach (var value in item.Dates ?? [])
            {
                if (!DateOnly.TryParse(value, out var date) || !weekDates.Contains(date))
                {
                    return null;
                }
                if (!dates.Contains(date)) dates.Add(date);
            }
            var host = string.IsNullOrWhiteSpace(item.SourceHost) ? null : NormalizeHost(item.SourceHost);
            if (host != null && !allowedHosts.Contains(host))
            {
                return null;
            }
            if (!TryParseClasses(item.RequiredClasses, out var required)
                || !TryParseClasses(item.ExcludedClasses, out var excluded))
            {
                return null;
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
                        .Contains(requiredClass)))
            });
        }

        // Every explicit @[Source] must survive interpretation. Silently omitting one
        // would be worse than declaring the intent unparseable and falling back visibly.
        if (allowedHosts.Any(host => constraints.All(item =>
                !string.Equals(item.SourceHost, host, StringComparison.OrdinalIgnoreCase))))
        {
            return null;
        }

        return new PlanningIntent
        {
            AllowRepeatedDishes = payload.AllowRepeatedDishes,
            Constraints = constraints
        };
    }

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

    private sealed record IntentPayload
    {
        public int Version { get; init; }
        public bool AllowRepeatedDishes { get; init; }
        public List<ConstraintPayload> Constraints { get; init; } = [];
    }

    private sealed record ConstraintPayload
    {
        public string Id { get; init; } = "";
        public List<string> Dates { get; init; } = [];
        public MealType MealType { get; init; } = MealType.Dinner;
        public Course Course { get; init; } = Course.Main;
        public string? SourceHost { get; init; }
        [Range(1, 6)]
        public int Count { get; init; } = 1;
        public bool Distinct { get; init; } = true;
        public string? SearchQuery { get; init; }
        public string? DishRequest { get; init; }
        public List<IngredientClass> RequiredClasses { get; init; } = [];
        public List<IngredientClass> ExcludedClasses { get; init; } = [];
        public bool AllAttendees { get; init; } = true;
        public bool OverrideDietPreferences { get; init; }
    }
}
