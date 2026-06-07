using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dishhive.Api.Models.Agents;
using Dishhive.Api.Services.Agents;
using Dishhive.Api.Services.RecipeImport;
using HtmlAgilityPack;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Dishhive.Api.Services.Agents.RecipeImport;

/// <summary>
/// LLM-powered recipe importer. Used as the last-resort fallback when no static
/// <see cref="IRecipeSourceProvider"/> matches a URL — see <c>docs/features/ai-agents.md</c>.
///
/// On a successful import, the agent also emits a <see cref="RecipeImportBlueprint"/> that
/// is persisted via <see cref="ILearnedSourceStore"/>. Future imports of the same host
/// are handled by <see cref="LearnedRecipeSourceProvider"/> with zero LLM calls.
/// </summary>
public interface IRecipeImportAgent
{
    bool IsAvailable { get; }
    Task<AgentImportResult> ImportAsync(Uri url, CancellationToken ct = default);
}

public sealed record AgentImportResult(ImportedRecipe Recipe, bool BlueprintSaved, string? BlueprintNote);

public sealed class RecipeImportAgent : IRecipeImportAgent
{
    private const string AgentName = "DishhiveRecipeImporter";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly IChatClientFactory _chatFactory;
    private readonly ILearnedSourceStore _store;
    private readonly HttpClient _http;
    private readonly AiAgentOptions _options;
    private readonly ILogger<RecipeImportAgent> _logger;

    public RecipeImportAgent(
        IChatClientFactory chatFactory,
        ILearnedSourceStore store,
        HttpClient http,
        IOptions<AiAgentOptions> options,
        ILogger<RecipeImportAgent> logger)
    {
        _chatFactory = chatFactory;
        _store = store;
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsAvailable => _chatFactory.IsAvailable;

    public async Task<AgentImportResult> ImportAsync(Uri url, CancellationToken ct = default)
    {
        var chat = _chatFactory.Get()
            ?? throw new AgentUnavailableException("AI agent is disabled. Configure Dishhive:Ai:Provider to use adaptive recipe import.");

        // 1. Fetch the page (using the same HttpClient policy as the static providers).
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var rawHtml = await response.Content.ReadAsStringAsync(ct);
        var trimmed = TrimHtmlForLlm(rawHtml, _options.RecipeImport.MaxHtmlChars);

        // 2. Run the agent for structured extraction.
        var agent = new ChatClientAgent(
            chatClient: chat,
            instructions: SystemInstructions,
            name: AgentName,
            description: null,
            tools: null);

        var prompt = BuildExtractionPrompt(url, trimmed);
        var run = await agent.RunAsync(prompt, session: null, options: null, cancellationToken: ct);
        var responseText = run.Messages
            .SelectMany(m => m.Contents.OfType<TextContent>())
            .Aggregate(new System.Text.StringBuilder(), (sb, c) => sb.Append(c.Text))
            .ToString();
        var json = ExtractJson(responseText);

        AgentExtraction extraction;
        try
        {
            extraction = JsonSerializer.Deserialize<AgentExtraction>(json, JsonOptions)
                ?? throw new InvalidOperationException("Agent returned an empty response.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Recipe import agent returned malformed JSON for {Url}.", url);
            throw new InvalidOperationException("The recipe-import agent returned a malformed response.", ex);
        }

        // 3. Build the imported recipe + blueprint.
        var providerKey = $"learned:{url.Host.ToLowerInvariant()}";
        var imported = MapToImported(extraction, url, rawHtml, providerKey);
        var blueprint = MapBlueprint(extraction);

        // 4. Validate the blueprint by replaying it. Save only if validation succeeds.
        var blueprintSaved = false;
        string? note = null;
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(rawHtml);
            var replay = blueprint.Strategy switch
            {
                LearnedRecipeSourceStrategy.JsonLd => JsonLdRecipeParser.TryParse(url, doc, providerKey),
                LearnedRecipeSourceStrategy.XPath => XPathRecipeParser.TryParse(url, doc, providerKey, blueprint, rawHtml),
                _ => null,
            };

            if (replay is not null && !string.IsNullOrWhiteSpace(replay.Title) && replay.Ingredients.Count > 0)
            {
                await _store.UpsertAsync(url.Host, blueprint, url.ToString(), ct);
                blueprintSaved = true;
                _logger.LogInformation("Learned blueprint stored for {Host} (strategy: {Strategy}).",
                    url.Host, blueprint.Strategy);
            }
            else
            {
                note = "Blueprint validation failed — recipe imported but next visit will use the LLM again.";
                _logger.LogInformation("Blueprint replay failed for {Host}; not persisting.", url.Host);
            }
        }
        catch (Exception ex)
        {
            note = $"Blueprint validation threw: {ex.Message}";
            _logger.LogWarning(ex, "Blueprint replay threw for {Host}.", url.Host);
        }

        return new AgentImportResult(imported, blueprintSaved, note);
    }

    // ---------------------------------------------------------------- prompts

