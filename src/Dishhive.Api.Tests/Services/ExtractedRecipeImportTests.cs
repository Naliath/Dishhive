using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dishhive.Api.Tests.Services;

public sealed class ExtractedRecipeImportTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly DishhiveDbContext _context;
    private readonly IRecipeFactsExtractor _facts = Substitute.For<IRecipeFactsExtractor>();
    private readonly IRecipeLocalizationService _localizer = Substitute.For<IRecipeLocalizationService>();
    private readonly RecipeFactsAssessmentService _worker;
    private readonly RecipeImportService _importer;

    public ExtractedRecipeImportTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<DishhiveDbContext>(options => options.UseInMemoryDatabase($"extracted-{Guid.NewGuid()}"));
        _provider = services.BuildServiceProvider();
        _context = _provider.GetRequiredService<DishhiveDbContext>();
        _facts.IsAvailable.Returns(true);
        _localizer.IsAvailable.Returns(true);
        _worker = new RecipeFactsAssessmentService(_provider.GetRequiredService<IServiceScopeFactory>(),
            _facts, _localizer, new SupportedLanguageCatalog(), NullLogger<RecipeFactsAssessmentService>.Instance);
        // No network or model calls belong to completion of a source-only recipe.
        _importer = new RecipeImportService(Substitute.For<ISafeHttpFetcher>(), [], _context,
            new NoOpLlmRecipeExtractor(), _worker, NullLogger<RecipeImportService>.Instance);
    }

    private static ImportedRecipe ValidRecipe() => new()
    {
        Title = "Pancakes", IngredientLines = ["- 200 g flour", "- salt"],
        Steps = ["- Mix.", "- Cook."], RawData = "{\"sourceType\":\"text\"}"
    };

    private async Task EnableTranslationAsync(bool enabled = true)
    {
        _context.UserSettings.AddRange(
            new UserSetting { Key = UserSettingKeys.TranslateImportedRecipes, Value = enabled ? "true" : "false" },
            new UserSetting { Key = UserSettingKeys.PreferredLanguage, Value = "nl" });
        await _context.SaveChangesAsync();
    }

    [Theory]
    [InlineData(RecipeImportSource.Text, RecipeImportProvenance.TextProvider)]
    [InlineData(RecipeImportSource.Photo, RecipeImportProvenance.PhotoProvider)]
    public async Task ImportExtracted_MapsAndStagesBothEnrichmentsWithoutAUrl(RecipeImportSource source, string provider)
    {
        await EnableTranslationAsync();
        var recipe = await _importer.ImportExtractedAsync(ValidRecipe(), source, "source-operation");
        _context.ChangeTracker.Clear();
        var stored = await _context.Recipes.Include(r => r.Ingredients).Include(r => r.Steps).SingleAsync();
        Assert.Equal(recipe.Id, stored.Id);
        Assert.Null(stored.SourceUrl);
        Assert.Equal(provider, stored.SourceProvider);
        Assert.Equal(4, stored.Servings);
        Assert.Equal("{\"sourceType\":\"text\"}", stored.SourceRawData);
        var flour = stored.Ingredients.Single(i => i.SortOrder == 0);
        Assert.Equal("flour", flour.Name);
        Assert.Equal(200, flour.Quantity);
        Assert.Equal("g", flour.Unit);
        Assert.Equal("200 g flour", flour.OriginalText);
        Assert.Null(stored.Ingredients.Single(i => i.SortOrder == 1).Quantity);
        Assert.Equal(new[] { "Mix.", "Cook." }, stored.Steps.OrderBy(s => s.StepNumber).Select(s => s.Instruction));
        var jobs = await _context.RecipeEnrichmentJobs.ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, job =>
        {
            Assert.Equal(stored.Id, job.RecipeId);
            Assert.Equal(stored.Revision, job.RecipeRevision);
            Assert.Equal("source-operation", job.OperationId);
        });
        Assert.Equal("nl", jobs.Single(j => j.Task == RecipeEnrichmentTask.Translation).TargetLanguage);
        Assert.Contains(jobs, j => j.Task == RecipeEnrichmentTask.Facts);
    }

    [Fact]
    public async Task ImportExtracted_RepeatedTitlesAndModelUrlsNeverOverwriteExistingRecipes()
    {
        var existing = new Recipe { Title = "Pancakes", SourceUrl = "https://example.com/recipe" };
        var manual = new Recipe { Title = "Pancakes" };
        _context.Recipes.AddRange(existing, manual);
        await _context.SaveChangesAsync();
        var extracted = ValidRecipe() with { SourceUrl = existing.SourceUrl };
        var first = await _importer.ImportExtractedAsync(extracted, RecipeImportSource.Text);
        var second = await _importer.ImportExtractedAsync(extracted, RecipeImportSource.Photo);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(4, await _context.Recipes.CountAsync());
        Assert.Null(first.SourceUrl);
        Assert.Null(second.SourceUrl);
        Assert.Empty(manual.Ingredients);
        Assert.Empty(existing.Ingredients);
        Assert.Equal(1, existing.Revision);
    }

    public static IEnumerable<object[]> InvalidRecipes()
    {
        yield return [ValidRecipe() with { Title = " " }];
        yield return [ValidRecipe() with { Title = new string('x', 301) }];
        yield return [ValidRecipe() with { IngredientLines = ["- ", " "] }];
        yield return [ValidRecipe() with { IngredientLines = [new string('x', 301)] }];
        yield return [ValidRecipe() with { IngredientLines = [new string('x', 201)] }];
        yield return [ValidRecipe() with { Steps = ["- "] }];
        yield return [ValidRecipe() with { Steps = [new string('x', 2001)] }];
        yield return [ValidRecipe() with { Servings = 0 }];
        yield return [ValidRecipe() with { Servings = 101 }];
        yield return [ValidRecipe() with { PrepTimeMinutes = -1 }];
        yield return [ValidRecipe() with { CookTimeMinutes = -1 }];
        yield return [ValidRecipe() with { TotalTimeMinutes = -1 }];
        yield return [ValidRecipe() with { Description = new string('x', 2001) }];
        yield return [ValidRecipe() with { Category = new string('x', 101) }];
        yield return [ValidRecipe() with { Keywords = new string('x', 501) }];
        yield return [ValidRecipe() with { RawData = "raw text is not json" }];
    }

    [Theory]
    [MemberData(nameof(InvalidRecipes))]
    public async Task ImportExtracted_InvalidContentLeavesTrackedAndStoredDataUntouched(ImportedRecipe imported)
    {
        await Assert.ThrowsAsync<RecipeExtractionFailedException>(async () =>
            await _importer.ImportExtractedAsync(imported, RecipeImportSource.Text));
        Assert.Empty(_context.ChangeTracker.Entries());
        Assert.Equal(0, await _context.Recipes.CountAsync());
        Assert.Equal(0, await _context.RecipeEnrichmentJobs.CountAsync());
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("file-import", null, false)]
    [InlineData("unknown", null, false)]
    [InlineData("dagelijkse-kost", "https://example.com/recipe", true)]
    [InlineData(RecipeImportProvenance.TextProvider, null, true)]
    [InlineData(RecipeImportProvenance.PhotoProvider, null, true)]
    public async Task Stage_TranslatesOnlyRecognizedSources(string? provider, string? url, bool translates)
    {
        await EnableTranslationAsync();
        var recipe = new Recipe { Title = "Test", SourceProvider = provider, SourceUrl = url };
        _context.Add(recipe);
        await _worker.StageAsync(_context, recipe);
        await _context.SaveChangesAsync();
        Assert.Equal(translates, await _context.RecipeEnrichmentJobs.AnyAsync(j => j.Task == RecipeEnrichmentTask.Translation));
    }

    [Theory]
    [InlineData(false, null, true)]
    [InlineData(true, "nl", true)]
    [InlineData(true, null, false)]
    public async Task Stage_RespectsTranslationPreferenceLanguageAndAvailability(bool enabled, string? language, bool available)
    {
        await EnableTranslationAsync(enabled);
        _localizer.IsAvailable.Returns(available);
        var recipe = new Recipe { Title = "Test", SourceProvider = RecipeImportProvenance.TextProvider, ContentLanguage = language };
        _context.Add(recipe);
        await _worker.StageAsync(_context, recipe);
        await _context.SaveChangesAsync();
        Assert.False(await _context.RecipeEnrichmentJobs.AnyAsync(j => j.Task == RecipeEnrichmentTask.Translation));
    }

    [Fact]
    public async Task ImportExtracted_UnconfiguredEnrichmentStillSavesRecipe()
    {
        _facts.IsAvailable.Returns(false);
        _localizer.IsAvailable.Returns(false);
        await _importer.ImportExtractedAsync(ValidRecipe(), RecipeImportSource.Text);
        Assert.Equal(1, await _context.Recipes.CountAsync());
        Assert.Empty(await _context.RecipeEnrichmentJobs.ToListAsync());
    }

    public void Dispose()
    {
        _worker.Dispose();
        _provider.Dispose();
    }
}
