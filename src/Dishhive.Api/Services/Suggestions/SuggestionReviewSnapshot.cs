using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dishhive.Api.Models;

namespace Dishhive.Api.Services.Suggestions;

internal sealed record SuggestionReviewSnapshot(
    IReadOnlyList<Guid> AttendeeIds,
    string AttendanceFingerprint,
    string? RecipeFingerprint,
    IReadOnlyList<PlanningConstraint> Constraints,
    IReadOnlyList<string> ContainsClasses,
    bool FactsAssessed,
    string? FreezerFingerprint = null)
{
    public static string Fingerprint(Recipe recipe) => Hash(new
    {
        recipe.Title, recipe.Description, recipe.Servings, recipe.TotalTimeMinutes,
        recipe.PrepTimeMinutes, recipe.CookTimeMinutes, recipe.Revision,
        recipe.FactsConfirmationVersion, recipe.DietaryFactsStatus, recipe.DietaryFactsAssessedRevision,
        Ingredients = recipe.Ingredients.OrderBy(item => item.SortOrder)
            .Select(item => new { item.Name, item.Quantity, item.Unit }),
        Steps = recipe.Steps.OrderBy(item => item.StepNumber).Select(item => item.Instruction),
        Facts = recipe.DietaryFacts.Select(item => item.IngredientClass).Order()
    });

    public static string Fingerprint(IEnumerable<FamilyMember> members) => Hash(members
        .OrderBy(member => member.Id).Select(member => new
        {
            member.Id, member.IsActive, member.IsGuest,
            Tags = member.DietaryTags.OrderBy(tag => tag.DietaryTagId).Select(tag => new
            {
                tag.DietaryTagId, Kind = tag.DietaryTag?.Kind,
                Classes = tag.ExcludedClasses.Order()
            })
        }));

    private static string Hash(object value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
