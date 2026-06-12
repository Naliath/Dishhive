using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

/// <summary>
/// Integration tests for the recipe library exchange: GET /api/recipes/export
/// (schema.org Recipe JSON) and POST /api/recipes/import/file
/// </summary>
public class RecipeExchangeIntegrationTests : TestBase
{
    // 1x1 transparent PNG
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private static Recipe FullRecipe() => new()
    {
        Title = "Stoofvlees met frieten",
        Description = "Vlaamse klassieker",
        Servings = 4,
        PrepTimeMinutes = 20,
        CookTimeMinutes = 180,
        Category = "Hoofdgerecht",
        Keywords = "vlaams, winter",
        Ingredients =
        {
            new RecipeIngredient
            {
                SortOrder = 0, Name = "rundsvlees", Quantity = 1, Unit = "kg",
                OriginalText = "1 kg rundsvlees", OriginalQuantity = 1, OriginalUnit = "kg"
            },
            new RecipeIngredient { SortOrder = 1, Name = "bruin bier", Quantity = 330, Unit = "ml", OriginalText = "" }
        },
        Steps =
        {
            new RecipeStep { StepNumber = 1, Instruction = "Bak het vlees aan." },
            new RecipeStep { StepNumber = 2, Instruction = "Laat 3 uur sudderen." }
        }
    };

