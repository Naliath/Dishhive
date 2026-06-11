using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Text.Json;

namespace Dishhive.Api.Services.Demo;

/// <summary>
/// Seeds demo data when demo mode is on (Demo:Enabled / Demo__Enabled in Docker).
/// Reads pre-baked recipe data from the embedded demo-seed.json (generated once via
/// "dotnet run --project src/Dishhive.Api -- --generate-demo-seed") so no outbound
/// HTTP is needed at seed time.
///
/// Seeding happens at most once: skipped when the database already holds data, and a
/// marker user setting prevents re-seeding after the user deletes demo data.
/// </summary>
public class DemoDataSeeder : BackgroundService
{
    internal const string SeededSettingKey = "demo.dataSeeded";
    private const string SeedResourceSuffix = "demo-seed.json";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DemoDataSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue<bool>("Demo:Enabled"))
        {
            return;
        }

        try
        {
            await SeedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Application shutting down during seeding; the marker setting was not
            // written, so seeding resumes cleanly on the next start
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demo data seeding failed");
        }
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();

        if (await context.UserSettings.AnyAsync(s => s.Key == SeededSettingKey, cancellationToken))
        {
            return;
        }

        if (await context.Recipes.AnyAsync(cancellationToken)
            || await context.FamilyMembers.AnyAsync(cancellationToken))
        {
            _logger.LogInformation("Demo mode is on but the database already contains data; skipping demo seed");
            return;
        }

        var seedRecords = await LoadSeedRecordsAsync(cancellationToken);

        _logger.LogInformation("Demo mode: seeding {RecipeCount} recipes and {MemberCount} family members from embedded seed",
            seedRecords.Length, DemoData.Members.Count);

        var recipesByUrl = new Dictionary<string, Recipe>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in seedRecords)
        {
            var recipe = new Recipe
            {
                Id = Guid.NewGuid(),
                Title = record.Title,
                Description = record.Description,
                Servings = record.Servings,
                PrepTimeMinutes = record.PrepTimeMinutes,
                CookTimeMinutes = record.CookTimeMinutes,
                TotalTimeMinutes = record.TotalTimeMinutes,
                Category = record.Category,
                Keywords = record.Keywords,
                VideoUrl = record.VideoUrl,
                SourceUrl = record.SourceUrl,
                SourceProvider = record.SourceProvider,
                SourceRawData = record.SourceRawData,
                ImageContentType = record.ImageContentType,
                ImageData = record.ImageDataBase64 != null
                    ? Convert.FromBase64String(record.ImageDataBase64)
                    : null
            };

            foreach (var ing in record.Ingredients)
            {
                recipe.Ingredients.Add(new RecipeIngredient
                {
                    SortOrder = ing.SortOrder,
                    Name = ing.Name,
                    Quantity = ing.Quantity,
                    Unit = ing.Unit,
                    OriginalText = ing.OriginalText,
                    OriginalQuantity = ing.OriginalQuantity,
                    OriginalUnit = ing.OriginalUnit
                });
            }

            foreach (var step in record.Steps)
            {
                recipe.Steps.Add(new RecipeStep
                {
                    StepNumber = step.StepNumber,
                    Instruction = step.Instruction
                });
            }

            context.Recipes.Add(recipe);
            recipesByUrl[record.SourceUrl] = recipe;
        }

        var familyMembers = new List<FamilyMember>();
        foreach (var demoMember in DemoData.Members)
        {
            var member = new FamilyMember
            {
                Id = Guid.NewGuid(),
                Name = demoMember.Name,
                Allergies = demoMember.Allergies,
                DietaryConstraints = demoMember.DietaryConstraints,
                PreferenceNotes = demoMember.PreferenceNotes
            };
            context.FamilyMembers.Add(member);
            familyMembers.Add(member);

            foreach (var url in demoMember.FavoriteRecipeUrls)
            {
                if (recipesByUrl.TryGetValue(url, out var recipe))
                {
                    context.FamilyMemberFavorites.Add(new FamilyMemberFavorite
                    {
                        FamilyMember = member,
                        RecipeId = recipe.Id,
                        DishName = recipe.Title
                    });
                }
            }

            foreach (var dishName in demoMember.FavoriteDishNames)
            {
                context.FamilyMemberFavorites.Add(new FamilyMemberFavorite
                {
                    FamilyMember = member,
                    DishName = dishName
                });
            }
        }

        SeedPastMeals(context, recipesByUrl.Values.ToList(), familyMembers);

        context.UserSettings.Add(new UserSetting { Key = SeededSettingKey, Value = "true" });
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Demo data seeded: {RecipeCount} recipes, {MemberCount} family members",
            recipesByUrl.Count, DemoData.Members.Count);
    }

    /// <summary>
    /// Two weeks of past demo dinners with eaten marks and ratings, so the history,
    /// statistics and AI suggestion features have data to show. Deterministic: same
    /// dishes and ratings on every fresh seed.
    /// </summary>
    private static void SeedPastMeals(
        DishhiveDbContext context, IReadOnlyList<Recipe> recipes, IReadOnlyList<FamilyMember> members)
    {
        if (recipes.Count == 0 || members.Count == 0)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var daysAgo = 14; daysAgo >= 1; daysAgo--)
        {
            var recipe = recipes[daysAgo % recipes.Count];
            // One skipped day keeps the demo data honest; the rest were eaten
            var skipped = daysAgo == 5;

            var meal = new PlannedMeal
            {
                Id = Guid.NewGuid(),
                Date = today.AddDays(-daysAgo),
                MealType = MealType.Dinner,
                Course = Course.Main,
                RecipeId = recipe.Id,
                DishName = recipe.Title,
                Eaten = skipped ? EatenStatus.Skipped : EatenStatus.Eaten
            };

            foreach (var (member, index) in members.Select((m, i) => (m, i)))
            {
                meal.Attendees.Add(new PlannedMealAttendee { FamilyMemberId = member.Id });
                if (!skipped)
                {
                    // Spread deterministic ratings between 3 and 5
                    meal.Ratings.Add(new MealRating
                    {
                        FamilyMemberId = member.Id,
                        Rating = 3 + (daysAgo + index) % 3
                    });
                }
            }

            context.PlannedMeals.Add(meal);
        }
    }

    private static async Task<SeedRecipeRecord[]> LoadSeedRecordsAsync(CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(SeedResourceSuffix))
            ?? throw new InvalidOperationException(
                $"Embedded resource '{SeedResourceSuffix}' not found in assembly");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return await JsonSerializer.DeserializeAsync<SeedRecipeRecord[]>(stream, options, cancellationToken)
            ?? throw new InvalidOperationException("demo-seed.json deserialised to null");
    }
}
