using Dishhive.Api.Models;
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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal const string SystemPrompt =
        """
        Interpret meal-planning instructions written in any language. Do not plan meals.
        Convert the instructions into a language-neutral JSON intent. Use only exact dates
        from the supplied week mapping and only source hosts from the supplied source list.

        Reply with ONLY this JSON shape:
        {
          "version": 1,
          "allowRepeatedDishes": false,
          "constraints": [{
            "id": "constraint-1",
            "dates": ["yyyy-MM-dd"],
            "mealType": "dinner",
            "course": "main",
            "sourceHost": null,
            "count": 1,
            "distinct": true,
            "searchQuery": null,
            "dishRequest": null,
            "requiredClasses": [],
            "excludedClasses": [],
            "allAttendees": true,
            "overrideDietPreferences": false
          }]
        }

        Rules:
        - Create constraints only for explicit requirements, not general preferences.
        - Default to no repeated dishes. Set allowRepeatedDishes=true only when repetition
          is explicitly requested.
        - Resolve weekday names in any language through the supplied date mapping.
        - A duration/count such as four days means four distinct dated selections.
        - Valid mealType values are breakfast, lunch, dinner, snack. A dessert is a
          course, not a meal type; unless explicitly requested otherwise use dinner.
        - Valid course values are main, appetizer, side, dessert.
        - Keep one logical counted requirement as one constraint: two dated desserts
          means count=2 with both dates, not two unrelated count=1 constraints.
        - Keep a referenced source only on the clause that actually references it.
        - searchQuery and dishRequest preserve the user's food meaning, translated into
          useful search terms when necessary; deterministic code treats them as opaque text.
        - Every constraint with sourceHost MUST have a concise, non-generic searchQuery.
          Include the requested dish, dietary style or course using terms likely to occur
          on that source, in the source website's likely language rather than automatically
          copying the instruction language. Use only discriminating food terms: omit generic
          words meaning "recipe" in any language, because they match nearly every source page.
        - Map explicit food semantics to canonical classes when possible. Examples:
          chicken -> requiredClasses ["Poultry"]; vegetarian -> excludedClasses
          ["RedMeat","Poultry","Pork","Fish","Crustaceans","Molluscs","Gelatin"].
        - When the user explicitly requests a dish that conflicts with a diet preference,
          keep it for all attendees and set overrideDietPreferences=true. Allergies are never
          overridden.
        - Valid classes: Gluten, Crustaceans, Eggs, Fish, Peanuts, Soybeans, Milk,
          TreeNuts, Celery, Mustard, Sesame, Sulphites, Lupin, Molluscs, RedMeat,
          Poultry, Pork, Gelatin, Alcohol, Honey.
        """;

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
            payload = JsonSerializer.Deserialize<IntentPayload>(text[start..(end + 1)], JsonOptions);
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
                || item.Count is < 1 or > 6
                || !Enum.TryParse<MealType>(item.MealType ?? "dinner", true, out var mealType)
                || !Enum.IsDefined(mealType)
                || !Enum.TryParse<Course>(item.Course ?? "main", true, out var course)
                || !Enum.IsDefined(course))
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
                MealType = mealType,
                Course = course,
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

    public static string FormatForModel(PlanningIntent intent) => JsonSerializer.Serialize(new
    {
        version = intent.Version,
        allowRepeatedDishes = intent.AllowRepeatedDishes,
        constraints = intent.Constraints.Select(item => new
        {
            id = item.Id,
            dates = item.Dates.Select(date => date.ToString("yyyy-MM-dd")),
            mealType = item.MealType.ToString().ToLowerInvariant(),
            course = item.Course.ToString().ToLowerInvariant(),
            sourceHost = item.SourceHost,
            count = item.Count,
            distinct = item.Distinct,
            searchQuery = item.SearchQuery,
            dishRequest = item.DishRequest,
            requiredClasses = IngredientClasses.ToNames(item.RequiredClasses),
            excludedClasses = IngredientClasses.ToNames(item.ExcludedClasses),
            allAttendees = item.AllAttendees,
            overrideDietPreferences = item.OverrideDietPreferences
        })
    });

    private static bool TryParseClasses(IEnumerable<string>? names, out List<IngredientClass> classes)
    {
        classes = [];
        foreach (var name in names ?? [])
        {
            if (!IngredientClasses.TryParse(name, out var value)) return false;
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

    private sealed record IntentPayload(
        int Version,
        bool AllowRepeatedDishes,
        List<ConstraintPayload>? Constraints);

    private sealed record ConstraintPayload(
        string? Id,
        List<string>? Dates,
        string? MealType,
        string? Course,
        string? SourceHost,
        int Count,
        bool Distinct,
        string? SearchQuery,
        string? DishRequest,
        List<string>? RequiredClasses,
        List<string>? ExcludedClasses,
        bool AllAttendees = true,
        bool OverrideDietPreferences = false);
}
