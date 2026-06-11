using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

/// <summary>
/// Recipe organization: tags (implicit lifecycle), category/tag filters and
/// cookbooks (saved filters). See docs/features/recipe-organization.md.
/// </summary>
public class RecipeOrganizationIntegrationTests : TestBase
{
    [Fact]
    public async Task CreateRecipe_WithTags_ReturnsSortedTags()
    {
        var created = await CreateRecipeAsync("Lasagne", tags: ["pasta", "Comfort food"]);

        created.Tags.Should().Equal("Comfort food", "pasta");
    }

    [Fact]
    public async Task Tags_AreReusedCaseInsensitively_AcrossRecipes()
    {
        await CreateRecipeAsync("Lasagne", tags: ["Pasta"]);
        var second = await CreateRecipeAsync("Spaghetti", tags: ["pasta"]);

        second.Tags.Should().Equal("Pasta");
        var tags = await Client.GetFromJsonAsync<List<RecipeTagDto>>("/api/recipetags");
        tags!.Should().ContainSingle().Which.Name.Should().Be("Pasta");
    }

    [Fact]
    public async Task UpdateRecipe_RemovingLastUse_DeletesOrphanedTag()
    {
        var recipe = await CreateRecipeAsync("Lasagne", tags: ["pasta"]);

        await UpdateRecipeTagsAsync(recipe, []);

        var tags = await Client.GetFromJsonAsync<List<RecipeTagDto>>("/api/recipetags");
        tags!.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteRecipe_CleansUpOrphanedTags()
    {
        var recipe = await CreateRecipeAsync("Lasagne", tags: ["pasta"]);

        var response = await Client.DeleteAsync($"/api/recipes/{recipe.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var tags = await Client.GetFromJsonAsync<List<RecipeTagDto>>("/api/recipetags");
        tags!.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecipes_FiltersByTag_AllTagsMustMatch()
    {
        await CreateRecipeAsync("Lasagne", tags: ["pasta", "oven"]);
        await CreateRecipeAsync("Spaghetti", tags: ["pasta"]);
        await CreateRecipeAsync("Stew", tags: ["oven"]);

        var single = await Client.GetFromJsonAsync<List<RecipeListItemDto>>("/api/recipes?tags=pasta");
        var both = await Client.GetFromJsonAsync<List<RecipeListItemDto>>("/api/recipes?tags=pasta,oven");

        single!.Select(r => r.Title).Should().BeEquivalentTo("Lasagne", "Spaghetti");
        both!.Should().ContainSingle().Which.Title.Should().Be("Lasagne");
    }

    [Fact]
    public async Task GetRecipes_FiltersByCategory_CaseInsensitive()
    {
        await CreateRecipeAsync("Lasagne", category: "Hoofdgerecht");
        await CreateRecipeAsync("Cheesecake", category: "Dessert");

        var result = await Client.GetFromJsonAsync<List<RecipeListItemDto>>("/api/recipes?category=hoofdgerecht");

        result!.Should().ContainSingle().Which.Title.Should().Be("Lasagne");
    }

    [Fact]
    public async Task GetCategories_ReturnsDistinctOrderedCategories()
    {
        await CreateRecipeAsync("Lasagne", category: "Hoofdgerecht");
        await CreateRecipeAsync("Spaghetti", category: "Hoofdgerecht");
        await CreateRecipeAsync("Cheesecake", category: "Dessert");
        await CreateRecipeAsync("Mystery dish");

        var categories = await Client.GetFromJsonAsync<List<string>>("/api/recipes/categories");

        categories.Should().Equal("Dessert", "Hoofdgerecht");
    }

    [Fact]
    public async Task Cookbooks_CrudRoundtrip()
    {
        var create = new CreateCookbookDto { Name = "Italiaans", SearchTerm = "pasta", Tags = ["comfort"] };
        var createResponse = await Client.PostAsJsonAsync("/api/cookbooks", create);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createResponse.Content.ReadFromJsonAsync<CookbookDto>())!;

        var update = new UpdateCookbookDto { Name = "Italiaanse keuken", Category = "Hoofdgerecht" };
        var updateResponse = await Client.PutAsJsonAsync($"/api/cookbooks/{created.Id}", update);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await updateResponse.Content.ReadFromJsonAsync<CookbookDto>())!;
        updated.Name.Should().Be("Italiaanse keuken");
        updated.SearchTerm.Should().BeNull();
        updated.Category.Should().Be("Hoofdgerecht");

        var list = await Client.GetFromJsonAsync<List<CookbookDto>>("/api/cookbooks");
        list!.Should().ContainSingle(c => c.Id == created.Id);

        var deleteResponse = await Client.DeleteAsync($"/api/cookbooks/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetFromJsonAsync<List<CookbookDto>>("/api/cookbooks"))!.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateCookbook_WithoutAnyFilter_ReturnsBadRequest()
    {
        var response = await Client.PostAsJsonAsync("/api/cookbooks",
            new CreateCookbookDto { Name = "Leeg" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateCookbook_DuplicateName_ReturnsBadRequest()
    {
        await Client.PostAsJsonAsync("/api/cookbooks",
            new CreateCookbookDto { Name = "Italiaans", SearchTerm = "pasta" });

        var duplicate = await Client.PostAsJsonAsync("/api/cookbooks",
            new CreateCookbookDto { Name = "italiaans", SearchTerm = "pizza" });

        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<RecipeDto> CreateRecipeAsync(
        string title, List<string>? tags = null, string? category = null)
    {
        var response = await Client.PostAsJsonAsync("/api/recipes", new CreateRecipeDto
        {
            Title = title,
            Category = category,
            Tags = tags ?? []
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RecipeDto>())!;
    }

    private async Task UpdateRecipeTagsAsync(RecipeDto recipe, List<string> tags)
    {
        var response = await Client.PutAsJsonAsync($"/api/recipes/{recipe.Id}", new UpdateRecipeDto
        {
            Title = recipe.Title,
            Category = recipe.Category,
            Tags = tags
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
