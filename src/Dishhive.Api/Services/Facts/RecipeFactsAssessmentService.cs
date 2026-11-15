using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Localization;
using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Import;
using Microsoft.EntityFrameworkCore;
using System.Threading.Channels;

namespace Dishhive.Api.Services.Facts;

public record RecipeFactsQueueStatus(int QueueDepth, bool Running, string? LastError);

/// <summary>
/// Durable recipe enrichment worker. Database rows are the queue; the channel is
/// only a wake-up signal. Work is performed from an immutable snapshot and applied
/// conditionally to the captured recipe and confirmation revisions.
/// </summary>
public class RecipeFactsAssessmentService : BackgroundService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRecipeFactsExtractor _extractor;
    private readonly IRecipeLocalizationService _localizer;
    private readonly SupportedLanguageCatalog _languages;
    private readonly ILogger<RecipeFactsAssessmentService> _logger;
    private volatile bool _processing;
    private volatile string? _lastError;
    private int _knownDepth;

    public RecipeFactsAssessmentService(
        IServiceScopeFactory scopeFactory,
        IRecipeFactsExtractor extractor,
        IRecipeLocalizationService localizer,
        SupportedLanguageCatalog languages,
        ILogger<RecipeFactsAssessmentService> logger)
    {
        _scopeFactory = scopeFactory;
        _extractor = extractor;
        _localizer = localizer;
        _languages = languages;
        _logger = logger;
    }

    public RecipeFactsAssessmentService(
        IServiceScopeFactory scopeFactory,
        IRecipeFactsExtractor extractor,
        ILogger<RecipeFactsAssessmentService> logger)
        : this(scopeFactory, extractor, new NoOpRecipeLocalizationService(),
            new SupportedLanguageCatalog(), logger) { }

    public RecipeFactsQueueStatus Status =>
        new(Volatile.Read(ref _knownDepth), _processing || Volatile.Read(ref _knownDepth) > 0, _lastError);

    public async Task<bool> EnqueueAsync(
        Guid recipeId,
        bool overwriteUserConfirmed = false,
        string? operationId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_extractor.IsAvailable && !_localizer.IsAvailable) return false;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var recipe = await context.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId, cancellationToken);
        if (recipe is null) return false;

        if (!await StageAsync(context, recipe, overwriteUserConfirmed, operationId, cancellationToken)) return false;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The mutation that won the race owns the work for its new revision.
            return false;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException
            { SqlState: "23505", ConstraintName: "IX_RecipeEnrichmentJobs_RecipeId_Task" })
        {
            // A concurrent enqueue already persisted the pending work.
            return false;
        }
        await RefreshDepthAsync(cancellationToken);
        _wake.Writer.TryWrite(true);
        return true;
    }

    /// <summary>Stage jobs in the mutation's context, so source and work commit together.</summary>
    public async Task<bool> StageAsync(
        DishhiveDbContext context, Recipe recipe, bool overwriteUserConfirmed = false,
        string? operationId = null, CancellationToken cancellationToken = default)
    {
        if (!_extractor.IsAvailable && !_localizer.IsAvailable) return false;

        var trimmedOperationId = operationId?.Trim();
        var effectiveOperationId = string.IsNullOrWhiteSpace(trimmedOperationId)
            ? $"enrich-{Guid.NewGuid():N}"[..19]
            : trimmedOperationId[..Math.Min(64, trimmedOperationId.Length)];
        var added = false;

        if (_extractor.IsAvailable
            && (overwriteUserConfirmed || recipe.DietaryFactsStatus != DietaryFactsStatus.UserConfirmed))
        {
            if (recipe.DietaryFactsStatus is DietaryFactsStatus.AiDetected or DietaryFactsStatus.UserConfirmed)
            {
                await context.Entry(recipe).Collection(value => value.DietaryFacts).LoadAsync(cancellationToken);
                await context.Entry(recipe).Collection(value => value.DietaryFactsHistory).LoadAsync(cancellationToken);
                RecipeFactsStateService.Archive(recipe);
            }
            added |= await CoalesceAsync(context, recipe, RecipeEnrichmentTask.Facts,
                null, effectiveOperationId, cancellationToken);
            recipe.DietaryFactsStatus = DietaryFactsStatus.Pending;
        }

        if (_localizer.IsAvailable && RecipeImportProvenance.CanTranslate(recipe))
        {
            var preferred = await context.UserSettings.AsNoTracking().FirstOrDefaultAsync(
                s => s.Key == UserSettingKeys.PreferredLanguage, cancellationToken);
            var enabled = await context.UserSettings.AsNoTracking().FirstOrDefaultAsync(
                s => s.Key == UserSettingKeys.TranslateImportedRecipes, cancellationToken);
            var target = _languages.Find(preferred?.Value)?.Code ?? _languages.DefaultCode;
            if (enabled?.Value == "true" && recipe.ContentLanguage != target)
            {
                added |= await CoalesceAsync(context, recipe, RecipeEnrichmentTask.Translation,
                    target, effectiveOperationId, cancellationToken);
            }
        }

        return added;
    }

    public async Task<int> EnqueueManyAsync(
        IEnumerable<Guid> recipeIds,
        CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var id in recipeIds)
        {
            if (await EnqueueAsync(id, cancellationToken: cancellationToken)) count++;
        }
        return count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await ProcessNextAsync(stoppingToken)) continue;
                await RefreshDepthAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _lastError = Limit(ex.Message);
                _logger.LogWarning(ex, "Enrichment queue polling failed; retrying");
            }
            using var idleWait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            idleWait.CancelAfter(TimeSpan.FromSeconds(2));
            try { await _wake.Reader.ReadAsync(idleWait.Token); }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
        }
    }

    internal async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        await RecoverExpiredLeasesAsync(cancellationToken);
        var job = await ClaimNextAsync(cancellationToken);
        if (job is null) return false;

        _processing = true;
        try
        {
            await ProcessAsync(job, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseLeaseAsync(job, CancellationToken.None);
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            await CompleteAsync(job, RecipeEnrichmentJobState.Superseded, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _lastError = Limit(ex.Message);
            await FailOrRetryAsync(job, ex.Message, cancellationToken);
            _logger.LogWarning(ex, "Recipe enrichment job {JobId} failed", job.Id);
        }
        finally
        {
            _processing = false;
            await RefreshDepthAsync(CancellationToken.None);
        }
        return true;
    }

    private static async Task<bool> CoalesceAsync(
        DishhiveDbContext context,
        Recipe recipe,
        RecipeEnrichmentTask task,
        string? targetLanguage,
        string operationId,
        CancellationToken cancellationToken)
    {
        var pending = context.RecipeEnrichmentJobs.Local.FirstOrDefault(job =>
            (job.Recipe == recipe || (recipe.Id != Guid.Empty && job.RecipeId == recipe.Id))
            && job.Task == task && job.State == RecipeEnrichmentJobState.Pending)
            ?? await context.RecipeEnrichmentJobs.FirstOrDefaultAsync(job =>
            job.RecipeId == recipe.Id
            && job.Task == task
            && job.State == RecipeEnrichmentJobState.Pending,
            cancellationToken);
        if (pending is not null)
        {
            pending.RecipeRevision = recipe.Revision;
            pending.FactsConfirmationVersion = recipe.FactsConfirmationVersion;
            pending.TargetLanguage = targetLanguage;
            pending.OperationId = operationId;
            pending.AttemptCount = 0;
            pending.NotBefore = DateTime.UtcNow;
            pending.Error = null;
            pending.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        context.RecipeEnrichmentJobs.Add(new RecipeEnrichmentJob
        {
            // New PostgreSQL recipes receive their UUID on insert. The navigation
            // gives EF the insert dependency and propagates that generated key.
            Recipe = recipe,
            RecipeId = recipe.Id,
            RecipeRevision = recipe.Revision,
            FactsConfirmationVersion = recipe.FactsConfirmationVersion,
            Task = task,
            TargetLanguage = targetLanguage,
            OperationId = operationId
        });
        return true;
    }

    internal async Task RecoverExpiredLeasesAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var now = DateTime.UtcNow;
        var expired = await context.RecipeEnrichmentJobs.Where(job =>
            job.State == RecipeEnrichmentJobState.Running
            && (job.LeaseExpiresAt == null || job.LeaseExpiresAt < now))
            .OrderByDescending(job => job.RecipeRevision)
            .ThenByDescending(job => job.FactsConfirmationVersion).ToListAsync(cancellationToken);
        foreach (var job in expired)
        {
            job.State = await HasPendingReplacementAsync(context, job, cancellationToken)
                ? RecipeEnrichmentJobState.Superseded : RecipeEnrichmentJobState.Pending;
            job.LeaseExpiresAt = null;
            job.NotBefore = now;
            job.UpdatedAt = now;
        }
        if (expired.Count > 0)
        {
            try { await context.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { /* Another poll or the owner won; retry next poll. */ }
            catch (DbUpdateException ex) when (IsPendingConflict(ex)) { /* Concurrent enqueue won. */ }
        }
    }

    private async Task<RecipeEnrichmentJob?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        // PostgreSQL timestamps retain microseconds. Keep the returned ownership
        // token identical to the value that will be read by the completion context.
        var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
        var job = await context.RecipeEnrichmentJobs
            .Where(j => j.State == RecipeEnrichmentJobState.Pending && j.NotBefore <= now)
            .OrderBy(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null) return null;
        job.State = RecipeEnrichmentJobState.Running;
        job.AttemptCount++;
        job.LeaseExpiresAt = now + LeaseDuration;
        job.UpdatedAt = now;
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return null; }
        return new RecipeEnrichmentJob
        {
            Id = job.Id,
            RecipeId = job.RecipeId,
            RecipeRevision = job.RecipeRevision,
            FactsConfirmationVersion = job.FactsConfirmationVersion,
            Task = job.Task,
            TargetLanguage = job.TargetLanguage,
            OperationId = job.OperationId,
            AttemptCount = job.AttemptCount,
            LeaseExpiresAt = job.LeaseExpiresAt
        };
    }

    private async Task ProcessAsync(RecipeEnrichmentJob job, CancellationToken cancellationToken)
    {
        Recipe? snapshot;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            snapshot = await context.Recipes.AsNoTracking()
                .Include(r => r.Ingredients).Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.Id == job.RecipeId, cancellationToken);
        }
        if (snapshot is null || snapshot.Revision != job.RecipeRevision)
        {
            await CompleteAsync(job, RecipeEnrichmentJobState.Superseded, null, cancellationToken);
            return;
        }

        using var inputSnapshot = AiInputSnapshotScope.ForRecipe(snapshot);
        if (job.Task == RecipeEnrichmentTask.Facts)
        {
            if (snapshot.Ingredients.Count == 0)
            {
                await CompleteAsync(job, RecipeEnrichmentJobState.Failed,
                    "A recipe without ingredients cannot be assessed", cancellationToken);
                return;
            }
            var classes = await _extractor.ExtractAsync(snapshot.Title,
                snapshot.Ingredients.OrderBy(i => i.SortOrder).Select(i => i.Name).ToList(),
                cancellationToken, job.OperationId);
            if (classes is null) throw new InvalidOperationException("Dietary facts assessment returned no result");
            await ApplyFactsAsync(job, classes, cancellationToken);
        }
        else
        {
            var translated = await _localizer.TranslateAsync(snapshot, job.TargetLanguage!, cancellationToken,
                job.OperationId);
            if (translated is null) throw new InvalidOperationException("Recipe translation returned no result");
            await ApplyTranslationAsync(job, translated, cancellationToken);
        }
        _lastError = null;
    }

    private async Task ApplyFactsAsync(
        RecipeEnrichmentJob job,
        IReadOnlyList<IngredientClass> classes,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var recipe = await context.Recipes.Include(r => r.DietaryFacts)
            .FirstOrDefaultAsync(r => r.Id == job.RecipeId, cancellationToken);
        var row = await context.RecipeEnrichmentJobs.FirstAsync(j => j.Id == job.Id, cancellationToken);
        if (!OwnsLease(row, job)) return;
        if (recipe is null || recipe.Revision != job.RecipeRevision
            || recipe.FactsConfirmationVersion != job.FactsConfirmationVersion
            || recipe.DietaryFactsStatus == DietaryFactsStatus.UserConfirmed)
        {
            row.State = RecipeEnrichmentJobState.Superseded;
        }
        else
        {
            context.RecipeDietaryFacts.RemoveRange(recipe.DietaryFacts);
            recipe.DietaryFacts.Clear();
            foreach (var ingredientClass in classes.Distinct())
                recipe.DietaryFacts.Add(new RecipeDietaryFact { RecipeId = recipe.Id, IngredientClass = ingredientClass });
            recipe.DietaryFactsStatus = DietaryFactsStatus.AiDetected;
            recipe.DietaryFactsAssessedAt = DateTime.UtcNow;
            recipe.DietaryFactsAssessedRevision = recipe.Revision;
            row.State = RecipeEnrichmentJobState.Completed;
        }
        row.LeaseExpiresAt = null;
        row.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyTranslationAsync(
        RecipeEnrichmentJob job,
        LocalizedRecipeText translated,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var recipe = await context.Recipes.Include(r => r.Ingredients).Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == job.RecipeId, cancellationToken);
        var row = await context.RecipeEnrichmentJobs.FirstAsync(j => j.Id == job.Id, cancellationToken);
        if (!OwnsLease(row, job)) return;
        if (recipe is null || recipe.Revision != job.RecipeRevision)
        {
            row.State = RecipeEnrichmentJobState.Superseded;
        }
        else
        {
            recipe.OriginalTitle ??= recipe.Title;
            recipe.OriginalDescription ??= recipe.Description;
            recipe.Title = translated.Title;
            recipe.Description = translated.Description;
            foreach (var pair in recipe.Ingredients.OrderBy(i => i.SortOrder).Zip(translated.IngredientNames))
                pair.First.Name = pair.Second;
            foreach (var pair in recipe.Steps.OrderBy(i => i.StepNumber).Zip(translated.Steps))
            {
                pair.First.OriginalInstruction ??= pair.First.Instruction;
                pair.First.Instruction = pair.Second;
            }
            recipe.ContentLanguage = job.TargetLanguage;
            // Child-only translations still need a parent UPDATE so EF includes
            // the captured revision and confirmation in the atomic write condition.
            context.Entry(recipe).Property(item => item.Revision).IsModified = true;
            row.State = RecipeEnrichmentJobState.Completed;
        }
        row.LeaseExpiresAt = null;
        row.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteAsync(RecipeEnrichmentJob claim, RecipeEnrichmentJobState state, string? error,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var job = await context.RecipeEnrichmentJobs.FirstOrDefaultAsync(j => j.Id == claim.Id, cancellationToken);
        if (job is null || !OwnsLease(job, claim)) return;
        job.State = state;
        job.Error = error is null ? null : Limit(error);
        job.LeaseExpiresAt = null;
        job.UpdatedAt = DateTime.UtcNow;
        if (state == RecipeEnrichmentJobState.Failed && job.Task == RecipeEnrichmentTask.Facts
            && !await HasPendingReplacementAsync(context, job, cancellationToken))
        {
            var recipe = await context.Recipes.FirstOrDefaultAsync(row => row.Id == job.RecipeId, cancellationToken);
            if (recipe is not null && recipe.Revision == job.RecipeRevision
                && recipe.FactsConfirmationVersion == job.FactsConfirmationVersion
                && recipe.DietaryFactsStatus != DietaryFactsStatus.UserConfirmed)
                recipe.DietaryFactsStatus = DietaryFactsStatus.Failed;
        }
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { /* A new owner controls this job. */ }
    }

    private async Task FailOrRetryAsync(RecipeEnrichmentJob claim, string error, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var job = await context.RecipeEnrichmentJobs.FirstOrDefaultAsync(j => j.Id == claim.Id, cancellationToken);
        if (job is null || !OwnsLease(job, claim)) return;
        job.Error = Limit(error);
        job.LeaseExpiresAt = null;
        job.UpdatedAt = DateTime.UtcNow;
        if (job.AttemptCount < MaxAttempts)
        {
            job.State = await HasPendingReplacementAsync(context, job, cancellationToken)
                ? RecipeEnrichmentJobState.Superseded : RecipeEnrichmentJobState.Pending;
            job.NotBefore = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Pow(2, job.AttemptCount));
        }
        else
        {
            job.State = RecipeEnrichmentJobState.Failed;
            if (job.Task == RecipeEnrichmentTask.Facts)
            {
                var recipe = await context.Recipes.FirstOrDefaultAsync(r => r.Id == job.RecipeId, cancellationToken);
                if (recipe is not null && recipe.Revision == job.RecipeRevision
                    && recipe.FactsConfirmationVersion == job.FactsConfirmationVersion
                    && recipe.DietaryFactsStatus != DietaryFactsStatus.UserConfirmed)
                    recipe.DietaryFactsStatus = DietaryFactsStatus.Failed;
            }
        }
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { /* A newer owner or source revision won. */ }
        catch (DbUpdateException ex) when (IsPendingConflict(ex)) { /* Recovery supersedes this lease next poll. */ }
    }

    private async Task ReleaseLeaseAsync(RecipeEnrichmentJob claim, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var job = await context.RecipeEnrichmentJobs.FirstOrDefaultAsync(j => j.Id == claim.Id, cancellationToken);
        if (job is null || !OwnsLease(job, claim)) return;
        job.State = await HasPendingReplacementAsync(context, job, cancellationToken)
            ? RecipeEnrichmentJobState.Superseded : RecipeEnrichmentJobState.Pending;
        job.LeaseExpiresAt = null;
        job.NotBefore = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { /* A newer owner won. */ }
        catch (DbUpdateException ex) when (IsPendingConflict(ex)) { /* Recovery supersedes this lease next poll. */ }
    }

    private async Task RefreshDepthAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            var depth = await context.RecipeEnrichmentJobs.CountAsync(j =>
                j.State == RecipeEnrichmentJobState.Pending || j.State == RecipeEnrichmentJobState.Running,
                cancellationToken);
            Volatile.Write(ref _knownDepth, depth);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static string Limit(string value) => value.Length <= 1000 ? value : value[..1000];

    private static bool OwnsLease(RecipeEnrichmentJob row, RecipeEnrichmentJob claim) =>
        row.State == RecipeEnrichmentJobState.Running && row.LeaseExpiresAt == claim.LeaseExpiresAt
        && row.LeaseExpiresAt > DateTime.UtcNow;

    private static bool IsPendingConflict(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException
            { SqlState: "23505", ConstraintName: "IX_RecipeEnrichmentJobs_RecipeId_Task" };

    private static async Task<bool> HasPendingReplacementAsync(DishhiveDbContext context,
        RecipeEnrichmentJob job, CancellationToken cancellationToken) =>
        context.RecipeEnrichmentJobs.Local.Any(other => other.Id != job.Id
            && other.RecipeId == job.RecipeId && other.Task == job.Task
            && other.State == RecipeEnrichmentJobState.Pending)
        || await context.RecipeEnrichmentJobs.AnyAsync(other => other.Id != job.Id
            && other.RecipeId == job.RecipeId && other.Task == job.Task
            && other.State == RecipeEnrichmentJobState.Pending, cancellationToken);
}
