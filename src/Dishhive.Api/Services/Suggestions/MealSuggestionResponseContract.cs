using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Single owner of the model response DTOs, JSON schema, tolerant parser,
/// and corrective-reprompt quoting.</summary>
public static partial class MealSuggestionResponseContract
{
    public const string ExampleJson =
        "{\"suggestions\":[{\"date\":\"yyyy-MM-dd\",\"mealType\":\"dinner\",\"course\":\"main\",\"attendeeIds\":[],\"dishName\":\"...\",\"recipeTitle\":null,\"freezerItemId\":null,\"externalCandidateId\":null,\"reason\":\"...\"}]}";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const int MaxRepromptQuoteChars = 2000;

    public static ChatResponseFormat JsonSchemaFormat { get; } = ChatResponseFormat.ForJsonSchema(
        JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "suggestions": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "date": { "type": "string", "description": "yyyy-MM-dd" },
                      "mealType": { "type": "string", "enum": ["breakfast", "lunch", "dinner", "snack"] },
                      "course": { "type": "string", "enum": ["main", "appetizer", "side", "dessert"] },
                      "attendeeIds": {
                        "type": "array",
                        "items": { "type": "string" },
                        "description": "empty means every household member; otherwise exact ids from the household list"
                      },
                      "dishName": { "type": "string" },
                      "recipeTitle": { "type": ["string", "null"] },
                      "freezerItemId": { "type": ["string", "null"], "description": "exact id from the freezer items list, or null" },
                      "externalCandidateId": { "type": ["string", "null"], "description": "candidateId from a successful get_recipe call, or null" },
                      "reason": { "type": "string" }
                    },
                    "required": ["date", "mealType", "course", "attendeeIds", "dishName", "recipeTitle", "freezerItemId", "externalCandidateId", "reason"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["suggestions"],
              "additionalProperties": false
            }
            """).RootElement,
        schemaName: "week_suggestions",
        schemaDescription: "A dinner suggestion per requested date");

    [GeneratedRegex(@"<(?:think|reasoning)>.*?(?:</(?:think|reasoning)>|$)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ReasoningBlockRegex();

    public static string BuildRepromptQuote(string text)
    {
        var stripped = ReasoningBlockRegex().Replace(text, "").Trim();
        if (stripped.Length == 0)
        {
            return "(your reply contained only reasoning, no visible answer)";
        }

        return stripped.Length > MaxRepromptQuoteChars
            ? stripped[^MaxRepromptQuoteChars..]
            : stripped;
    }

    public static WeekSuggestionsPayload? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = ReasoningBlockRegex().Replace(text, "");
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<WeekSuggestionsPayload>(
                    text[start..(end + 1)], JsonOptions);
                if (payload?.Suggestions is not null)
                {
                    return payload;
                }
            }
            catch (JsonException)
            {
                // Fall through to the tolerated bare-array form.
            }
        }

        var arrayStart = text.IndexOf('[');
        var arrayEnd = text.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            try
            {
                var items = JsonSerializer.Deserialize<List<DaySuggestionPayload>>(
                    text[arrayStart..(arrayEnd + 1)], JsonOptions);
                return items == null ? null : new WeekSuggestionsPayload(items);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }
}

public sealed record WeekSuggestionsPayload(List<DaySuggestionPayload>? Suggestions);

public sealed record DaySuggestionPayload(
    string? Date,
    string? DishName,
    string? RecipeTitle,
    string? Reason,
    string? ExternalCandidateId,
    string? FreezerItemId = null,
    string? MealType = null,
    string? Course = null,
    IReadOnlyList<string>? AttendeeIds = null);
