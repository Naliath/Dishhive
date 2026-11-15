using Dishhive.Api.Services.Suggestions;
using Microsoft.Extensions.AI;

namespace Dishhive.Api.Services.Ai;

public enum AiTaskKind
{
    CapabilityPlanning,
    PlanningIntent,
    PlanningResearch,
    WeekPlanning,
    RecipeFacts,
    RecipeTranslation,
    RecipeImportExtraction,
    RecipeDishMatch,
    RecipeTextExtraction,
    RecipeImageExtraction,
    CapabilityVision
}

public sealed record AiTaskProfile(
    string Name,
    int MaxInputTokens,
    int MaxOutputTokens,
    float Temperature,
    int TimeoutSeconds,
    int MaxRetries,
    bool RequiresPlanningCapability,
    bool EnableReasoning = false);

public static class AiTokenBudget
{
    // Conservative visual-token allowance for a single normalized image (at most
    // 2048px per side). Encoded size is also counted to bound unusually dense input.
    public const int ImageInputTokens = 16384;
    public static long ImageCharacters(IReadOnlyList<ChatMessage> messages) => messages
        .SelectMany(message => message.Contents).OfType<DataContent>()
        .Sum(data => Math.Max(ImageInputTokens * 2L, data.Data.Length / 16L));
    // Dishhive prompts contain UUIDs and dense JSON, which tokenize less efficiently
    // than prose. Two characters per token is deliberately conservative for admission.
    public const int ConservativeCharactersPerToken = 2;
    public const int PromptBuilderCharactersPerToken = ConservativeCharactersPerToken;

    public static long InputCharacters(IReadOnlyList<ChatMessage> messages, ChatResponseFormat? format) =>
        messages.Sum(message => (long)(message.Text?.Length ?? 0) + 64) + SchemaCharacters(format) + ImageCharacters(messages);

    public static int SchemaCharacters(ChatResponseFormat? format) =>
        format is ChatResponseFormatJson json ? json.Schema?.GetRawText().Length ?? 0 : 0;

    public static int ContentCharacters(int inputTokens, string systemPrompt,
        ChatResponseFormat? format, int retries = 1) => Math.Max(0,
        checked(inputTokens * ConservativeCharactersPerToken) - systemPrompt.Length
        - SchemaCharacters(format) - (retries + 2) * 64 - retries * 256);
}

public static class AiTaskProfiles
{
    public static AiTaskProfile Resolve(
        AiTaskKind task, AiOptions options, bool useExtendedTimeout = false) => task switch
    {
        AiTaskKind.CapabilityPlanning => new(
            "capability.planning", Positive(options.MaxInputTokens), Positive(options.MaxOutputTokens), Temperature(options),
            Positive(options.TimeoutSeconds), 0, false),
        AiTaskKind.PlanningIntent => new(
            "planning.intent", Positive(options.MaxInputTokens), Limit(options.MaxOutputTokens, 1400), 0,
            Positive(options.TimeoutSeconds), Math.Max(0, options.MaxRetries), true),
        AiTaskKind.PlanningResearch => new(
            "planning.research", Limit(options.MaxInputTokens, 3000), Limit(options.MaxOutputTokens, 400), 0,
            Positive(options.TimeoutSeconds), 0, true),
        AiTaskKind.WeekPlanning => new(
            "planning.generate", Positive(options.MaxInputTokens), Positive(options.MaxOutputTokens), Temperature(options),
            Positive(useExtendedTimeout ? options.AgentTimeoutSeconds : options.TimeoutSeconds),
            Math.Max(0, options.MaxRetries), true),
        AiTaskKind.RecipeFacts => new(
            "recipe.facts", Positive(options.FactsMaxPromptTokens), Limit(options.MaxOutputTokens, options.FactsMaxOutputTokens), 0,
            Positive(options.TimeoutSeconds), Math.Max(0, options.MaxRetries), false, options.FactsEnableReasoning),
        AiTaskKind.RecipeDishMatch => new(
            "recipe.dish_match", Limit(options.MaxInputTokens, 6000), Limit(options.MaxOutputTokens, 400), 0,
            Positive(options.TimeoutSeconds), 0, false),
        AiTaskKind.RecipeTranslation => new(
            "recipe.translate", Positive(options.TranslationMaxPromptTokens), Limit(options.MaxOutputTokens, options.TranslationMaxOutputTokens), 0,
            Positive(options.TimeoutSeconds), Math.Max(0, options.MaxRetries), false),
        AiTaskKind.RecipeImportExtraction => new(
            "recipe.import_extract", Positive(options.ImportExtractionMaxPromptTokens), Limit(options.MaxOutputTokens, options.ImportExtractionMaxOutputTokens), 0,
            Positive(options.TimeoutSeconds), Math.Max(0, options.MaxRetries), false),
        AiTaskKind.RecipeTextExtraction or AiTaskKind.RecipeImageExtraction => new(
            task == AiTaskKind.RecipeTextExtraction ? "recipe.import_text" : "recipe.import_image",
            Positive(options.ImportExtractionMaxPromptTokens), Limit(options.MaxOutputTokens, options.ImportExtractionMaxOutputTokens),
            0, Positive(options.TimeoutSeconds), Math.Max(0, options.MaxRetries), false),
        AiTaskKind.CapabilityVision => new(
            "capability.vision", Positive(options.ImportExtractionMaxPromptTokens), Limit(options.MaxOutputTokens, 400),
            0, Positive(options.TimeoutSeconds), 0, false),
        _ => throw new ArgumentOutOfRangeException(nameof(task), task, null)
    };

    private static int Positive(int value) => Math.Max(1, value);
    private static int Limit(int shared, int task) => Math.Min(Positive(shared), Positive(task));
    private static float Temperature(AiOptions options) =>
        Math.Clamp((float)options.Temperature, 0, 2);
}
