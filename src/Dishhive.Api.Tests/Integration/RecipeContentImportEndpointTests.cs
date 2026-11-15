using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Import;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Dishhive.Api.Tests.Integration;

public sealed class RecipeContentImportEndpointTests
{
    [Fact]
    public async Task TextImport_UsesSharedPersistenceAndCreatesDistinctRecipes()
    {
        using var factory = new TestWebApplicationFactory();
        var extractor = Substitute.For<IRecipeContentExtractor>();
        extractor.IsAvailable.Returns(true);
        extractor.ExtractAsync(Arg.Any<string?>(), Arg.Any<ProcessedRecipeImage?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ImportedRecipe { Title = "Soup", IngredientLines = ["- 2 onions"], Steps = ["- Cook."], RawData = "{\"text\":\"source\"}" });
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRecipeContentExtractor>(); services.AddSingleton(extractor);
        }));
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/recipes/import/text", new { text = "2 onions. Cook." });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var recipe = (await response.Content.ReadFromJsonAsync<RecipeDto>())!;
        Assert.Null(recipe.SourceUrl); Assert.Equal("llm-text", recipe.SourceProvider);
        Assert.Equal("onions", Assert.Single(recipe.Ingredients).Name);
        Assert.Equal("Cook.", Assert.Single(recipe.Steps).Instruction);
        var second = await client.PostAsJsonAsync("/api/recipes/import/text", new { text = "2 onions. Cook." });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual(recipe.Id, (await second.Content.ReadFromJsonAsync<RecipeDto>())!.Id);
    }

    [Fact]
    public async Task ImageImport_ValidatesActualBytesNormalizesAndDoesNotStoreSourceAsDishImage()
    {
        using var factory = new TestWebApplicationFactory();
        var extractor = Substitute.For<IRecipeContentExtractor>(); extractor.IsAvailable.Returns(true);
        ProcessedRecipeImage? received = null;
        extractor.ExtractAsync(Arg.Is<string?>(text => text == null), Arg.Any<ProcessedRecipeImage?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => { received = call.ArgAt<ProcessedRecipeImage>(1); return Task.FromResult(new ImportedRecipe { Title = "Soup", IngredientLines = ["2 onions"], Steps = ["Cook."] }); });
        var vision = Substitute.For<IRecipeImageCapability>(); vision.State.Returns("available");
        vision.CheckAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>()).Returns("available");
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRecipeContentExtractor>(); services.AddSingleton(extractor);
            services.RemoveAll<IRecipeImageCapability>(); services.AddSingleton(vision);
        }));
        using var client = host.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(RecipeImageCapability.CreateProbe().Bytes), "file", "source.png");
        var response = await client.PostAsync("/api/recipes/import/image", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("image/webp", received!.ContentType);
        var recipe = (await response.Content.ReadFromJsonAsync<RecipeDto>())!;
        Assert.Equal("llm-image", recipe.SourceProvider); Assert.Null(recipe.SourceUrl);
        Assert.False(recipe.HasLocalImage);
        using var bad = new MultipartFormDataContent(); bad.Add(new StringContent("not an image"), "file", "fake.png");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/api/recipes/import/image", bad)).StatusCode);
    }

    [Fact]
    public async Task UnconfiguredAi_ReportsCapabilitiesAndActionableErrors()
    {
        using var factory = new TestWebApplicationFactory(); using var client = factory.CreateClient();
        var capabilities = (await client.GetFromJsonAsync<RecipeImportCapabilitiesDto>("/api/recipes/import/capabilities"))!;
        Assert.False(capabilities.TextAvailable); Assert.Equal("unavailable", capabilities.PhotoState);
        var response = await client.PostAsJsonAsync("/api/recipes/import/text", new { text = "2 onions. Cook." });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("import_aiUnavailable", json.GetProperty("code").GetString());
        Assert.DoesNotContain("recipeImport.errors", json.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/recipes/import/text", new { text = "" })).StatusCode);
    }
}
