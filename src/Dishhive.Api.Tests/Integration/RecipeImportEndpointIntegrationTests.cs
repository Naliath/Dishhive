using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Tests.Mocks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Dishhive.Api.Tests.Integration;

/// <summary>
/// Integration tests for POST /api/recipes/import through the full HTTP pipeline,
/// with outbound HTTP (page + image fetch) replaced by the fixture-backed mock handler
/// </summary>
public class RecipeImportEndpointIntegrationTests : IDisposable
{
    private const string FixtureUrl =
        "https://dagelijksekost.vrt.be/gerechten/cremeux-citroen-bodem-witte-chocolade-gepofte-rijst-rode-bessen";

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RecipeImportEndpointIntegrationTests()
    {
        var fixtureHtml = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "dagelijkse-kost-recipe.html"));

        var mockHandler = new MockHttpMessageHandler()
            .RespondWith(FixtureUrl, fixtureHtml)
            .RespondWith("https://storage.googleapis.com/", [0xFF, 0xD8, 0xFF, 0xE0], "image/jpeg");

        _factory = new ImportTestFactory(mockHandler);
        _client = _factory.CreateClient();
    }

    /// <summary>Factory that reroutes the import service's outbound HTTP to the mock handler</summary>
    private sealed class ImportTestFactory(MockHttpMessageHandler handler) : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<IRecipeImportService, RecipeImportService>()
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        }
    }

    [Fact]
    public async Task ImportEndpoint_SupportedUrl_ReturnsCreatedRecipeWithLocalImage()
    {
        var response = await _client.PostAsJsonAsync("/api/recipes/import",
            new ImportRecipeRequestDto { Url = FixtureUrl });
        var recipe = await response.Content.ReadFromJsonAsync<RecipeDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        recipe!.Title.Should().Contain("crémeux van citroen");
        recipe.Ingredients.Should().HaveCount(15);
        recipe.Steps.Should().HaveCount(11);
        recipe.SourceProvider.Should().Be("dagelijkse-kost");
        recipe.HasLocalImage.Should().BeTrue();
        recipe.ImageUrl.Should().Be($"/api/recipes/{recipe.Id}/image");
    }

    [Fact]
    public async Task ImportEndpoint_ImportedImage_IsServedByImageEndpoint()
    {
        var importResponse = await _client.PostAsJsonAsync("/api/recipes/import",
            new ImportRecipeRequestDto { Url = FixtureUrl });
        var recipe = await importResponse.Content.ReadFromJsonAsync<RecipeDto>();

        var imageResponse = await _client.GetAsync($"/api/recipes/{recipe!.Id}/image");

        imageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        imageResponse.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        (await imageResponse.Content.ReadAsByteArrayAsync()).Should().Equal(0xFF, 0xD8, 0xFF, 0xE0);
    }

    [Fact]
    public async Task ImportEndpoint_UnsupportedSource_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/recipes/import",
            new ImportRecipeRequestDto { Url = "https://example.com/recept" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ImportEndpoint_UnreachablePage_ReturnsUnprocessableEntity()
    {
        // The mock handler 404s unmatched URLs, simulating a fetch failure
        var response = await _client.PostAsJsonAsync("/api/recipes/import",
            new ImportRecipeRequestDto { Url = "https://dagelijksekost.vrt.be/gerechten/bestaat-niet" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }
}
