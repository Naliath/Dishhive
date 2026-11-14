using Dishhive.Api.Services.Time;
using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Import;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace Dishhive.Api.Services.Demo;

/// <summary>
/// Seeds demo data when demo mode is on (Demo:Enabled / Demo__Enabled in Docker).
/// Imports the embedded enriched recipe backup, so no website or model calls are
/// needed at seed time. Regenerate it with scripts/generate-demo-seed.ps1.
///
/// Seeding happens at most once: skipped when the database already holds data, and a
/// marker user setting prevents re-seeding after the user deletes demo data.
/// </summary>
public class DemoDataSeeder : BackgroundService
{
    private readonly IHouseholdClock _clock;
    private const string SeedResourceSuffix = "demo-seed.zip";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DemoDataSeeder> logger, IHouseholdClock clock)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
        _clock = clock;
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

        if (await context.UserSettings.AnyAsync(s => s.Key == UserSettingKeys.DemoDataSeeded, cancellationToken))
        {
            return;
        }

        if (await context.Recipes.AnyAsync(cancellationToken)
            || await context.FamilyMembers.AnyAsync(cancellationToken))
        {
            _logger.LogInformation("Demo mode is on but the database already contains data; skipping demo seed");
            return;
        }

        // The imported library, household, history and seed marker commit together.
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        using var archive = OpenSeedArchive();
        var imported = await scope.ServiceProvider.GetRequiredService<IRecipeExchangeService>()
            .ImportAsync(archive, cancellationToken);
        if (imported.Skipped != 0 || imported.Created != DemoData.RecipeUrls.Count)
            throw new InvalidDataException("The demo backup did not restore the complete selected recipe library.");
        var recipesByUrl = await context.Recipes.ToDictionaryAsync(
            r => r.SourceUrl!, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var familyMembers = new List<FamilyMember>();
        var tagPool = new Dictionary<(string Name, DietaryTagKind Kind), DietaryTag>();
        foreach (var demoMember in DemoData.Members)
        {
            var member = new FamilyMember
            {
                Id = Guid.NewGuid(),
                Name = demoMember.Name,
                PreferenceNotes = demoMember.PreferenceNotes
            };
            AddTags(context, member, demoMember.AllergyTags, DietaryTagKind.Allergy, tagPool);
            AddTags(context, member, demoMember.DietTags, DietaryTagKind.Diet, tagPool);
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

        foreach (var demoCollection in DemoData.Collections)
        {
            var cookbook = new Cookbook { Id = Guid.NewGuid(), Name = demoCollection.Name };
            foreach (var url in demoCollection.RecipeUrls)
            {
                if (recipesByUrl.TryGetValue(url, out var recipe))
                {
                    cookbook.Entries.Add(new CookbookEntry { Cookbook = cookbook, RecipeId = recipe.Id });
                }
            }

            context.Cookbooks.Add(cookbook);
        }

        SeedPastMeals(context, recipesByUrl.Values.ToList(), familyMembers);

        context.UserSettings.Add(new UserSetting { Key = UserSettingKeys.DemoDataSeeded, Value = "true" });
        await context.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Demo data seeded: {RecipeCount} recipes, {MemberCount} family members",
            recipesByUrl.Count, DemoData.Members.Count);
    }

    /// <summary>Links a member to dietary tags, sharing tag entities across members</summary>
    private static void AddTags(
        DishhiveDbContext context, FamilyMember member, IReadOnlyList<string> names,
        DietaryTagKind kind, Dictionary<(string Name, DietaryTagKind Kind), DietaryTag> tagPool)
    {
        foreach (var name in names)
        {
            var key = (name.ToLowerInvariant(), kind);
            if (!tagPool.TryGetValue(key, out var tag))
            {
                tag = new DietaryTag { Id = Guid.NewGuid(), Name = name, Kind = kind };
                context.DietaryTags.Add(tag);
                tagPool[key] = tag;
            }

            member.DietaryTags.Add(new FamilyMemberDietaryTag
            {
                FamilyMember = member,
                DietaryTag = tag,
                // Same preset seeding a member saved through the API gets
                ExcludedClasses = [.. DietaryTagPresets.Resolve(name, kind)]
            });
        }
    }

    /// <summary>
    /// Two weeks of past demo dinners with eaten marks and ratings, so the history,
    /// statistics and AI suggestion features have data to show. Deterministic: same
    /// dishes and ratings on every fresh seed.
    /// </summary>
    private void SeedPastMeals(
        DishhiveDbContext context, IReadOnlyList<Recipe> recipes, IReadOnlyList<FamilyMember> members)
    {
        if (recipes.Count == 0 || members.Count == 0)
        {
            return;
        }

        var today = _clock.Today;

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

    internal static Stream OpenSeedArchive()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(SeedResourceSuffix))
            ?? throw new InvalidOperationException(
                $"Embedded resource '{SeedResourceSuffix}' not found in assembly");

        return assembly.GetManifestResourceStream(resourceName)!;
    }
}
