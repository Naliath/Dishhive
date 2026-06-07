using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Dishhive.Api.Services.Agents.Planning;

/// <summary>
/// Conversational + single-shot meal-planning agent. Uses the Microsoft Agent Framework
/// (<see cref="ChatClientAgent"/>) with read-only tools defined in <see cref="MealPlanningTools"/>.
///
/// Two entry points:
/// <list type="bullet">
///   <item><see cref="SuggestAsync"/> — single recipe suggestion for one slot, returns structured JSON.</item>
///   <item><see cref="ChatAsync"/> — free-form conversational planning assistant.</item>
/// </list>
/// </summary>
public interface IMealPlanningAgent
{
    bool IsAvailable { get; }
    Task<MealSuggestionResult?> SuggestAsync(MealSuggestionInput input, CancellationToken ct = default);
    Task<string> ChatAsync(IReadOnlyList<ChatTurn> conversation, CancellationToken ct = default);
}

public sealed record MealSuggestionInput(
    string? VagueIntent,
    string? IntentTag,
    DateOnly Date,
    string MealType,
    IReadOnlyList<Guid> AttendingFamilyMemberIds);

public sealed record MealSuggestionResult(Guid? RecipeId, string DishLabel, string Reason);

public sealed record ChatTurn(string Role, string Content);

public sealed class MealPlanningAgent : IMealPlanningAgent
{
    private const string AgentName = "DishhiveMealPlanner";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IChatClientFactory _chatFactory;
    private readonly MealPlanningTools _tools;
    private readonly ILogger<MealPlanningAgent> _logger;

    public MealPlanningAgent(
        IChatClientFactory chatFactory,
        MealPlanningTools tools,
        ILogger<MealPlanningAgent> logger)
    {
        _chatFactory = chatFactory;
        _tools = tools;
        _logger = logger;
    }

    public bool IsAvailable => _chatFactory.IsAvailable;

    public async Task<MealSuggestionResult?> SuggestAsync(MealSuggestionInput input, CancellationToken ct = default)
    {
        var agent = CreateAgent()
            ?? throw new AgentUnavailableException("AI agent is disabled. Configure Dishhive:Ai:Provider to use meal-planning suggestions.");

        var prompt = BuildSuggestPrompt(input);
        var run = await agent.RunAsync(prompt, session: null, options: null, cancellationToken: ct);

        var json = StripFences(GetText(run));
        try
        {
            var parsed = JsonSerializer.Deserialize<MealSuggestionResult>(json, JsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.DishLabel)) return null;
            return parsed;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Meal-planning agent returned malformed suggestion JSON: {Body}", run.Text);
            return null;
        }
    }

    public async Task<string> ChatAsync(IReadOnlyList<ChatTurn> conversation, CancellationToken ct = default)
    {
        var agent = CreateAgent()
            ?? throw new AgentUnavailableException("AI agent is disabled. Configure Dishhive:Ai:Provider to chat with the planner.");

        // Send the entire conversation in one call; the underlying ChatClient handles tool invocation.
        var messages = conversation
            .Select(t => new ChatMessage(MapRole(t.Role), t.Content))
            .ToList();
        var run = await agent.RunAsync(messages, session: null, options: null, cancellationToken: ct);
        return GetText(run);
    }

    private ChatClientAgent? CreateAgent()
    {
        var chat = _chatFactory.Get();
        if (chat is null) return null;
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(_tools.ListFamilyMembers),
            AIFunctionFactory.Create((string? query, string? tag) => _tools.SearchRecipes(query, tag),
                name: nameof(MealPlanningTools.SearchRecipes)),
            AIFunctionFactory.Create((int? days) => _tools.GetRecentHistory(days),
                name: nameof(MealPlanningTools.GetRecentHistory)),
            AIFunctionFactory.Create(_tools.GetFreezyItems),
        };
        return new ChatClientAgent(
            chatClient: chat,
            instructions: SystemInstructions,
            name: AgentName,
            description: null,
            tools: tools);
    }

    private static string GetText(AgentResponse response) =>
        response.Messages
            .SelectMany(m => m.Contents.OfType<TextContent>())
            .Aggregate(new System.Text.StringBuilder(), (sb, c) => sb.Append(c.Text))
            .ToString();

    private const string SystemInstructions =
        """
        You are Dishhive's family meal-planning assistant. You help plan meals for a single household.
        Use the tools provided to fetch recipes, family members, frozen items, and the recent history
        before making a suggestion. Honor allergies and diet constraints as hard rules. Avoid suggesting
        recipes the family ate in the last 7 days when reasonable alternatives exist.

        When asked for a single suggestion, respond with ONLY a JSON object:
          { "recipeId": "guid|null", "dishLabel": "string", "reason": "short justification" }

        For free-form chat, respond conversationally and briefly. Cite specific recipes and frozen items
        you saw via the tools when relevant.
        """;

    private static string BuildSuggestPrompt(MealSuggestionInput input)
    {
        var attendees = input.AttendingFamilyMemberIds.Count == 0
            ? "All family members"
            : string.Join(", ", input.AttendingFamilyMemberIds);
        return $$"""
            Suggest one meal for:
              Date: {{input.Date:yyyy-MM-dd}}
              Meal: {{input.MealType}}
              Attendees: {{attendees}}
              Vague intent: {{input.VagueIntent ?? "(none)"}}
              Intent tag: {{input.IntentTag ?? "(none)"}}

            Use the tools to inspect family preferences and recent history first.
            Respond with ONLY the JSON object — no prose, no markdown fences.
            """;
    }

    private static ChatRole MapRole(string role) => role.ToLowerInvariant() switch
    {
        "user" => ChatRole.User,
        "assistant" => ChatRole.Assistant,
        "system" => ChatRole.System,
        _ => ChatRole.User,
    };

    private static string StripFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```")) return trimmed;
        var first = trimmed.IndexOf('\n');
        var last = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return first > 0 && last > first ? trimmed.Substring(first + 1, last - first - 1).Trim() : trimmed;
    }
}
