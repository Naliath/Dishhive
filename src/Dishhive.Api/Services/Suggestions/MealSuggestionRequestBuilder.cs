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

    private readonly DishhiveDbContext _context;
    private readonly IFreezyClient _freezyClient;

    public MealSuggestionRequestBuilder(DishhiveDbContext context, IFreezyClient freezyClient)
    {
        _context = context;
        _freezyClient = freezyClient;
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

        var knownRecipes = await _context.Recipes
            .AsNoTracking()
            .OrderBy(r => r.Title)
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
                DishName = m.DishName,
                VagueInstruction = m.VagueInstruction
            })
            .ToList();

        // Fill days without a dinner main, or with a vague-instruction-only dinner;
        // never propose over a concretely planned dish
        var daysToFill = Enumerable.Range(0, 7)
            .Select(weekStart.AddDays)
            .Where(date => !weekMeals.Any(m =>
                m.Date == date
                && m.MealType == MealType.Dinner
                && m.Course == Course.Main
                && (m.RecipeId != null || m.DishName != null)))
            .ToList();

        var frozenItems = await _freezyClient.GetFrozenItemsAsync(cancellationToken);

        return new MealSuggestionRequest
        {
            WeekStart = weekStart,
            Members = members.Select(m => new MemberProfile
            {
                Name = m.Name,
                Allergies = TagNames(m, DietaryTagKind.Allergy),
                Diets = TagNames(m, DietaryTagKind.Diet),
                PreferenceNotes = m.PreferenceNotes
            }).ToList(),
            Favorites = favorites,
            RecentDishes = recentDishes,
            KnownRecipes = knownRecipes,
            WeekPlan = weekPlan,
            DaysToFill = daysToFill,
            AvailableFrozenItems = frozenItems,
            Instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim()
        };
    }

    private static List<string> TagNames(FamilyMember member, DietaryTagKind kind) => member.DietaryTags
        .Where(link => link.DietaryTag != null && link.DietaryTag.Kind == kind)
        .Select(link => link.DietaryTag!.Name)
        .OrderBy(n => n)
        .ToList();
}
