namespace Dishhive.Api.Models;

/// <summary>
/// Stable storage and API keys for settings known by Dishhive.
///
/// These remain string constants rather than an enum because the values are persisted
/// strings and are also accepted by the generic /api/settings/{key} route. Several keys
/// do not map naturally to enum names, and an enum would still require an explicit,
/// backward-compatible string conversion at every storage and HTTP boundary.
/// </summary>
public static class UserSettingKeys
{
    /// <summary>Household measurement preference: metric or imperial.</summary>
    public const string MeasurementSystem = "measurementSystem";

    /// <summary>Week boundary preference: Monday or Sunday.</summary>
    public const string FirstDayOfWeek = "firstDayOfWeek";

    /// <summary>Interface and recipe-localization language: en or nl.</summary>
    public const string PreferredLanguage = "preferredLanguage";

    /// <summary>Whether AI-assisted imports translate recipes to the preferred language.</summary>
    public const string TranslateImportedRecipes = "translateImportedRecipes";

    /// <summary>First-run wizard lifecycle marker.</summary>
    public const string OnboardingStatus = "onboardingStatus";

    /// <summary>User override for the AI meal-planning system prompt.</summary>
    public const string AiSystemPrompt = "aiSystemPrompt";

    /// <summary>Shipped AI prompt captured when the user created an override.</summary>
    public const string AiSystemPromptBaseline = "aiSystemPromptBaseline";

    /// <summary>JSON list of disabled computed collection identifiers.</summary>
    public const string DisabledAutoCollections = "autoCollections.disabled";

    /// <summary>Marker preventing demo data from being seeded more than once.</summary>
    public const string DemoDataSeeded = "demo.dataSeeded";
}
