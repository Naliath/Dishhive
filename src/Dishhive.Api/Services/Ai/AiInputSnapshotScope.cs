using System.Security.Cryptography;
using System.Text.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Suggestions;

namespace Dishhive.Api.Services.Ai;

/// <summary>Immutable source-version evidence for the enclosing feature operation.
/// Per-call PromptHash still identifies the actual messages sent to the model.</summary>
public sealed class AiInputSnapshotScope : IDisposable
{
    private static readonly AsyncLocal<AiInputSnapshotScope?> Ambient = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly AiInputSnapshotScope? _previous;
    public static AiInputSnapshotScope? Current => Ambient.Value;
    public string RevisionsJson { get; }
    public string Fingerprint { get; }

    public AiInputSnapshotScope(IEnumerable<AiInputRevisionDto> revisions)
    {
        _previous = Ambient.Value;
        RevisionsJson = JsonSerializer.Serialize(revisions.ToArray(), Json);
        Fingerprint = Hash(JsonSerializer.Deserialize<JsonElement>(RevisionsJson));
        Ambient.Value = this;
    }
    public static AiInputSnapshotScope ForRecipe(Recipe recipe) => new([
        new("recipe", recipe.Id.ToString(), recipe.Revision, recipe.FactsConfirmationVersion,
            Hash(new { recipe.Title, recipe.Description, recipe.ContentLanguage, recipe.OriginalTitle,
                Ingredients = recipe.Ingredients.OrderBy(item => item.SortOrder).Select(item => new { item.Name, item.Quantity, item.Unit }),
                Steps = recipe.Steps.OrderBy(item => item.StepNumber).Select(item => item.Instruction) }))]);

    public static AiInputSnapshotScope ForPlanning(MealSuggestionRequest request) => new(
        new[] { new AiInputRevisionDto("planningContext", request.WeekStart.ToString("yyyy-MM-dd"), null, null, Hash(request)) }
        .Concat(request.KnownRecipes.Select(recipe => new AiInputRevisionDto("recipe", recipe.Id.ToString(),
            recipe.Revision, recipe.FactsConfirmationVersion, Hash(recipe)))));

    public static AiInputSnapshotScope ForContent<T>(string kind, string id, T content) => new(
        (Current == null ? [] : JsonSerializer.Deserialize<AiInputRevisionDto[]>(Current.RevisionsJson, Json) ?? [])
        .Append(new AiInputRevisionDto(kind, id, null, null, Hash(content))));

    internal static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json)));
    public void Dispose() => Ambient.Value = _previous;
}
