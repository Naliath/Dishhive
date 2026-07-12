using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Freezy;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Suggestions;

/// <summary>
/// Assembles the planning context for meal suggestions: active household members,
/// favorites, 90 days of dish history with eaten/rating feedback, the recipe store,
/// the week's existing plan and expiring Freezy items.
/// </summary>
public class MealSuggestionRequestBuilder
{
    private const int HistoryDays = 90;

    /// <summary>
    /// Upper bound on recipe titles carried into the request after relevance
    /// ranking. The prompt trims further to the token budget; this just bounds the
    /// payload and the per-candidate allergen load for large libraries.
    /// </summary>
    private const int RecipeCandidateCap = 100;

    private readonly DishhiveDbContext _context;
    private readonly FreezerAvailabilityService _freezerAvailability;
    private readonly CollectionMentionResolver _mentionResolver;
    private readonly SourceMentionResolver _sourceMentionResolver;
    private readonly ILogger<MealSuggestionRequestBuilder> _logger;

    public MealSuggestionRequestBuilder(
        DishhiveDbContext context, FreezerAvailabilityService freezerAvailability,
        CollectionMentionResolver mentionResolver, SourceMentionResolver sourceMentionResolver,
        ILogger<MealSuggestionRequestBuilder> logger)
    {
        _context = context;
        _freezerAvailability = freezerAvailability;
        _mentionResolver = mentionResolver;
        _sourceMentionResolver = sourceMentionResolver;
        _logger = logger;
    }

