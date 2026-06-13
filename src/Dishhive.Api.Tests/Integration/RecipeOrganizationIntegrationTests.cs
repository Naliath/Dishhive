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
    public async Task Collections_CrudRoundtrip()
    {
        // Collections may be created empty — they are curated sets, not filters
        var createResponse = await Client.PostAsJsonAsync("/api/cookbooks",
            new CreateCookbookDto { Name = "Italiaans" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createResponse.Content.ReadFromJsonAsync<CookbookDto>())!;
        created.Kind.Should().Be("manual");
        created.RecipeCount.Should().Be(0);

        var updateResponse = await Client.PutAsJsonAsync($"/api/cookbooks/{created.Id}",
            new UpdateCookbookDto { Name = "Italiaanse keuken" });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updateResponse.Content.ReadFromJsonAsync<CookbookDto>())!.Name.Should().Be("Italiaanse keuken");

        var list = await Client.GetFromJsonAsync<List<CookbookDto>>("/api/cookbooks");
        list!.Should().ContainSingle(c => c.Id == created.Id);

        var deleteResponse = await Client.DeleteAsync($"/api/cookbooks/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetFromJsonAsync<List<CookbookDto>>("/api/cookbooks"))!
            .Where(c => c.Kind == "manual").Should().BeEmpty();
    }

    [Fact]
    public async Task CreateCollection_DuplicateName_ReturnsBadRequest()
    {
        await Client.PostAsJsonAsync("/api/cookbooks", new CreateCookbookDto { Name = "Italiaans" });

        var duplicate = await Client.PostAsJsonAsync("/api/cookbooks",
            new CreateCookbookDto { Name = "italiaans" });

        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateCollection_BracketInName_ReturnsBadRequest()
    {
        // Brackets delimit #[Name] references in planning instructions
        var response = await Client.PostAsJsonAsync("/api/cookbooks",
            new CreateCookbookDto { Name = "Snel [weekdag]" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateCollection_ReservedAutoName_ReturnsBadRequest()
    {
        var response = await Client.PostAsJsonAsync("/api/cookbooks",
            new CreateCookbookDto { Name = "Top rated" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Collections_AddRecipes_IsIdempotent_AndListsMembers()
    {
        var lasagne = await CreateRecipeAsync("Lasagne");
        var stew = await CreateRecipeAsync("Stew");
        var collection = await CreateCollectionAsync("Comfort food");

        var add = new CookbookRecipesRequestDto { RecipeIds = [lasagne.Id, stew.Id] };
        var first = await Client.PostAsJsonAsync($"/api/cookbooks/{collection.Id}/recipes", add);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        // Re-adding the same recipes silently keeps the membership unchanged
        var second = await Client.PostAsJsonAsync($"/api/cookbooks/{collection.Id}/recipes", add);
        (await second.Content.ReadFromJsonAsync<CookbookDto>())!.RecipeCount.Should().Be(2);

        var members = await Client.GetFromJsonAsync<List<RecipeListItemDto>>(
            $"/api/cookbooks/{collection.Id}/recipes");
        members!.Select(r => r.Title).Should().Equal("Lasagne", "Stew");
    }

    [Fact]
    public async Task Collections_AddUnknownRecipe_ReturnsBadRequest()
    {
        var collection = await CreateCollectionAsync("Comfort food");

        var response = await Client.PostAsJsonAsync($"/api/cookbooks/{collection.Id}/recipes",
            new CookbookRecipesRequestDto { RecipeIds = [Guid.NewGuid()] });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Collections_RemoveRecipe_RemovesMembershipOnly()
    {
        var lasagne = await CreateRecipeAsync("Lasagne");
        var collection = await CreateCollectionAsync("Comfort food");
        await Client.PostAsJsonAsync($"/api/cookbooks/{collection.Id}/recipes",
            new CookbookRecipesRequestDto { RecipeIds = [lasagne.Id] });

        var remove = await Client.DeleteAsync($"/api/cookbooks/{collection.Id}/recipes/{lasagne.Id}");
        remove.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Client.GetFromJsonAsync<List<RecipeListItemDto>>($"/api/cookbooks/{collection.Id}/recipes"))!
            .Should().BeEmpty();
        // The recipe itself survives
        (await Client.GetAsync($"/api/recipes/{lasagne.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RecipeSide_MembershipSync_ReplacesAndReportsOnDetail()
    {
        var lasagne = await CreateRecipeAsync("Lasagne");
        var comfort = await CreateCollectionAsync("Comfort food");
        var quick = await CreateCollectionAsync("Doordeweeks");
        var comfortId = Guid.Parse(comfort.Id);
        var quickId = Guid.Parse(quick.Id);

        var sync = await Client.PutAsJsonAsync($"/api/recipes/{lasagne.Id}/cookbooks",
            new RecipeCookbooksRequestDto { CookbookIds = [comfortId, quickId] });
        sync.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await Client.GetFromJsonAsync<RecipeDto>($"/api/recipes/{lasagne.Id}");
        detail!.CookbookIds.Should().BeEquivalentTo([comfortId, quickId]);

        // Re-sync to a single collection removes the other membership
        await Client.PutAsJsonAsync($"/api/recipes/{lasagne.Id}/cookbooks",
            new RecipeCookbooksRequestDto { CookbookIds = [quickId] });
        detail = await Client.GetFromJsonAsync<RecipeDto>($"/api/recipes/{lasagne.Id}");
        detail!.CookbookIds.Should().Equal(quickId);
    }

    [Fact]
    public async Task DeleteRecipe_RemovesItsCollectionMemberships()
    {
        var lasagne = await CreateRecipeAsync("Lasagne");
        var collection = await CreateCollectionAsync("Comfort food");
        await Client.PostAsJsonAsync($"/api/cookbooks/{collection.Id}/recipes",
            new CookbookRecipesRequestDto { RecipeIds = [lasagne.Id] });

        (await Client.DeleteAsync($"/api/recipes/{lasagne.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await Client.GetFromJsonAsync<List<CookbookDto>>("/api/cookbooks");
        list!.Single(c => c.Id == collection.Id).RecipeCount.Should().Be(0);
    }

    [Fact]
    public async Task GetRecipes_FiltersByCollection_CombinableWithTags()
    {
        var lasagne = await CreateRecipeAsync("Lasagne", tags: ["oven"]);
        var stew = await CreateRecipeAsync("Stew", tags: ["stoof"]);
        await CreateRecipeAsync("Spaghetti", tags: ["oven"]);
        var collection = await CreateCollectionAsync("Comfort food");
        await Client.PostAsJsonAsync($"/api/cookbooks/{collection.Id}/recipes",
            new CookbookRecipesRequestDto { RecipeIds = [lasagne.Id, stew.Id] });

        var members = await Client.GetFromJsonAsync<List<RecipeListItemDto>>(
            $"/api/recipes?cookbookId={collection.Id}");
        members!.Select(r => r.Title).Should().Equal("Lasagne", "Stew");

        var combined = await Client.GetFromJsonAsync<List<RecipeListItemDto>>(
            $"/api/recipes?cookbookId={collection.Id}&tags=oven");
        combined!.Should().ContainSingle().Which.Title.Should().Be("Lasagne");
    }

    [Fact]
    public async Task AutoCollections_AreListedReadOnly_AndComputeMembers()
    {
        var quickRecipe = await CreateRecipeAsync("Wrap", totalTimeMinutes: 20);
        await CreateRecipeAsync("Stoofpot", totalTimeMinutes: 180);

        var list = await Client.GetFromJsonAsync<List<CookbookDto>>("/api/cookbooks");
        var quick = list!.Single(c => c.Id == "auto-quick");
        quick.Kind.Should().Be("auto");
        quick.RecipeCount.Should().Be(1);

        var members = await Client.GetFromJsonAsync<List<RecipeListItemDto>>("/api/cookbooks/auto-quick/recipes");
        members!.Should().ContainSingle().Which.Id.Should().Be(quickRecipe.Id);

        var filtered = await Client.GetFromJsonAsync<List<RecipeListItemDto>>("/api/recipes?cookbookId=auto-quick");
        filtered!.Should().ContainSingle().Which.Title.Should().Be("Wrap");

        // Auto collections are computed; membership cannot be edited (slug is not a Guid route)
        var mutate = await Client.PostAsJsonAsync("/api/cookbooks/auto-quick/recipes",
            new CookbookRecipesRequestDto { RecipeIds = [quickRecipe.Id] });
        mutate.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AutoCollections_CanBeDisabled_AndDropOutOfListAndFilter()
    {
        await CreateRecipeAsync("Wrap", totalTimeMinutes: 20);

        var autoList = await Client.GetFromJsonAsync<List<AutoCollectionDto>>("/api/cookbooks/auto-collections");
        autoList!.Should().Contain(c => c.Id == "auto-quick" && c.Enabled);

        var toggle = await Client.PutAsJsonAsync("/api/cookbooks/auto-collections/auto-quick",
            new ToggleAutoCollectionDto { Enabled = false });
        toggle.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // gone from the cookbooks list and the recipe filter…
        var cookbooks = await Client.GetFromJsonAsync<List<CookbookDto>>("/api/cookbooks");
        cookbooks!.Should().NotContain(c => c.Id == "auto-quick");
        var filtered = await Client.GetAsync("/api/recipes?cookbookId=auto-quick");
        filtered.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // …but still listed (disabled) in the management view, and its name stays reserved
        var afterList = await Client.GetFromJsonAsync<List<AutoCollectionDto>>("/api/cookbooks/auto-collections");
        afterList!.Should().Contain(c => c.Id == "auto-quick" && !c.Enabled);
        var reserved = await Client.PostAsJsonAsync("/api/cookbooks",
            new CreateCookbookDto { Name = "Quick (max 30 min)" });
        reserved.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // re-enabling brings it back
        await Client.PutAsJsonAsync("/api/cookbooks/auto-collections/auto-quick",
            new ToggleAutoCollectionDto { Enabled = true });
        (await Client.GetFromJsonAsync<List<CookbookDto>>("/api/cookbooks"))!
            .Should().Contain(c => c.Id == "auto-quick");
    }

    [Fact]
    public async Task DisableUnknownAutoCollection_ReturnsNotFound()
    {
        var response = await Client.PutAsJsonAsync("/api/cookbooks/auto-collections/auto-nope",
            new ToggleAutoCollectionDto { Enabled = false });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<CookbookDto> CreateCollectionAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/cookbooks", new CreateCookbookDto { Name = name });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CookbookDto>())!;
    }

    private async Task<RecipeDto> CreateRecipeAsync(
        string title, List<string>? tags = null, string? category = null, int? totalTimeMinutes = null)
    {
        var response = await Client.PostAsJsonAsync("/api/recipes", new CreateRecipeDto
        {
            Title = title,
            Category = category,
            TotalTimeMinutes = totalTimeMinutes,
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
