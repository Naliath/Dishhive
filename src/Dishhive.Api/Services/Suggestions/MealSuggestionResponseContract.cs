using Dishhive.Api.Models;
using Dishhive.Api.Services.Ai;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>Single owner of the model response DTOs, JSON schema, tolerant parser,
/// and corrective-reprompt quoting.</summary>
public static partial class MealSuggestionResponseContract
{
    public static string ExampleJson { get; } = AiJsonContract.Serialize(
        new WeekSuggestionsPayload(
        [
            new DaySuggestionPayload(
                "yyyy-MM-dd", "...", null, "...", null, null,
                MealType.Dinner, Course.Main, [], [])
        ]));

    private const int MaxRepromptQuoteChars = 2000;

    public static ChatResponseFormat JsonSchemaFormat { get; } =
        AiJsonContract.CreateResponseFormat<WeekSuggestionsPayload>(
            "week_suggestions",
            "A meal suggestion per requested date");

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
                var payload = AiJsonContract.Deserialize<WeekSuggestionsPayload>(
                    text[start..(end + 1)]);
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
                var items = AiJsonContract.Deserialize<List<DaySuggestionPayload>>(
                    text[arrayStart..(arrayEnd + 1)]);
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
    [property: Description("yyyy-MM-dd")] string? Date,
    string? DishName,
    string? RecipeTitle,
    string? Reason,
    [property: Description("Candidate id from the verified candidates supplied by Dishhive, or null")]
    string? ExternalCandidateId,
    [property: Description("Exact id from the freezer items list, or null")]
    string? FreezerItemId = null,
    MealType MealType = Dishhive.Api.Models.MealType.Dinner,
    Course Course = Dishhive.Api.Models.Course.Main,
    [property: Description("Empty means every household member; otherwise exact ids from the household list")]
    IReadOnlyList<string>? AttendeeIds = null,
    [property: Description("Exact ids from the normalized planning intent satisfied by this suggestion")]
    IReadOnlyList<string>? ConstraintIds = null,
    [property: Description("Exact id from the known recipes list, or null for freezer, external, or free-text dishes")]
    string? LocalRecipeId = null);
