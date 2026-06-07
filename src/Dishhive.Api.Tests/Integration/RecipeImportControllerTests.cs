using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class RecipeImportControllerTests : TestBase
{
    [Fact]
    public async Task Preview_with_unsupported_host_returns_400()
    {
        var resp = await Client.PostAsJsonAsync("/api/recipe-import/preview",
            new ImportPreviewRequest("https://example.com/some-recipe"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Preview_with_invalid_url_returns_400()
    {
        var resp = await Client.PostAsJsonAsync("/api/recipe-import/preview",
            new ImportPreviewRequest("not-a-url"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Providers_endpoint_lists_dagelijksekost()
    {
        var providers = await Client.GetFromJsonAsync<List<string>>("/api/recipe-import/providers");
        providers.Should().Contain("dagelijksekost");
    }

    [Fact]
    public async Task Save_persists_imported_recipe_with_raw_payload()
    {
        var dto = new ImportedRecipeDto(
            Title: "Imported pasta",
            Description: "from a fixture",
            Servings: 2,
            ImageUrl: "https://example.com/img.jpg",
            VideoUrl: "https://example.com/v.mp4",
            SourceUrl: "https://dagelijksekost.vrt.be/gerechten/x",
            ProviderKey: "dagelijksekost",
            SourceRawPayload: "{\"@type\":\"Recipe\"}",
            Ingredients: new List<ImportedIngredientDto>
            {
                new(0, "spaghetti", 400m, "g", 400m, "g", null, null),
            },
            Steps: new List<ImportedStepDto>
            {
                new(0, "Boil water"),
                new(1, "Cook pasta"),
            },
            Tags: new[] { "pasta" });

        var post = await Client.PostAsJsonAsync("/api/recipe-import/save", dto);
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var saved = await post.Content.ReadFromJsonAsync<RecipeDto>();
        saved!.Title.Should().Be("Imported pasta");
        saved.SourceProviderKey.Should().Be("dagelijksekost");
        saved.Ingredients.Should().HaveCount(1);
        saved.Steps.Should().HaveCount(2);
        saved.Tags.Should().Contain("pasta");

        // Round-trip: it should now appear in the recipes list.
        var all = await Client.GetFromJsonAsync<List<RecipeSummaryDto>>("/api/recipes");
        all.Should().Contain(r => r.Id == saved.Id);
    }
}
