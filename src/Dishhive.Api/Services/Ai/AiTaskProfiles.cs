using Dishhive.Api.Services.Suggestions;

namespace Dishhive.Api.Services.Ai;

public enum AiTaskKind
{
    CapabilityPlanning,
    PlanningIntent,
    WeekPlanning,
    RecipeFacts,
    RecipeTranslation,
    RecipeImportExtraction
}

public sealed record AiTaskProfile(
    string Name,
    int MaxOutputTokens,
    float Temperature,
    int TimeoutSeconds,
    int MaxRetries,
    bool RequiresPlanningCapability);

public static class AiTaskProfiles
{
    public static AiTaskProfile Resolve(
        AiTaskKind task, AiOptions options, bool useExtendedTimeout = false) => task switch
    {
        AiTaskKind.CapabilityPlanning => new(
            "capability.planning", Positive(options.MaxOutputTokens), Temperature(options),
            Positive(options.TimeoutSeconds), 0, false),
        AiTaskKind.PlanningIntent => new(
            "planning.intent", Limit(options.MaxOutputTokens, 1400), 0,
            Positive(options.TimeoutSeconds), 0, false),
        AiTaskKind.WeekPlanning => new(
            "planning.generate", Positive(options.MaxOutputTokens), Temperature(options),
            Positive(useExtendedTimeout ? options.AgentTimeoutSeconds : options.TimeoutSeconds),
            Math.Max(0, options.MaxRetries), true),
        AiTaskKind.RecipeFacts => new(
            "recipe.facts", Limit(options.MaxOutputTokens, options.FactsMaxOutputTokens), 0,
            Positive(options.TimeoutSeconds), Math.Max(0, options.MaxRetries), false),
        AiTaskKind.RecipeTranslation => new(
            "recipe.translate", Limit(options.MaxOutputTokens, options.TranslationMaxOutputTokens), 0,
            Positive(options.TimeoutSeconds), Math.Max(0, options.MaxRetries), false),
        AiTaskKind.RecipeImportExtraction => new(
            "recipe.import_extract", Limit(options.MaxOutputTokens, options.ImportExtractionMaxOutputTokens), 0,
            Positive(options.TimeoutSeconds), Math.Max(0, options.MaxRetries), false),
        _ => throw new ArgumentOutOfRangeException(nameof(task), task, null)
    };

    private static int Positive(int value) => Math.Max(1, value);
    private static int Limit(int shared, int task) => Math.Min(Positive(shared), Positive(task));
    private static float Temperature(AiOptions options) =>
        Math.Clamp((float)options.Temperature, 0, 2);
}
