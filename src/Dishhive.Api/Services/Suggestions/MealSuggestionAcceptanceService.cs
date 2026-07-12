using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Dishhive.Api.Services.Suggestions;

public class MealSuggestionAcceptanceService(
    DishhiveDbContext context,
    IRecipeImportService importService,
    FreezerAvailabilityService freezerAvailability,
    ILogger<MealSuggestionAcceptanceService> logger)
{
    public async Task<AcceptMealSuggestionsResponseDto> AcceptAsync(
        AcceptMealSuggestionsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var attendeeIds = await context.FamilyMembers
            .AsNoTracking()
            .Where(member => member.IsActive && !member.IsGuest)
            .Select(member => member.Id)
            .ToListAsync(cancellationToken);
        var results = new List<AcceptMealSuggestionResultDto>();
        foreach (var item in request.Suggestions.DistinctBy(item => item.Id))
        {
            results.Add(await AcceptOneAsync(request.BatchId, item, attendeeIds, cancellationToken));
        }

        return new AcceptMealSuggestionsResponseDto { Results = results };
    }

    private async Task<AcceptMealSuggestionResultDto> AcceptOneAsync(
        Guid batchId,
        AcceptMealSuggestionDto item,
        IReadOnlyList<Guid> attendeeIds,
        CancellationToken cancellationToken)
    {
        var suggestionKey = $"{batchId:N}:{item.Id:N}";
        var existing = await context.PlannedMeals
            .AsNoTracking()
            .FirstOrDefaultAsync(meal => meal.SuggestionKey == suggestionKey, cancellationToken);
        if (existing != null)
        {
            return Result(item, "alreadyApplied", existing.Id, existing.RecipeId);
        }

        var requestedAttendees = item.AttendeeIds.Count == 0
            ? attendeeIds.ToList()
            : item.AttendeeIds.Where(attendeeIds.Contains).Distinct().ToList();
        if (item.AttendeeIds.Count > 0 && requestedAttendees.Count == 0)
        {
            return Result(item, "planningFailed", error: "None of the selected attendees are active household members.");
        }

        var quantity = Math.Max(1, item.FreezyItemQuantity);
        if (!string.IsNullOrWhiteSpace(item.FreezyItemRef))
        {
            var available = await freezerAvailability.GetAvailableAsync(cancellationToken);
            var stock = available.FirstOrDefault(candidate => candidate.Id == item.FreezyItemRef);
            if (stock == null || stock.Quantity < quantity)
            {
                return Result(item, "stockUnavailable", error: "The selected freezer item is no longer available in the requested quantity.");
            }
        }

        Recipe? recipe = null;
        if (item.RecipeId.HasValue)
        {
            recipe = await context.Recipes.FindAsync([item.RecipeId.Value], cancellationToken);
            if (recipe == null)
            {
                return Result(item, "planningFailed", error: "The linked recipe no longer exists.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(item.SourceUrl))
        {
            try
            {
                recipe = await importService.ImportAsync(item.SourceUrl, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex,
                    "Could not import accepted suggestion {SuggestionId} from {Url}",
                    item.Id, item.SourceUrl);
                return Result(item, "importFailed", error: ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Could not import accepted suggestion {SuggestionId} from {Url}",
                    item.Id, item.SourceUrl);
                return Result(item, "importFailed", error: ex.Message);
            }
        }

        IDbContextTransaction? transaction = null;
        if (context.Database.IsRelational())
        {
            transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        }

        await using (transaction)
        {
            try
            {
                var ideas = await context.PlannedMeals
                    .Where(meal => meal.Date == item.Date
                        && meal.MealType == item.MealType
                        && meal.Course == item.Course
                        && meal.VagueInstruction != null
                        && meal.DishName == null
                        && meal.RecipeId == null)
                    .ToListAsync(cancellationToken);
                context.PlannedMeals.RemoveRange(ideas);

                var meal = new PlannedMeal
                {
                    Date = item.Date,
                    MealType = item.MealType,
                    Course = item.Course,
                    RecipeId = recipe?.Id,
                    DishName = recipe?.Title ?? item.DishName.Trim(),
                    FreezyItemRef = string.IsNullOrWhiteSpace(item.FreezyItemRef) ? null : item.FreezyItemRef.Trim(),
                    FreezyItemQuantity = string.IsNullOrWhiteSpace(item.FreezyItemRef) ? 0 : quantity,
                    SuggestionKey = suggestionKey,
                    Attendees = requestedAttendees
                        .Select(memberId => new PlannedMealAttendee { FamilyMemberId = memberId })
                        .ToList()
                };
                context.PlannedMeals.Add(meal);
                await context.SaveChangesAsync(cancellationToken);
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return Result(item, "created", meal.Id, meal.RecipeId);
            }
            catch (DbUpdateException ex)
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                context.ChangeTracker.Clear();
                existing = await context.PlannedMeals
                    .AsNoTracking()
                    .FirstOrDefaultAsync(meal => meal.SuggestionKey == suggestionKey, cancellationToken);
                if (existing != null)
                {
                    return Result(item, "alreadyApplied", existing.Id, existing.RecipeId);
                }

                logger.LogWarning(ex, "Could not persist accepted suggestion {SuggestionId}", item.Id);
                return Result(item, "planningFailed", recipeId: recipe?.Id, error: "The planned meal could not be saved.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                context.ChangeTracker.Clear();
                logger.LogWarning(ex, "Could not apply accepted suggestion {SuggestionId}", item.Id);
                return Result(item, "planningFailed", recipeId: recipe?.Id, error: "The suggestion could not be applied.");
            }
        }
    }

    private static AcceptMealSuggestionResultDto Result(
        AcceptMealSuggestionDto item,
        string status,
        Guid? plannedMealId = null,
        Guid? recipeId = null,
        string? error = null) => new()
    {
        SuggestionId = item.Id,
        Status = status,
        DishName = item.DishName,
        PlannedMealId = plannedMealId,
        RecipeId = recipeId,
        Error = error
    };
}