    public async Task<MealSuggestionRequest> BuildAsync(
        DateOnly weekStart, IReadOnlyList<Guid> attendeeIds, string? instructions = null,
        CancellationToken cancellationToken = default)
    {
        var weekEnd = weekStart.AddDays(6);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var membersQuery = _context.FamilyMembers
            .AsNoTracking()
            .Include(m => m.DietaryTags).ThenInclude(t => t.DietaryTag)
            .Where(m => m.IsActive);
        membersQuery = attendeeIds.Count > 0
            ? membersQuery.Where(m => attendeeIds.Contains(m.Id))
            : membersQuery.Where(m => !m.IsGuest);
        var members = await membersQuery.OrderBy(m => m.Name).ToListAsync(cancellationToken);
        var memberIds = members.Select(m => m.Id).ToList();

        var favorites = await _context.FamilyMemberFavorites
            .AsNoTracking()
            .Where(f => memberIds.Contains(f.FamilyMemberId) && f.DishName != null)
            .Join(_context.FamilyMembers, f => f.FamilyMemberId, m => m.Id,
                (f, m) => new FavoriteDish { MemberName = m.Name, DishName = f.DishName! })
            .ToListAsync(cancellationToken);

        var historyStart = today.AddDays(-HistoryDays);
        var historyMeals = _context.PlannedMeals
            .AsNoTracking()
            .Where(m => m.Date >= historyStart && m.Date < today && m.DishName != null);

        var recentDishes = await historyMeals
            .GroupBy(m => m.DishName!)
            .Select(g => new DishHistoryEntry
            {
                DishName = g.Key,
                TimesPlanned = g.Count(),
                LastPlanned = g.Max(m => m.Date),
                TimesEaten = g.Count(m => m.Eaten == EatenStatus.Eaten)
            })
            .ToListAsync(cancellationToken);

        var ratingsByDish = await historyMeals
            .SelectMany(m => m.Ratings.Select(r => new { DishName = m.DishName!, r.Rating }))
            .GroupBy(x => x.DishName)
            .Select(g => new { DishName = g.Key, Average = g.Average(x => (double)x.Rating) })
            .ToDictionaryAsync(x => x.DishName, cancellationToken);

        recentDishes = recentDishes
            .Select(d => ratingsByDish.TryGetValue(d.DishName, out var agg)
                ? d with { AverageRating = Math.Round(agg.Average, 2) }
                : d)
            .OrderByDescending(d => d.LastPlanned)
            .ToList();

        var allRecipes = await _context.Recipes
            .AsNoTracking()
            .Select(r => new RecipeOption { Id = r.Id, Title = r.Title, Category = r.Category })
            .ToListAsync(cancellationToken);

        var weekMeals = await _context.PlannedMeals
            .AsNoTracking()
            .Where(m => m.Date >= weekStart && m.Date <= weekEnd)
            .ToListAsync(cancellationToken);

        var weekPlan = weekMeals
            .Select(m => new ExistingMeal
            {
                Date = m.Date,
                MealType = m.MealType,
                Course = m.Course,
                DishName = m.DishName,
                VagueInstruction = m.VagueInstruction
            })
            .ToList();

        // Fill days without a dinner main, or with a vague-instruction-only dinner;
        // never propose over a concretely planned dish or a day that has already passed
        var daysToFill = Enumerable.Range(0, 7)
            .Select(weekStart.AddDays)
            .Where(date => date >= today)
            .Where(date => !weekMeals.Any(m =>
                m.Date == date
                && m.MealType == MealType.Dinner
                && m.Course == Course.Main
                && (m.RecipeId != null || m.DishName != null)))
            .ToList();

        var frozenItems = await _freezerAvailability.GetAvailableAsync(cancellationToken);

        // Resolve #[Collection Name] references from the day instructions and the
        // global instructions into recipe-title constraints
        var mentionSources = weekPlan
            .Where(m => !string.IsNullOrWhiteSpace(m.VagueInstruction))
            .Select(m => ((DateOnly?)m.Date, m.VagueInstruction))
            .Append((null, instructions))
            .ToList();
        var lastPlannedByTitle = recentDishes
            .GroupBy(d => d.DishName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(d => d.LastPlanned), StringComparer.OrdinalIgnoreCase);
        var collectionConstraints = await _mentionResolver.ResolveAsync(
            mentionSources, lastPlannedByTitle, cancellationToken);

        // Resolve @[Source] references into website-host constraints for the LLM search tool
        var sourceConstraints = await _sourceMentionResolver.ResolveAsync(mentionSources, cancellationToken);

        // Rank recipes by planning relevance instead of sending an arbitrary
        // alphabetical slice: favorites, well-rated and collection-referenced
        // recipes rank highest; recently-eaten ones are pushed down for variety.
        // The prompt then trims this ranked list to the token budget.
        var favoriteTitles = favorites.Select(f => f.DishName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var constraintTitles = collectionConstraints
            .SelectMany(c => c.RecipeTitles)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ratingByTitle = recentDishes
            .Where(d => d.AverageRating.HasValue)
            .GroupBy(d => d.DishName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(d => d.AverageRating!.Value), StringComparer.OrdinalIgnoreCase);

        double Score(string title)
        {
            double score = 0;
            if (constraintTitles.Contains(title)) score += 1000; // keep referenced titles linkable
            if (favoriteTitles.Contains(title)) score += 100;
            if (ratingByTitle.TryGetValue(title, out var rating)) score += rating * 20;
            if (lastPlannedByTitle.TryGetValue(title, out var last))
            {
                var ageDays = today.DayNumber - last.DayNumber;
                score += ageDays < 14 ? -20 : ageDays < 30 ? 5 : 20;
            }
            else
            {
                score += 30; // not planned in the last 90 days → fresh variety candidate
            }
            return score;
        }

        var knownRecipes = allRecipes
            .OrderByDescending(r => Score(r.Title))
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Take(RecipeCandidateCap)
            .ToList();

        // Ingredient names (post-hoc substring net for unassessed recipes) and
        // assessed dietary facts for the ranked candidates only, in one round trip.
        // Scoped to the candidates so large libraries stay cheap.
        var rankedIds = knownRecipes.Select(r => r.Id).ToList();
        var candidateInfo = await _context.Recipes
            .AsNoTracking()
            .Where(r => rankedIds.Contains(r.Id))
            .Select(r => new
            {
                r.Id,
                Ingredients = r.Ingredients.Select(i => i.Name).ToList(),
                Assessed = r.DietaryFactsStatus != DietaryFactsStatus.Unassessed,
                Facts = r.DietaryFacts.Select(f => f.IngredientClass).ToList()
            })
            .ToListAsync(cancellationToken);

        var recipeAllergens = candidateInfo.ToDictionary(
            x => x.Id,
            x => new RecipeAllergenInfo { Ingredients = x.Ingredients });

        var factsById = candidateInfo
            .Where(x => x.Assessed)
            .ToDictionary(x => x.Id, x => x.Facts);
        knownRecipes = knownRecipes
            .Select(r => factsById.TryGetValue(r.Id, out var classes)
                ? r with { ContainsClasses = classes, FactsAssessed = true }
                : r)
            .ToList();

        // Assessed candidates conflicting with an attending member's ALLERGY
        // exclusions are excluded from planning (prompt block + rules fallback).
        // Diets never hard-exclude — they only annotate and warn. The recipes stay
        // in KnownRecipes so titles remain resolvable; consumers filter by id.
        var allergyClasses = members
            .SelectMany(m => m.DietaryTags)
            .Where(link => link.DietaryTag?.Kind == DietaryTagKind.Allergy)
            .SelectMany(link => link.ExcludedClasses)
            .ToHashSet();
        var allergyExcludedIds = knownRecipes
            .Where(r => r.FactsAssessed && r.ContainsClasses.Any(allergyClasses.Contains))
            .Select(r => r.Id)
            .ToHashSet();

        // A #[Collection]-referenced title that is allergy-excluded must not stay a
        // hard "pick from this list" option; drop it from the constraint (visibly).
        if (allergyExcludedIds.Count > 0)
        {
            var excludedTitles = knownRecipes
                .Where(r => allergyExcludedIds.Contains(r.Id))
                .Select(r => r.Title)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            collectionConstraints = collectionConstraints
                .Select(c =>
                {
                    var kept = c.RecipeTitles.Where(t => !excludedTitles.Contains(t)).ToList();
                    if (kept.Count < c.RecipeTitles.Count)
                    {
                        _logger.LogInformation(
                            "Dropped {Count} recipe(s) from #[{Collection}] for household allergies",
                            c.RecipeTitles.Count - kept.Count, c.Name);
                    }
                    return c with { RecipeTitles = kept };
                })
                .ToList();
        }

        return new MealSuggestionRequest
        {
            WeekStart = weekStart,
            Members = members.Select(m => new MemberProfile
            {
                Id = m.Id,
                Name = m.Name,
                Allergies = TagProfiles(m, DietaryTagKind.Allergy),
                Diets = TagProfiles(m, DietaryTagKind.Diet),
                PreferenceNotes = m.PreferenceNotes
            }).ToList(),
            Favorites = favorites,
            RecentDishes = recentDishes,
            KnownRecipes = knownRecipes,
            WeekPlan = weekPlan,
            DaysToFill = daysToFill,
            AvailableFrozenItems = frozenItems,
            Instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim(),
            CollectionConstraints = collectionConstraints,
            SourceConstraints = sourceConstraints,
            RecipeAllergens = recipeAllergens,
            AllergyExcludedRecipeIds = allergyExcludedIds
        };
    }

    private static List<DietaryTagProfile> TagProfiles(FamilyMember member, DietaryTagKind kind) => member.DietaryTags
        .Where(link => link.DietaryTag != null && link.DietaryTag.Kind == kind)
        .OrderBy(link => link.DietaryTag!.Name)
        .Select(link => new DietaryTagProfile
        {
            Name = link.DietaryTag!.Name,
            ExcludedClasses = link.ExcludedClasses
        })
        .ToList();
}
