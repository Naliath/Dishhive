using Dishhive.Api.Data;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Tests.Mocks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// End-to-end import pipeline tests (fetch → extract → parse → persist) with mocked HTTP,
/// using the stored Dagelijkse Kost fixture page
/// </summary>
public class RecipeImportServiceTests : IDisposable
{
    private const string FixtureUrl =
        "https://dagelijksekost.vrt.be/gerechten/cremeux-citroen-bodem-witte-chocolade-gepofte-rijst-rode-bessen";

    // The fixture's JSON-LD image URL points at Google Storage
    private const string ImageUrlPrefix = "https://storage.googleapis.com/";

    // 1x1 transparent PNG; the pipeline should normalize it to WebP.
    private static readonly byte[] FakeImageBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly DishhiveDbContext _context;

    public RecipeImportServiceTests()
    {
        var options = new DbContextOptionsBuilder<DishhiveDbContext>()
            .UseInMemoryDatabase($"ImportTests_{Guid.NewGuid()}")
            .Options;
        _context = new DishhiveDbContext(options);
    }

    private static string LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "dagelijkse-kost-recipe.html");
        return File.ReadAllText(path);
    }

    /// <summary>Facts queue with the NoOp extractor: enqueue is a no-op, worker never runs</summary>
    private static RecipeFactsAssessmentService NoOpFactsQueue() => new(
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        new NoOpRecipeFactsExtractor(),
        NullLogger<RecipeFactsAssessmentService>.Instance);

    private RecipeImportService CreateService(MockHttpMessageHandler handler)
    {
        return new RecipeImportService(
            new HttpClient(handler),
            [new DagelijkseKostProvider()],
            _context,
            new NoOpLlmRecipeExtractor(),
            NoOpFactsQueue(),
            NullLogger<RecipeImportService>.Instance);
    }

    private static MockHttpMessageHandler FixtureHandler()
    {
        return new MockHttpMessageHandler()
            .RespondWith(FixtureUrl, LoadFixture())
            .RespondWith(ImageUrlPrefix, FakeImageBytes, "image/jpeg");
    }

    [Fact]
    public async Task Import_FixtureRecipe_PersistsAllStepsAndIngredients()
    {
        var service = CreateService(FixtureHandler());

        var recipe = await service.ImportAsync(FixtureUrl);

        recipe.Steps.Should().HaveCount(11);
        recipe.Steps.OrderBy(s => s.StepNumber).First().Instruction
            .Should().Be("Laat de gelatine weken in koud water.");
        recipe.Ingredients.Should().HaveCount(15);
        recipe.SourceUrl.Should().Be(FixtureUrl);
        recipe.SourceProvider.Should().Be("dagelijkse-kost");
    }

    [Fact]
    public async Task Import_FixtureRecipe_StoresImageLocally()
    {
        var service = CreateService(FixtureHandler());

        var recipe = await service.ImportAsync(FixtureUrl);

        recipe.ImageData.Should().NotBeNullOrEmpty();
        recipe.ImageData.Should().NotEqual(FakeImageBytes);
        recipe.ImageContentType.Should().Be("image/webp");
        // The original source URL stays for traceability
        recipe.ImageUrl.Should().StartWith(ImageUrlPrefix);
    }

    [Fact]
    public async Task Import_ImageDownloadFails_KeepsRemoteUrlWithoutFailingImport()
    {
        var handler = new MockHttpMessageHandler()
            .RespondWith(FixtureUrl, LoadFixture())
            .FailWith(ImageUrlPrefix, System.Net.HttpStatusCode.Forbidden);
        var service = CreateService(handler);

        var recipe = await service.ImportAsync(FixtureUrl);

        recipe.ImageData.Should().BeNull();
        recipe.ImageContentType.Should().BeNull();
        recipe.ImageUrl.Should().StartWith(ImageUrlPrefix);
        recipe.Steps.Should().HaveCount(11);
    }

    [Fact]
    public async Task Import_NonImageContentType_SkipsLocalStorage()
    {
        var handler = new MockHttpMessageHandler()
            .RespondWith(FixtureUrl, LoadFixture())
            .RespondWith(ImageUrlPrefix, "<html>not an image</html>", "text/html");
        var service = CreateService(handler);

        var recipe = await service.ImportAsync(FixtureUrl);

        recipe.ImageData.Should().BeNull();
        recipe.ImageContentType.Should().BeNull();
    }

    [Fact]
    public async Task Import_SameUrlTwice_UpdatesExistingRecipeInsteadOfDuplicating()
    {
        var service = CreateService(FixtureHandler());

        var first = await service.ImportAsync(FixtureUrl);
        var second = await service.ImportAsync(FixtureUrl);

        second.Id.Should().Be(first.Id);
        _context.Recipes.Should().HaveCount(1);
        _context.RecipeSteps.Should().HaveCount(11);
        _context.RecipeIngredients.Should().HaveCount(15);
    }

    [Fact]
    public async Task Import_UnsupportedUrl_Throws()
    {
        var service = CreateService(new MockHttpMessageHandler());

        var act = () => service.ImportAsync("https://example.com/some-recipe");

        await act.Should().ThrowAsync<UnsupportedRecipeSourceException>();
    }

    // -------------------------------------------------------------------------
    // Provider precedence: dedicated providers win over the recipe-scrapers
    // sidecar fallback; the fallback only sees sites nobody else handles
    // -------------------------------------------------------------------------

    private const string SidecarBaseUrl = "http://scraper:8000/";

    private const string SidecarScrapeResponse = """
        {
          "title": "Fallback dish",
          "ingredients": ["1 thing"],
          "instructions": ["Do the thing."],
          "yields": "2 servings",
          "canonicalUrl": "https://unknown-site.test/dish"
        }
        """;

    private RecipeImportService CreateServiceWithFallback(MockHttpMessageHandler handler)
    {
        var sidecarHttpClient = new HttpClient(handler) { BaseAddress = new Uri(SidecarBaseUrl) };
        var fallback = new RecipeScrapersFallbackProvider(
            new RecipeScrapersClient(sidecarHttpClient, NullLogger<RecipeScrapersClient>.Instance),
            NullLogger<RecipeScrapersFallbackProvider>.Instance);

        return new RecipeImportService(
            new HttpClient(handler),
            [new DagelijkseKostProvider(), fallback],
            _context,
            new NoOpLlmRecipeExtractor(),
            NoOpFactsQueue(),
            NullLogger<RecipeImportService>.Instance);
    }

    [Fact]
    public async Task Import_SiteWithDedicatedProvider_DoesNotUseFallback()
    {
        var handler = FixtureHandler()
            .RespondWith(SidecarBaseUrl, SidecarScrapeResponse, "application/json");
        var service = CreateServiceWithFallback(handler);

        var recipe = await service.ImportAsync(FixtureUrl);

        recipe.SourceProvider.Should().Be("dagelijkse-kost");
        handler.Requests.Should().NotContain(r => r.AbsoluteUri.StartsWith(SidecarBaseUrl));
    }

    // -------------------------------------------------------------------------
    // LLM extraction fallback: when no structured provider can parse a page and
    // AI is configured, the page is handed to the LLM extractor
    // -------------------------------------------------------------------------

    private sealed class FakeLlmRecipeExtractor(ImportedRecipe? result) : ILlmRecipeExtractor
    {
        public bool WasCalled { get; private set; }
        public bool IsAvailable => true;

        public Task<ImportedRecipe?> ExtractAsync(string html, Uri sourceUrl, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Import_NoStructuredProviderButAiConfigured_UsesLlmExtraction()
    {
        var handler = new MockHttpMessageHandler()
            .RespondWith("https://blog.example/dish", "<html><body>A tasty dish</body></html>");
        var extractor = new FakeLlmRecipeExtractor(new ImportedRecipe
        {
            Title = "LLM dish",
            IngredientLines = ["1 onion"],
            Steps = ["Cook it."]
        });
        var service = new RecipeImportService(
            new HttpClient(handler),
            [new DagelijkseKostProvider()], // does not handle blog.example
            _context,
            extractor,
            NoOpFactsQueue(),
            NullLogger<RecipeImportService>.Instance);

        var recipe = await service.ImportAsync("https://blog.example/dish");

        extractor.WasCalled.Should().BeTrue();
        recipe.Title.Should().Be("LLM dish");
        recipe.SourceProvider.Should().Be("llm");
    }

    [Fact]
    public async Task Import_SiteWithoutDedicatedProvider_UsesScrapersFallback()
    {
        var handler = new MockHttpMessageHandler()
            .RespondWith("https://unknown-site.test/dish", "<html>some page</html>")
            .RespondWith(SidecarBaseUrl + "scrape", SidecarScrapeResponse, "application/json");
        var service = CreateServiceWithFallback(handler);

        var recipe = await service.ImportAsync("https://unknown-site.test/dish");

        recipe.SourceProvider.Should().Be("recipe-scrapers");
        recipe.Title.Should().Be("Fallback dish");
        recipe.Servings.Should().Be(2);
        recipe.Steps.Should().ContainSingle().Which.Instruction.Should().Be("Do the thing.");
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
