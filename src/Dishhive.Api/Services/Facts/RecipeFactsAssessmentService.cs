using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Dishhive.Api.Services.Facts;

/// <summary>Progress snapshot for the settings page (polled while a backfill runs)</summary>
public record RecipeFactsQueueStatus(int QueueDepth, bool Running, string? LastError);

/// <summary>
/// Background dietary-facts assessment: recipe ids are enqueued after import/create/
/// ingredient-edit (and in bulk by the settings-page backfill) and processed one at a
/// time — deliberately sequential so a local LM Studio model never gets hammered with
/// parallel calls, and so the settings page can show a meaningful remaining count.
/// Fire-and-forget by design: enqueueing never blocks the save that triggered it, a
/// failed assessment just leaves the recipe Unassessed (it behaves like before the
/// feature existed) and is visible via LastError + logs.
/// </summary>
public class RecipeFactsAssessmentService : BackgroundService
{
    /// <param name="RecipeId">Recipe to (re)assess</param>
    /// <param name="OverwriteUserConfirmed">True only for ingredient edits: the
    /// confirmed facts describe ingredients that no longer exist, so a fresh AI
    /// assessment beats a stale human one. Everything else respects UserConfirmed.</param>
    private readonly record struct WorkItem(Guid RecipeId, bool OverwriteUserConfirmed);

    private readonly Channel<WorkItem> _queue = Channel.CreateUnbounded<WorkItem>(
        new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Ids currently queued or processing, for dedupe and the depth counter</summary>
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRecipeFactsExtractor _extractor;
    private readonly ILogger<RecipeFactsAssessmentService> _logger;

    private volatile bool _processing;
    private volatile string? _lastError;

    public RecipeFactsAssessmentService(
        IServiceScopeFactory scopeFactory, IRecipeFactsExtractor extractor,
        ILogger<RecipeFactsAssessmentService> logger)
    {
        _scopeFactory = scopeFactory;
        _extractor = extractor;
        _logger = logger;
    }

    public RecipeFactsQueueStatus Status => new(_pending.Count, _processing || !_pending.IsEmpty, _lastError);

    /// <summary>
    /// Queues one recipe for assessment. No-op (false) when AI is unconfigured or
    /// the recipe is already queued.
    /// </summary>
    public bool TryEnqueue(Guid recipeId, bool overwriteUserConfirmed = false)
    {
        if (!_extractor.IsAvailable || !_pending.TryAdd(recipeId, 0))
        {
            return false;
        }

        _queue.Writer.TryWrite(new WorkItem(recipeId, overwriteUserConfirmed));
        return true;
    }

    /// <summary>Bulk enqueue (settings-page backfill); returns how many were newly queued</summary>
    public int EnqueueMany(IEnumerable<Guid> recipeIds) => recipeIds.Count(id => TryEnqueue(id));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            _processing = true;
            try
            {
                await ProcessAsync(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logger.LogWarning(ex, "Facts assessment crashed for recipe {RecipeId}", item.RecipeId);
            }
            finally
            {
                _pending.TryRemove(item.RecipeId, out _);
                _processing = false;
            }
        }
    }

    private async Task ProcessAsync(WorkItem item, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();

        var recipe = await context.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.DietaryFacts)
            .FirstOrDefaultAsync(r => r.Id == item.RecipeId, cancellationToken);
        if (recipe == null)
        {
            return; // deleted while queued
        }
        if (recipe.DietaryFactsStatus == DietaryFactsStatus.UserConfirmed && !item.OverwriteUserConfirmed)
        {
            return; // a human verdict outranks a fresher model one
        }
        if (recipe.Ingredients.Count == 0)
        {
            // Nothing to classify: assessing from the title alone would be exactly
            // the world-knowledge guessing this feature replaces
            _logger.LogInformation("Skipping facts assessment for \"{Title}\": no ingredients", recipe.Title);
            return;
        }

        var classes = await _extractor.ExtractAsync(
            recipe.Title,
            recipe.Ingredients.OrderBy(i => i.SortOrder).Select(i => i.Name).ToList(),
            cancellationToken);
        if (classes == null)
        {
            _lastError = $"Assessment failed for \"{recipe.Title}\" (see logs)";
            return; // stays Unassessed; behaves like before the feature existed
        }

        recipe.DietaryFacts.Clear();
        foreach (var ingredientClass in classes)
        {
            recipe.DietaryFacts.Add(new RecipeDietaryFact
            {
                RecipeId = recipe.Id,
                IngredientClass = ingredientClass
            });
        }
        recipe.DietaryFactsStatus = DietaryFactsStatus.AiDetected;
        recipe.DietaryFactsAssessedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        _lastError = null;
        _logger.LogInformation(
            "Facts assessed for \"{Title}\": [{Classes}]",
            recipe.Title, string.Join(", ", classes));
    }
}
