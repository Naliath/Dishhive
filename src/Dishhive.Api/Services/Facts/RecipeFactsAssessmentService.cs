using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Localization;
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
    private readonly IRecipeLocalizationService _localizer;
    private readonly SupportedLanguageCatalog _languages;
    private readonly ILogger<RecipeFactsAssessmentService> _logger;

    private volatile bool _processing;
    private volatile string? _lastError;

    public RecipeFactsAssessmentService(
        IServiceScopeFactory scopeFactory, IRecipeFactsExtractor extractor, IRecipeLocalizationService localizer,
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
        IServiceScopeFactory scopeFactory, IRecipeFactsExtractor extractor,
        ILogger<RecipeFactsAssessmentService> logger)
        : this(scopeFactory, extractor, new NoOpRecipeLocalizationService(), new SupportedLanguageCatalog(), logger)
    {
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
        var messages = scope.ServiceProvider.GetRequiredService<UserMessageLocalizer>();

        var recipe = await context.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Include(r => r.DietaryFacts)
            .FirstOrDefaultAsync(r => r.Id == item.RecipeId, cancellationToken);
        if (recipe == null)
        {
            return; // deleted while queued
        }
        var shouldAssessFacts = recipe.DietaryFactsStatus != DietaryFactsStatus.UserConfirmed
            || item.OverwriteUserConfirmed;
        IReadOnlyList<IngredientClass>? classes = null;
        if (shouldAssessFacts && recipe.Ingredients.Count == 0)
        {
            // Nothing to classify: assessing from the title alone would be exactly
            // the world-knowledge guessing this feature replaces
            _logger.LogInformation("Skipping facts assessment for \"{Title}\": no ingredients", recipe.Title);
            shouldAssessFacts = false;
        }
        if (shouldAssessFacts)
        {
            classes = await _extractor.ExtractAsync(
                recipe.Title,
                recipe.Ingredients.OrderBy(i => i.SortOrder).Select(i => i.Name).ToList(),
                cancellationToken);
            if (classes == null)
                _lastError = await messages.GetAsync("recipeAssessment.failed", cancellationToken,
                    ("recipeTitle", recipe.Title));
        }

        if (classes != null)
        {
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
        }

        var languageSetting = await context.UserSettings.AsNoTracking().FirstOrDefaultAsync(
            setting => setting.Key == UserSettingKeys.PreferredLanguage, cancellationToken);
        var translateSetting = await context.UserSettings.AsNoTracking().FirstOrDefaultAsync(
            setting => setting.Key == UserSettingKeys.TranslateImportedRecipes, cancellationToken);
        var targetLanguage = _languages.Find(languageSetting?.Value)?.Code ?? _languages.DefaultCode;
        if (recipe.SourceUrl != null && translateSetting?.Value == "true" && _localizer.IsAvailable
            && recipe.ContentLanguage != targetLanguage)
        {
            var translated = await _localizer.TranslateAsync(recipe, targetLanguage, cancellationToken);
            if (translated != null)
            {
                recipe.OriginalTitle ??= recipe.Title;
                recipe.OriginalDescription ??= recipe.Description;
                recipe.Title = translated.Title;
                recipe.Description = translated.Description;
                foreach (var pair in recipe.Ingredients.OrderBy(item => item.SortOrder)
                             .Zip(translated.IngredientNames)) pair.First.Name = pair.Second;
                foreach (var pair in recipe.Steps.OrderBy(item => item.StepNumber).Zip(translated.Steps))
                {
                    pair.First.OriginalInstruction ??= pair.First.Instruction;
                    pair.First.Instruction = pair.Second;
                }
                recipe.ContentLanguage = targetLanguage;
            }
        }
        await context.SaveChangesAsync(cancellationToken);

        if (classes != null)
        {
            _lastError = null;
            _logger.LogInformation(
                "Facts assessed for \"{Title}\": [{Classes}]",
                recipe.Title, string.Join(", ", classes));
        }
    }
}
