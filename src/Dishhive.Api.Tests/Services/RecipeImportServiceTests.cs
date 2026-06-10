using Dishhive.Api.Data;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Tests.Mocks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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

    private static readonly byte[] FakeImageBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x42, 0x42];

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

    private RecipeImportService CreateService(MockHttpMessageHandler handler)
    {
        return new RecipeImportService(
            new HttpClient(handler),
            [new DagelijkseKostProvider()],
            _context,
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

        recipe.ImageData.Should().Equal(FakeImageBytes);
        recipe.ImageContentType.Should().Be("image/jpeg");
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

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
