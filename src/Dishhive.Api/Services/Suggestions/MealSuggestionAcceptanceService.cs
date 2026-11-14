using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Localization;
using Dishhive.Api.Services.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;

namespace Dishhive.Api.Services.Suggestions;

public class MealSuggestionAcceptanceService(
    DishhiveDbContext context,
    IRecipeImportService importService,
    FreezerAvailabilityService freezerAvailability,
    AttendancePolicy attendancePolicy,
    UserMessageLocalizer messages,
    ILogger<MealSuggestionAcceptanceService> logger)
{
    public async Task StoreBatchAsync(
        Guid batchId,
        PlanCompletionStatus status,
        IReadOnlyList<MealSuggestionDto> suggestions,
        CancellationToken cancellationToken = default,
        PlanningIntent? intent = null,
        IReadOnlyList<FrozenItem>? freezerItems = null,
        string? operationId = null)
    {
        var recipeIds = suggestions.Where(item => item.RecipeId.HasValue).Select(item => item.RecipeId!.Value).ToList();
        var recipes = await context.Recipes.AsNoTracking().Include(recipe => recipe.Ingredients)
            .Include(recipe => recipe.Steps).Include(recipe => recipe.DietaryFacts)
            .Where(recipe => recipeIds.Contains(recipe.Id)).ToDictionaryAsync(recipe => recipe.Id, cancellationToken);
        var members = await context.FamilyMembers.AsNoTracking()
            .Include(member => member.DietaryTags).ThenInclude(tag => tag.DietaryTag)
            .ToListAsync(cancellationToken);
        var batch = new MealSuggestionBatch
        {
            Id = batchId,
            Status = status.ToString().ToLowerInvariant(),
            OperationId = operationId,
            Candidates = suggestions.Select(item => new MealSuggestionCandidate
            {
                Id = item.Id,
                RecipeId = item.RecipeId,
                RecipeRevision = SuggestionCandidateVersion.LocalRevision(item.CandidateVersion),
                FactsConfirmationVersion = item.RecipeId is { } recipeId
                    ? recipes.GetValueOrDefault(recipeId)?.FactsConfirmationVersion : null,
                ReviewSnapshotJson = JsonSerializer.Serialize(new SuggestionReviewSnapshot(
                    item.AttendeeIds,
                    SuggestionReviewSnapshot.Fingerprint(members.Where(member => item.AttendeeIds.Count == 0
                        ? member.IsActive && !member.IsGuest : item.AttendeeIds.Contains(member.Id))),
                    item.RecipeId is { } localId && recipes.TryGetValue(localId, out var recipe)
                        ? SuggestionReviewSnapshot.Fingerprint(recipe) : null,
                    intent?.Constraints.Where(constraint => item.ConstraintIds.Contains(constraint.Id)).ToList() ?? [],
                    item.ContainsClasses, item.FactsAssessed,
                    freezerItems?.FirstOrDefault(stock => stock.Id == item.FreezyItemRef) is { } freezerItem
                        ? FreezerReservationService.Fingerprint(freezerItem) : null)),
                DishName = item.DishName,
                SourceUrl = item.SourceUrl,
                CandidateVersion = item.CandidateVersion ?? string.Empty,
                FreezyItemRef = item.FreezyItemRef,
                FreezyItemQuantity = item.FreezyItemQuantity,
                Date = item.Date,
                MealType = item.MealType,
                Course = item.Course
            }).ToList()
        };
        context.MealSuggestionBatches.Add(batch);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AcceptMealSuggestionsResponseDto> AcceptAsync(
        AcceptMealSuggestionsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AcceptMealSuggestionResultDto>();
        foreach (var item in request.Suggestions.DistinctBy(item => item.Id))
        {
            results.Add(await AcceptOneAsync(request.BatchId, item, cancellationToken));
        }

        try
        {
            await RecordReviewOutcomesAsync(request, results, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Review telemetry is useful, but it must not turn an already committed
            // meal acceptance into an apparent failure that callers may retry.
            context.ChangeTracker.Clear();
            logger.LogWarning(ex, "Could not persist review outcomes for suggestion batch {BatchId}",
                request.BatchId);
        }

        return new AcceptMealSuggestionsResponseDto { Results = results };
    }

    private async Task RecordReviewOutcomesAsync(
        AcceptMealSuggestionsRequestDto request,
        IReadOnlyList<AcceptMealSuggestionResultDto> results,
        CancellationToken cancellationToken)
    {
        var resultById = results.ToDictionary(result => result.SuggestionId);
        var rejectedIds = request.RejectedSuggestionIds.ToHashSet();
        var candidateIds = resultById.Keys.Concat(rejectedIds).Distinct().ToList();
        var candidates = await context.MealSuggestionCandidates
            .Include(candidate => candidate.Batch)
            .Where(candidate => candidate.BatchId == request.BatchId && candidateIds.Contains(candidate.Id))
            .ToListAsync(cancellationToken);
        var submitted = request.Suggestions.DistinctBy(item => item.Id).ToDictionary(item => item.Id);
        var recipeIds = candidates.Where(candidate => candidate.RecipeId != null).Select(candidate => candidate.RecipeId!.Value).ToList();
        var recipes = await context.Recipes.AsNoTracking().Include(recipe => recipe.Ingredients)
            .Include(recipe => recipe.Steps).Include(recipe => recipe.DietaryFacts)
            .Where(recipe => recipeIds.Contains(recipe.Id)).ToDictionaryAsync(recipe => recipe.Id, cancellationToken);
        var members = await context.FamilyMembers.AsNoTracking().Include(member => member.DietaryTags)
            .ThenInclude(tag => tag.DietaryTag).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var collectionResolver = new CollectionMentionResolver(context, new AutoCollectionProvider(context));
        var collectionCache = new Dictionary<string, CollectionConstraint?>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var changed = new List<string>();
            var inputChanges = new List<string>();
            var snapshot = candidate.ReviewSnapshotJson == null ? null : JsonSerializer.Deserialize<SuggestionReviewSnapshot>(candidate.ReviewSnapshotJson);
            if (submitted.TryGetValue(candidate.Id, out var item))
            {
                if (candidate.Date != item.Date) changed.Add("date");
                if (candidate.MealType != item.MealType) changed.Add("mealType");
                if (candidate.Course != item.Course) changed.Add("course");
                if (candidate.RecipeId != item.RecipeId) changed.Add("recipeId");
                if (candidate.DishName != item.DishName) changed.Add("dishName");
                if (candidate.SourceUrl != item.SourceUrl) changed.Add("sourceUrl");
                if (candidate.CandidateVersion != (item.CandidateVersion ?? "")) changed.Add("candidateVersion");
                if (candidate.FreezyItemRef != item.FreezyItemRef || candidate.FreezyItemQuantity != item.FreezyItemQuantity) changed.Add("freezerReservation");
                if (snapshot != null && !snapshot.AttendeeIds.Order().SequenceEqual(item.AttendeeIds.Distinct().Order())) changed.Add("attendees");
            }
            recipes.TryGetValue(candidate.RecipeId ?? Guid.Empty, out var recipe);
            if (candidate.RecipeId.HasValue)
            {
                if (recipe == null) inputChanges.Add("recipeDeleted");
                else
                {
                    if (candidate.RecipeRevision.HasValue && candidate.RecipeRevision != recipe.Revision) inputChanges.Add("recipeRevision");
                    if (candidate.FactsConfirmationVersion.HasValue && candidate.FactsConfirmationVersion != recipe.FactsConfirmationVersion) inputChanges.Add("factsConfirmationVersion");
                    if (snapshot?.RecipeFingerprint is { } fingerprint && fingerprint != SuggestionReviewSnapshot.Fingerprint(recipe)) inputChanges.Add("recipeContentOrFacts");
                }
            }
            if (snapshot != null && snapshot.AttendanceFingerprint != SuggestionReviewSnapshot.Fingerprint(members.Where(member =>
                snapshot.AttendeeIds.Count == 0 ? member.IsActive && !member.IsGuest : snapshot.AttendeeIds.Contains(member.Id))))
                inputChanges.Add("household");
            if (snapshot != null && recipe != null)
                foreach (var id in snapshot.Constraints.SelectMany(constraint => constraint.RequiredCollectionIds).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!collectionCache.TryGetValue(id, out var collection))
                        collectionCache[id] = collection = await collectionResolver.ResolveByIdAsync(id, cancellationToken);
                    if (collection == null || !collection.RecipeIds.Contains(recipe.Id))
                    {
                        inputChanges.Add("collectionMembership");
                        break;
                    }
                }
            if (resultById.TryGetValue(candidate.Id, out var result))
            {
                if (result.Code == "freezer_details_changed") inputChanges.Add("freezerDetails");
                if (result.Status == "stockUnavailable") inputChanges.Add("freezerAvailability");
                candidate.ReviewOutcome = result.Status is "created" or "alreadyApplied"
                    ? "accepted"
                    : result.Status;
                candidate.WasModified |= changed.Count > 0;
            }
            else if (rejectedIds.Contains(candidate.Id))
            {
                candidate.ReviewOutcome = "rejected";
            }
            candidate.ResolvedAt ??= now;
            context.AiReviewEvents.Add(new AiReviewEvent
            {
                OccurredAt = now, Task = "planning.generate", Kind = "suggestionReviewed",
                OperationId = candidate.Batch?.OperationId, RecipeId = candidate.RecipeId,
                BatchId = request.BatchId, SuggestionId = candidate.Id, Outcome = candidate.ReviewOutcome,
                SourceRevision = candidate.RecipeRevision, ObservedRevision = recipe?.Revision,
                SourceConfirmationVersion = candidate.FactsConfirmationVersion, ObservedConfirmationVersion = recipe?.FactsConfirmationVersion,
                ChangedFieldsJson = JsonSerializer.Serialize(changed), InputChangesJson = JsonSerializer.Serialize(inputChanges)
            });
        }
        if (candidates.Count > 0) await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<AcceptMealSuggestionResultDto> AcceptOneAsync(
        Guid batchId,
        AcceptMealSuggestionDto item,
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

        var reviewed = await context.MealSuggestionCandidates.AsNoTracking()
            .Include(candidate => candidate.Batch)
            .FirstOrDefaultAsync(candidate => candidate.BatchId == batchId && candidate.Id == item.Id,
                cancellationToken);
        if (reviewed?.Batch is null || reviewed.Batch.ExpiresAt <= DateTime.UtcNow)
            return Result(item, "needsReview", error: await messages.GetAsync("suggestionAcceptance.expiredReview", cancellationToken));
        if (reviewed.Date != item.Date || reviewed.MealType != item.MealType
            || reviewed.Course != item.Course
            || reviewed.RecipeId != item.RecipeId
            || reviewed.FreezyItemRef != item.FreezyItemRef
            || reviewed.FreezyItemQuantity != item.FreezyItemQuantity
            || !string.Equals(reviewed.DishName, item.DishName, StringComparison.Ordinal)
            || !string.Equals(reviewed.SourceUrl, item.SourceUrl, StringComparison.Ordinal)
            || !string.Equals(reviewed.CandidateVersion, item.CandidateVersion ?? string.Empty,
                StringComparison.Ordinal))
            return Result(item, "needsReview", error: await messages.GetAsync("suggestionAcceptance.submittedChanged", cancellationToken));

        var snapshot = reviewed.ReviewSnapshotJson is null ? null
            : JsonSerializer.Deserialize<SuggestionReviewSnapshot>(reviewed.ReviewSnapshotJson);
        if (snapshot is null)
            return Result(item, "needsReview", error: await messages.GetAsync("suggestionAcceptance.legacyReview", cancellationToken));
        if (!snapshot.AttendeeIds.Order().SequenceEqual(item.AttendeeIds.Distinct().Order()))
            return Result(item, "needsReview", error: await messages.GetAsync("suggestionAcceptance.attendanceChanged", cancellationToken));

        var attendance = await attendancePolicy.ResolveAsync(item.AttendeeIds, cancellationToken);
        var requestedAttendees = attendance.AttendeeIds.ToList();
        if (!attendance.UsedHouseholdDefault && attendance.InvalidIds.Count > 0)
        {
            return Result(item, "planningFailed", error: await messages.GetAsync(
                "suggestionAcceptance.noActiveAttendees", cancellationToken));
        }

        var quantity = Math.Max(1, item.FreezyItemQuantity);
        string? stockFingerprint = null;

        Recipe? recipe = null;
        if (item.RecipeId.HasValue)
        {
            recipe = await context.Recipes.AsNoTracking().Include(recipe => recipe.Ingredients)
                .Include(recipe => recipe.Steps).Include(recipe => recipe.DietaryFacts)
                .FirstOrDefaultAsync(recipe => recipe.Id == item.RecipeId.Value, cancellationToken);
            if (recipe == null)
            {
                return Result(item, "planningFailed", error: await messages.GetAsync(
                    "suggestionAcceptance.recipeMissing", cancellationToken));
            }
            if (reviewed.RecipeRevision != recipe.Revision
                || (reviewed.FactsConfirmationVersion.HasValue && reviewed.FactsConfirmationVersion != recipe.FactsConfirmationVersion)
                || (snapshot?.RecipeFingerprint is { } fingerprint && fingerprint != SuggestionReviewSnapshot.Fingerprint(recipe))
                || (item.CandidateVersion?.Split(':') is { Length: >= 3 } parts
                    && parts[0] == "local" && parts[2] != recipe.FactsConfirmationVersion.ToString())
                || (item.CandidateVersion?.Split(':').Length == 4
                    && item.CandidateVersion != SuggestionCandidateVersion.ForLocal(recipe)))
                return Result(item, "needsReview", recipeId: recipe.Id,
                    error: await messages.GetAsync("suggestionAcceptance.savedRecipeChanged", cancellationToken));
        }
        else if (!string.IsNullOrWhiteSpace(item.SourceUrl))
        {
            try
            {
                recipe = await importService.ImportAsync(item.SourceUrl, cancellationToken);
                var importedVersion = SuggestionCandidateVersion.ForContent(recipe.OriginalTitle ?? recipe.Title,
                    recipe.Ingredients.OrderBy(ingredient => ingredient.SortOrder).Select(ingredient => ingredient.OriginalText),
                    steps: recipe.Steps.OrderBy(step => step.StepNumber).Select(step => step.OriginalInstruction ?? step.Instruction),
                    totalTimeMinutes: recipe.TotalTimeMinutes);
                if (!string.IsNullOrEmpty(reviewed.CandidateVersion)
                    && !string.Equals(importedVersion, reviewed.CandidateVersion, StringComparison.Ordinal))
                    return Result(item, "needsReview", recipeId: recipe.Id,
                        error: await messages.GetAsync("suggestionAcceptance.importedRecipeChanged", cancellationToken));
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex,
                    "Could not import accepted suggestion {SuggestionId} from {Url}",
                    item.Id, item.SourceUrl);
                return Result(item, "importFailed", error: await messages.GetAsync(
                    "suggestionAcceptance.importFailed", cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Could not import accepted suggestion {SuggestionId} from {Url}",
                    item.Id, item.SourceUrl);
                return Result(item, "importFailed", error: await messages.GetAsync(
                    "suggestionAcceptance.importFailed", cancellationToken));
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
                if (!string.IsNullOrWhiteSpace(item.FreezyItemRef))
                {
                    if (context.Database.IsNpgsql())
                    {
                        // Serialize all reservations for one external stock id. The
                        // second transaction re-reads reservations after the first commits.
                        await context.Database.ExecuteSqlInterpolatedAsync(
                            $"SELECT pg_advisory_xact_lock(hashtext({item.FreezyItemRef.Trim()}))",
                            cancellationToken);
                    }
                    existing = await context.PlannedMeals.AsNoTracking()
                        .FirstOrDefaultAsync(meal => meal.SuggestionKey == suggestionKey, cancellationToken);
                    if (existing is not null)
                        return Result(item, "alreadyApplied", existing.Id, existing.RecipeId);
                    var available = await freezerAvailability.GetAvailableAsync(cancellationToken);
                    var stock = available.FirstOrDefault(candidate => candidate.Id == item.FreezyItemRef);
                    if (stock == null || stock.Quantity < quantity)
                    {
                        if (transaction != null) await transaction.RollbackAsync(cancellationToken);
                        return Result(item, "stockUnavailable", error: await messages.GetAsync(
                            "suggestionAcceptance.stockUnavailable", cancellationToken));
                    }
                    if (snapshot?.FreezerFingerprint is { } reviewedStock)
                    {
                        if (reviewedStock != FreezerReservationService.Fingerprint(stock))
                            return Result(item, "needsReview", error: await messages.GetAsync("suggestionAcceptance.freezerDetailsChanged", cancellationToken), code: "freezer_details_changed");
                        stockFingerprint = FreezerReservationService.Fingerprint(stock);
                    }
                }
                var compatibilityError = await RevalidateAsync(recipe, reviewed, snapshot, requestedAttendees, cancellationToken);
                if (compatibilityError is not null)
                    return Result(item, "needsReview", recipeId: recipe?.Id, error: compatibilityError);
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
                    FreezyItemFingerprint = stockFingerprint,
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
                return Result(item, "planningFailed", recipeId: recipe?.Id, error: await messages.GetAsync(
                    "suggestionAcceptance.saveFailed", cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                context.ChangeTracker.Clear();
                logger.LogWarning(ex, "Could not apply accepted suggestion {SuggestionId}", item.Id);
                return Result(item, "planningFailed", recipeId: recipe?.Id, error: await messages.GetAsync(
                    "suggestionAcceptance.applyFailed", cancellationToken));
            }
        }
    }

    private async Task<string?> RevalidateAsync(
        Recipe? recipe, MealSuggestionCandidate reviewed, SuggestionReviewSnapshot? snapshot,
        IReadOnlyList<Guid> attendeeIds, CancellationToken cancellationToken)
    {
        var members = await context.FamilyMembers.AsNoTracking()
            .Include(member => member.DietaryTags).ThenInclude(tag => tag.DietaryTag)
            .Where(member => attendeeIds.Contains(member.Id)).ToListAsync(cancellationToken);
        if (members.Count != attendeeIds.Count || members.Any(member => !member.IsActive)
            || (snapshot is not null && snapshot.AttendanceFingerprint != SuggestionReviewSnapshot.Fingerprint(members)))
            return await messages.GetAsync("suggestionAcceptance.householdChanged", cancellationToken);

        if (recipe is not null)
        {
            // Hold a shared recipe lock through the meal commit. A concurrent edit,
            // confirmation or enrichment must finish before this read or wait for us.
            var query = context.Database.IsNpgsql()
                ? context.Recipes.FromSqlInterpolated($"SELECT * FROM \"Recipes\" WHERE \"Id\" = {recipe.Id} FOR SHARE")
                : context.Recipes.Where(value => value.Id == recipe.Id);
            recipe = await query.AsNoTracking().Include(value => value.DietaryFacts)
                .Include(value => value.Ingredients).Include(value => value.Steps).FirstOrDefaultAsync(cancellationToken);
            if (recipe is null) return await messages.GetAsync("suggestionAcceptance.recipeRemoved", cancellationToken);
            if (snapshot?.RecipeFingerprint is { } fingerprint && fingerprint != SuggestionReviewSnapshot.Fingerprint(recipe))
                return await messages.GetAsync("suggestionAcceptance.savedRecipeChanged", cancellationToken);
        }

        var assessed = recipe is not null
            && recipe.DietaryFactsStatus is DietaryFactsStatus.AiDetected or DietaryFactsStatus.UserConfirmed
            && (recipe.DietaryFactsAssessedRevision is null || recipe.DietaryFactsAssessedRevision == recipe.Revision);
        var facts = recipe?.DietaryFacts.Select(fact => fact.IngredientClass).ToList() ?? [];
        if (recipe is not null && snapshot?.FactsAssessed == true && !assessed)
            return await messages.GetAsync("suggestionAcceptance.factsPending", cancellationToken);
        if (assessed && members.SelectMany(member => member.DietaryTags)
            .Where(tag => tag.DietaryTag?.Kind == DietaryTagKind.Allergy)
            .SelectMany(tag => tag.ExcludedClasses).Any(facts.Contains))
            return await messages.GetAsync("suggestionAcceptance.allergyConflict", cancellationToken);
        if (assessed && snapshot?.FactsAssessed == true && !snapshot.ContainsClasses.Order().SequenceEqual(
                IngredientClasses.ToNames(facts).Order()))
            return await messages.GetAsync("suggestionAcceptance.assessedClassesChanged", cancellationToken);

        if (snapshot?.Constraints.Count > 0)
        {
            var collections = new List<CollectionConstraint>();
            var resolver = new CollectionMentionResolver(context, new AutoCollectionProvider(context));
            foreach (var collectionId in snapshot.Constraints.SelectMany(constraint => constraint.RequiredCollectionIds)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (context.Database.IsNpgsql() && recipe is not null && Guid.TryParse(collectionId, out var cookbookId))
                    await context.CookbookEntries.FromSqlInterpolated(
                        $"SELECT * FROM \"CookbookEntries\" WHERE \"CookbookId\" = {cookbookId} AND \"RecipeId\" = {recipe.Id} FOR SHARE")
                        .AsNoTracking().ToListAsync(cancellationToken);
                var collection = await resolver.ResolveByIdAsync(collectionId, cancellationToken);
                if (collection is null) return await messages.GetAsync("suggestionAcceptance.collectionUnavailable", cancellationToken);
                collections.Add(collection);
            }
            var suggestion = new MealSuggestion
            {
                Date = reviewed.Date, MealType = reviewed.MealType, Course = reviewed.Course,
                DishName = recipe?.Title ?? reviewed.DishName, RecipeId = reviewed.RecipeId,
                SourceUrl = reviewed.SourceUrl, FreezyItemRef = reviewed.FreezyItemRef,
                AttendeeIds = attendeeIds, ConstraintIds = snapshot.Constraints.Select(constraint => constraint.Id).ToList(),
                ResolvedFactsAssessed = assessed, ResolvedContainsClasses = facts,
                ResolvedTotalTimeMinutes = recipe?.TotalTimeMinutes,
                CandidateVersion = reviewed.CandidateVersion,
                ExternalIngredients = reviewed.RecipeId.HasValue ? recipe?.Ingredients.Select(item => item.Name).ToList() ?? [] : []
            };
            var issue = PlanningIntentValidator.ValidateFinalItem(suggestion,
                new PlanningIntent { Constraints = snapshot.Constraints.ToList() },
                new MealSuggestionRequest
                {
                    DaysToFill = [reviewed.Date],
                    CollectionConstraints = collections,
                    Members = members.Select(member => new MemberProfile { Id = member.Id, Name = member.Name }).ToList()
                }).FirstOrDefault();
            if (issue is not null) return issue.DisplayText is { } text
                ? await messages.GetAsync(text, cancellationToken) : issue.Message;
        }
        return null;
    }

    private static AcceptMealSuggestionResultDto Result(
        AcceptMealSuggestionDto item,
        string status,
        Guid? plannedMealId = null,
        Guid? recipeId = null,
        string? error = null,
        string? code = null) => new()
    {
        SuggestionId = item.Id,
        Status = status,
        DishName = item.DishName,
        PlannedMealId = plannedMealId,
        RecipeId = recipeId,
        Error = error,
        Code = code
    };
}