    private async Task<RecipeFileImportResultDto> ImportFileAsync(string json, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(json)), "file", "recipes.json" }
        };
        var response = await Client.PostAsync("/api/recipes/import/file", content);
        response.StatusCode.Should().Be(expected);
        return expected == HttpStatusCode.OK
            ? (await response.Content.ReadFromJsonAsync<RecipeFileImportResultDto>())!
            : new RecipeFileImportResultDto();
    }

    [Fact]
    public async Task Export_ProducesSchemaOrgGraphWithAllFields()
    {
        var recipe = FullRecipe();
        recipe.ImageData = Convert.FromBase64String(TinyPngBase64);
        recipe.ImageContentType = "image/png";
        DbContext.Recipes.Add(recipe);
        await DbContext.SaveChangesAsync();

        var response = await Client.GetAsync("/api/recipes/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain("dishhive-recipes-");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("@context").GetString().Should().Be("https://schema.org");
        var graph = document.RootElement.GetProperty("@graph");
        graph.GetArrayLength().Should().Be(1);

        var node = graph[0];
        node.GetProperty("@type").GetString().Should().Be("Recipe");
        node.GetProperty("name").GetString().Should().Be("Stoofvlees met frieten");
        node.GetProperty("recipeYield").GetInt32().Should().Be(4);
        node.GetProperty("prepTime").GetString().Should().Be("PT20M");
        node.GetProperty("cookTime").GetString().Should().Be("PT180M");
        node.GetProperty("recipeCategory").GetString().Should().Be("Hoofdgerecht");
        node.GetProperty("image").GetString().Should().StartWith("data:image/png;base64,");

        var ingredients = node.GetProperty("recipeIngredient");
        ingredients[0].GetString().Should().Be("1 kg rundsvlees");
        // no original text -> composed from the structured values
        ingredients[1].GetString().Should().Be("330 ml bruin bier");

        var instructions = node.GetProperty("recipeInstructions");
        instructions[0].GetProperty("@type").GetString().Should().Be("HowToStep");
        instructions[0].GetProperty("text").GetString().Should().Be("Bak het vlees aan.");
    }

    [Fact]
    public async Task Import_CreatesRecipeFromSingleSchemaOrgObject()
    {
        var json = """
        {
          "@context": "https://schema.org",
          "@type": "Recipe",
          "name": "Pannenkoeken",
          "recipeYield": "6 stuks",
          "totalTime": "PT30M",
          "recipeIngredient": ["200 gram bloem", "3 eieren"],
          "recipeInstructions": [{ "@type": "HowToStep", "text": "Meng alles." }],
          "dishhive:tags": ["zoet", "snel"]
        }
        """;

        var result = await ImportFileAsync(json);

        result.Created.Should().Be(1);
        result.Updated.Should().Be(0);
        result.Skipped.Should().Be(0);

        var recipes = await Client.GetFromJsonAsync<List<RecipeListItemDto>>("/api/recipes?search=pannenkoeken");
        var recipe = await Client.GetFromJsonAsync<RecipeDto>($"/api/recipes/{recipes!.Single().Id}");

        recipe!.Servings.Should().Be(6);
        recipe.TotalTimeMinutes.Should().Be(30);
        recipe.SourceProvider.Should().Be("file-import");
        recipe.Ingredients.Should().HaveCount(2);
        recipe.Ingredients[0].OriginalText.Should().Be("200 gram bloem");
        recipe.Ingredients[0].Quantity.Should().Be(200); // parsed through IngredientLineParser
        recipe.Steps.Should().ContainSingle(s => s.Instruction == "Meng alles.");
        recipe.Tags.Should().BeEquivalentTo("zoet", "snel");
    }

    [Fact]
    public async Task Import_OwnExport_RoundTripsWithoutDuplicates()
    {
        DbContext.Recipes.Add(FullRecipe());
        await DbContext.SaveChangesAsync();

        var export = await Client.GetStringAsync("/api/recipes/export");
        var result = await ImportFileAsync(export);

        // manual recipe (no source URL) with a known title -> the library version wins
        result.Created.Should().Be(0);
        result.Updated.Should().Be(0);
        result.Skipped.Should().Be(1);
        result.SkippedRecipes.Single().Title.Should().Be("Stoofvlees met frieten");

        var recipes = await Client.GetFromJsonAsync<List<RecipeListItemDto>>("/api/recipes");
        recipes.Should().HaveCount(1);
    }

    [Fact]
    public async Task Import_KnownSourceUrl_UpdatesInsteadOfDuplicating()
    {
        var existing = FullRecipe();
        existing.SourceUrl = "https://example.com/recepten/stoofvlees";
        DbContext.Recipes.Add(existing);
        await DbContext.SaveChangesAsync();

        var json = """
        {
          "@type": "Recipe",
          "name": "Stoofvlees, verbeterde versie",
          "url": "https://example.com/recepten/stoofvlees",
          "recipeYield": 6,
          "recipeIngredient": ["1,5 kg rundsvlees"],
          "recipeInstructions": ["Stoof het vlees."]
        }
        """;

        var result = await ImportFileAsync(json);

        result.Updated.Should().Be(1);
        result.Created.Should().Be(0);

        var fresh = CreateFreshContext();
        var recipe = fresh.Recipes.Single();
        recipe.Title.Should().Be("Stoofvlees, verbeterde versie");
        recipe.Servings.Should().Be(6);
        fresh.RecipeIngredients.Should().ContainSingle(i => i.RecipeId == recipe.Id);
    }

    [Fact]
    public async Task Import_DataUriImage_StoresLocalImageBytes()
    {
        var json = $$"""
        {
          "@type": "Recipe",
          "name": "Recept met foto",
          "image": "data:image/png;base64,{{TinyPngBase64}}",
          "recipeIngredient": ["1 ei"],
          "recipeInstructions": ["Bak het ei."]
        }
        """;

        var result = await ImportFileAsync(json);
        result.Created.Should().Be(1);

        var fresh = CreateFreshContext();
        var recipe = fresh.Recipes.Single();
        recipe.ImageData.Should().Equal(Convert.FromBase64String(TinyPngBase64));
        recipe.ImageContentType.Should().Be("image/png");
        recipe.ImageUrl.Should().BeNull(); // data URIs never land in the URL column

        var image = await Client.GetAsync($"/api/recipes/{recipe.Id}/image");
        image.StatusCode.Should().Be(HttpStatusCode.OK);
        image.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
    }

    [Fact]
    public async Task Import_RecipeWithoutName_IsReportedAsSkipped()
    {
        var json = """
        {
          "@graph": [
            { "@type": "Recipe", "recipeIngredient": ["iets"] },
            { "@type": "Recipe", "name": "Geldig recept", "recipeInstructions": ["Doe iets."] }
          ]
        }
        """;

        var result = await ImportFileAsync(json);

        result.Created.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.SkippedRecipes.Single().Reason.Should().Contain("no name");
    }

    [Fact]
    public async Task Import_InvalidJson_ReturnsBadRequest()
    {
        await ImportFileAsync("definitely { not json", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Import_JsonWithoutRecipes_ReturnsUnprocessableEntity()
    {
        await ImportFileAsync("""{ "@type": "Article", "name": "Geen recept" }""", HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Import_MissingFile_ReturnsBadRequest()
    {
        using var content = new MultipartFormDataContent();
        var response = await Client.PostAsync("/api/recipes/import/file", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