    private const string SystemInstructions =
        """
        You are Dishhive's adaptive recipe-import agent. You are given a single recipe page's HTML
        and must extract a clean recipe AND emit a parsing blueprint that lets a deterministic parser
        re-import the same source in the future without calling you again.

        OUTPUT: a single JSON object matching this schema, no prose, no markdown fences:

        {
          "title": "string",
          "description": "string|null",
          "servings": 4,
          "imageUrl": "string|null",
          "ingredients": [{"name":"...", "quantity":1.0, "unit":"g|null"}],
          "steps": ["..."],
          "tags": ["..."],
          "blueprint": {
            "version": 1,
            "strategy": "JsonLd" | "XPath",
            "titleXPath": "string|null",
            "descriptionXPath": "string|null",
            "imageXPath": "string|null",
            "servingsXPath": "string|null",
            "ingredientsXPath": "string|null",
            "stepsXPath": "string|null"
          }
        }

        Rules:
        - If the HTML contains <script type="application/ld+json"> with a schema.org Recipe, set
          blueprint.strategy = "JsonLd" and leave the XPath fields null. (We have a deterministic
          JSON-LD parser.)
        - Otherwise set blueprint.strategy = "XPath" and provide selectors that, when evaluated
          against the DOM with HtmlAgilityPack, return:
            * title: a single node whose InnerText is the title
            * ingredients: multiple nodes, one per ingredient line
            * steps: multiple nodes, one per step
        - servings is an integer; if unknown, return 4.
        - Only emit XPath that you can verify in the page; never guess class names that aren't visible.
        - Output ONLY the JSON object.
        """;

    private static string BuildExtractionPrompt(Uri url, string trimmedHtml) =>
        $$"""
        URL: {{url}}
        ---
        HTML (truncated):
        {{trimmedHtml}}
        """;

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Strips <c>&lt;script&gt;</c>/<c>&lt;style&gt;</c>/<c>&lt;svg&gt;</c> blocks (except JSON-LD)
    /// then truncates. Keeps token cost predictable on long pages.
    /// </summary>
    public static string TrimHtmlForLlm(string html, int maxChars)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Keep JSON-LD scripts; remove everything else heavy.
        var stripXPaths = new[]
        {
            "//script[not(@type='application/ld+json')]",
            "//style",
            "//svg",
            "//noscript",
            "//iframe",
        };
        foreach (var xpath in stripXPaths)
        {
            var nodes = doc.DocumentNode.SelectNodes(xpath);
            if (nodes is null) continue;
            foreach (var n in nodes.ToList()) n.Remove();
        }

        var cleaned = doc.DocumentNode.OuterHtml;
        return cleaned.Length <= maxChars ? cleaned : cleaned.Substring(0, maxChars);
    }

    /// <summary>Strips ```json ... ``` fences if the model returned them.</summary>
    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var first = trimmed.IndexOf('\n');
            var last = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (first > 0 && last > first) return trimmed.Substring(first + 1, last - first - 1).Trim();
        }
        return trimmed;
    }

    private static ImportedRecipe MapToImported(AgentExtraction e, Uri sourceUrl, string rawHtml, string providerKey)
    {
        var ingredients = (e.Ingredients ?? new()).Select((i, idx) =>
            new ImportedIngredient(idx, i.Name ?? "(unknown)", i.Quantity, i.Unit, i.Quantity, i.Unit, null, null)).ToList();
        var steps = (e.Steps ?? new()).Select((s, idx) => new ImportedStep(idx, s)).ToList();

        return new ImportedRecipe(
            Title: e.Title ?? "(untitled)",
            Description: e.Description,
            Servings: e.Servings ?? 4,
            ImageUrl: e.ImageUrl,
            VideoUrl: null,
            SourceUrl: sourceUrl,
            ProviderKey: providerKey,
            // Preserve the (possibly truncated) HTML for traceability.
            SourceRawPayload: rawHtml.Length > 200_000 ? rawHtml.Substring(0, 200_000) : rawHtml,
            Ingredients: ingredients,
            Steps: steps,
            Tags: (e.Tags ?? new()).Where(t => !string.IsNullOrWhiteSpace(t)).ToList());
    }

    private static RecipeImportBlueprint MapBlueprint(AgentExtraction e)
    {
        if (e.Blueprint is null) return new RecipeImportBlueprint { Strategy = LearnedRecipeSourceStrategy.JsonLd };
        return new RecipeImportBlueprint
        {
            Version = e.Blueprint.Version > 0 ? e.Blueprint.Version : 1,
            Strategy = e.Blueprint.Strategy ?? LearnedRecipeSourceStrategy.JsonLd,
            TitleXPath = e.Blueprint.TitleXPath,
            DescriptionXPath = e.Blueprint.DescriptionXPath,
            ImageXPath = e.Blueprint.ImageXPath,
            ServingsXPath = e.Blueprint.ServingsXPath,
            IngredientsXPath = e.Blueprint.IngredientsXPath,
            StepsXPath = e.Blueprint.StepsXPath,
        };
    }

    // ---------------------------------------------------------------- DTOs

    internal sealed class AgentExtraction
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public int? Servings { get; set; }
        public string? ImageUrl { get; set; }
        public List<AgentIngredient>? Ingredients { get; set; }
        public List<string>? Steps { get; set; }
        public List<string>? Tags { get; set; }
        public AgentBlueprint? Blueprint { get; set; }
    }

    internal sealed class AgentIngredient
    {
        public string? Name { get; set; }
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
    }

    internal sealed class AgentBlueprint
    {
        public int Version { get; set; } = 1;
        public LearnedRecipeSourceStrategy? Strategy { get; set; }
        public string? TitleXPath { get; set; }
        public string? DescriptionXPath { get; set; }
        public string? ImageXPath { get; set; }
        public string? ServingsXPath { get; set; }
        public string? IngredientsXPath { get; set; }
        public string? StepsXPath { get; set; }
    }
}
