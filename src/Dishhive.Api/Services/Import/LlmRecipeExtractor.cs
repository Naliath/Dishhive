using Dishhive.Api.Services.Suggestions;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dishhive.Api.Services.Import;

/// <summary>
/// Last-resort recipe extraction: when the dedicated providers and the recipe-scrapers
/// sidecar can't parse a page, the configured LLM reads the page text and produces the
/// structured recipe. Best-effort and model-dependent — the structured scrapers always
/// win when they succeed. Only available when AI is configured (see Program.cs).
/// </summary>
public interface ILlmRecipeExtractor
{
    /// <summary>Whether LLM extraction can run (AI configured)</summary>
    bool IsAvailable { get; }

    /// <summary>Extracts a recipe from page HTML, or null when the page holds no recipe</summary>
    Task<ImportedRecipe?> ExtractAsync(string html, Uri sourceUrl, CancellationToken cancellationToken = default);
}

/// <summary>Default when AI is not configured: extraction unavailable</summary>
public class NoOpLlmRecipeExtractor : ILlmRecipeExtractor
{
    public bool IsAvailable => false;

    public Task<ImportedRecipe?> ExtractAsync(string html, Uri sourceUrl, CancellationToken cancellationToken = default)
        => Task.FromResult<ImportedRecipe?>(null);
}

public partial class LlmRecipeExtractor : ILlmRecipeExtractor
{
    private const string SystemPrompt =
        """
        You extract a single cooking recipe from the text of a web page. Use ONLY
        information present in the text. Convert times to whole minutes. If the page is
        not a recipe, reply with {"title":null}.

        Reply with ONLY a JSON object in exactly this shape, no other text:
        {"title":"...","description":"... or null","ingredients":["line",...],
        "instructions":["step",...],"servings":4,"prepTimeMinutes":null,
        "cookTimeMinutes":null,"totalTimeMinutes":null,"category":"... or null",
        "keywords":"comma,separated or null","imageUrl":"... or null"}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IChatClient _chatClient;
    private readonly AiOptions _options;
    private readonly ILogger<LlmRecipeExtractor> _logger;

    public LlmRecipeExtractor(IChatClient chatClient, AiOptions options, ILogger<LlmRecipeExtractor> logger)
    {
        _chatClient = chatClient;
        _options = options;
        _logger = logger;
    }

    public bool IsAvailable => true;

    public async Task<ImportedRecipe?> ExtractAsync(string html, Uri sourceUrl, CancellationToken cancellationToken = default)
    {
        var text = HtmlText.ToPlainText(html);
        if (text.Length < 40)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var systemPrompt = _options.DisableThinking ? "/no_think\n" + SystemPrompt : SystemPrompt;
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, $"Page URL: {sourceUrl}\n\nPage text:\n{text}")
        };
        var chatOptions = new ChatOptions
        {
            MaxOutputTokens = _options.MaxOutputTokens,
            Temperature = (float)_options.Temperature
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken: timeout.Token);
            var payload = ParsePayload(response.Text);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Title))
            {
                _logger.LogInformation("LLM found no recipe on {Url}", sourceUrl);
                return null;
            }

            return new ImportedRecipe
            {
                Title = payload.Title.Trim(),
                Description = Clean(payload.Description),
                IngredientLines = payload.Ingredients?.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).ToList() ?? [],
                Steps = payload.Instructions?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList() ?? [],
                Servings = payload.Servings,
                ImageUrl = Clean(payload.ImageUrl),
                SourceUrl = sourceUrl.AbsoluteUri,
                PrepTimeMinutes = payload.PrepTimeMinutes,
                CookTimeMinutes = payload.CookTimeMinutes,
                TotalTimeMinutes = payload.TotalTimeMinutes,
                Category = Clean(payload.Category),
                Keywords = Clean(payload.Keywords),
                RawData = null
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "LLM recipe extraction failed for {Url}", sourceUrl);
            return null;
        }
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)
            ? null : value.Trim();

    [GeneratedRegex(@"<(?:think|reasoning)>.*?(?:</(?:think|reasoning)>|$)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ReasoningBlockRegex();

    /// <summary>Slices the outermost JSON object from the reply, tolerating think-tags/fences</summary>
    internal static ExtractionPayload? ParsePayload(string? text)
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

        try
        {
            return JsonSerializer.Deserialize<ExtractionPayload>(text[start..(end + 1)], JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal sealed record ExtractionPayload(
        string? Title,
        string? Description,
        List<string>? Ingredients,
        List<string>? Instructions,
        int? Servings,
        int? PrepTimeMinutes,
        int? CookTimeMinutes,
        int? TotalTimeMinutes,
        string? Category,
        string? Keywords,
        string? ImageUrl);
}
