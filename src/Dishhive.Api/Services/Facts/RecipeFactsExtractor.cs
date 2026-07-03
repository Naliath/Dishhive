using Dishhive.Api.Models;
using Dishhive.Api.Services.Suggestions;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dishhive.Api.Services.Facts;

/// <summary>
/// Classifies a recipe's ingredient list into canonical <see cref="IngredientClass"/>es
/// ("contains milk, gluten"). One-shot LLM call, best-effort: null means the
/// assessment failed and the recipe stays Unassessed — distinct from an empty list,
/// which is a real "contains none of the classes" verdict. Only available when AI is
/// configured (see Program.cs); same posture as <see cref="Import.ILlmRecipeExtractor"/>.
/// </summary>
public interface IRecipeFactsExtractor
{
    /// <summary>Whether facts extraction can run (AI configured)</summary>
    bool IsAvailable { get; }

    /// <summary>The contained classes, empty when none apply, or null when the call failed</summary>
    Task<IReadOnlyList<IngredientClass>?> ExtractAsync(
        string title, IReadOnlyList<string> ingredientNames, CancellationToken cancellationToken = default);
}

/// <summary>Default when AI is not configured: facts extraction unavailable</summary>
public class NoOpRecipeFactsExtractor : IRecipeFactsExtractor
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<IngredientClass>?> ExtractAsync(
        string title, IReadOnlyList<string> ingredientNames, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<IngredientClass>?>(null);
}

public partial class LlmRecipeFactsExtractor : IRecipeFactsExtractor
{
    /// <summary>
    /// Asks for factual containment, not diet verdicts — "is this vegetarian" is a
    /// per-member policy the app derives itself (see docs/features/dietary-facts.md).
    /// Deliberately biased to over-include for processed products: a false
    /// "contains" surfaces visibly (warning/exclusion the user can correct), a
    /// missed allergen does not.
    /// </summary>
    private const string SystemPrompt =
        """
        You classify one cooking recipe by which ingredient classes it CONTAINS,
        based on its ingredient list (ingredient names are often Dutch). The only
        valid classes are:
        Gluten, Crustaceans, Eggs, Fish, Peanuts, Soybeans, Milk, TreeNuts, Celery,
        Mustard, Sesame, Sulphites, Lupin, Molluscs, RedMeat, Poultry, Pork,
        Gelatin, Alcohol, Honey.

        Rules:
        - Include classes hidden in derived or processed ingredients: butter, cheese,
          cream, yoghurt -> Milk; wheat flour, pasta, bread, breadcrumbs, couscous ->
          Gluten; soy sauce -> Soybeans AND Gluten; tofu -> Soybeans; fish sauce,
          anchovies, Worcestershire sauce -> Fish; mayonnaise -> Eggs; gelatine ->
          Gelatin; wine, beer, spirits -> Alcohol; stock/bouillon -> the matching
          meat or fish class.
        - Meat kinds: RedMeat = beef, veal, lamb, game (rund, kalf, lam, wild).
          Pork = pork, bacon, ham, spek, chorizo. Poultry = chicken, turkey, duck
          (kip, kalkoen, eend).
        - Base the answer on the ingredient list, not on what the dish name suggests.
        - When genuinely uncertain whether a processed product contains a class,
          INCLUDE the class (people rely on this for allergies; over-warning is the
          safe direction).

        Reply with ONLY a JSON object in exactly this shape, no other text:
        {"contains":["Milk","Gluten"]}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IChatClient _chatClient;
    private readonly AiOptions _options;
    private readonly ILogger<LlmRecipeFactsExtractor> _logger;

    public LlmRecipeFactsExtractor(IChatClient chatClient, AiOptions options, ILogger<LlmRecipeFactsExtractor> logger)
    {
        _chatClient = chatClient;
        _options = options;
        _logger = logger;
    }

    public bool IsAvailable => true;

    public async Task<IReadOnlyList<IngredientClass>?> ExtractAsync(
        string title, IReadOnlyList<string> ingredientNames, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var systemPrompt = _options.DisableThinking ? "/no_think\n" + SystemPrompt : SystemPrompt;
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, $"Recipe: {title}\nIngredients:\n{string.Join("\n", ingredientNames.Select(n => $"- {n}"))}")
        };
        var chatOptions = new ChatOptions
        {
            MaxOutputTokens = _options.MaxOutputTokens,
            Temperature = (float)_options.Temperature
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken: timeout.Token);
            stopwatch.Stop();
            _logger.LogInformation(
                "Facts assessment for \"{Title}\" in {ElapsedMs}ms{Usage}",
                title, stopwatch.ElapsedMilliseconds,
                response.Usage is { } u ? $"; input={u.InputTokenCount}, output={u.OutputTokenCount} tokens" : "");

            var classes = ParsePayload(response.Text);
            if (classes == null)
            {
                _logger.LogWarning("Facts assessment for \"{Title}\" returned no parseable JSON", title);
            }
            return classes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Facts assessment failed for \"{Title}\" after {ElapsedMs}ms",
                title, stopwatch.ElapsedMilliseconds);
            return null;
        }
    }

    [GeneratedRegex(@"<(?:think|reasoning)>.*?(?:</(?:think|reasoning)>|$)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ReasoningBlockRegex();

    /// <summary>
    /// Slices the outermost JSON object from the reply (tolerating think-tags/fences)
    /// and parses the class names; unknown names are dropped, not fatal — a model
    /// inventing "Shellfish" shouldn't discard the valid rest of its answer.
    /// </summary>
    internal static IReadOnlyList<IngredientClass>? ParsePayload(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = ReasoningBlockRegex().Replace(text, "");
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        FactsPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<FactsPayload>(text[start..(end + 1)], JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (payload?.Contains is null)
        {
            return null;
        }

        return payload.Contains
            .Select(n => IngredientClasses.TryParse(n, out var value) ? value : (IngredientClass?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }

    internal sealed record FactsPayload(List<string>? Contains);
}
