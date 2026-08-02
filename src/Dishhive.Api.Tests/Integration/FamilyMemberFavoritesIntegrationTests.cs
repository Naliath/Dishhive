using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class FamilyMemberFavoritesIntegrationTests : TestBase
{
    private async Task<FamilyMember> AddMemberAsync(string name = "Anna")
    {
        var member = new FamilyMember { Name = name };
        DbContext.FamilyMembers.Add(member);
        await DbContext.SaveChangesAsync();
        return member;
    }

    [Fact]
    public async Task AddFavorite_WithDishName_CreatesFavorite()
    {
        var member = await AddMemberAsync();

        var response = await Client.PostAsJsonAsync(
            $"/api/familymembers/{member.Id}/favorites",
            new CreateFamilyMemberFavoriteDto { DishName = "Frietjes" });
        var created = await response.Content.ReadFromJsonAsync<FamilyMemberFavoriteDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.DishName.Should().Be("Frietjes");
        created.RecipeId.Should().BeNull();
    }

    [Fact]
    public async Task AddFavorite_WithRecipe_DenormalizesDishNameFromTitle()
    {
        var member = await AddMemberAsync();
        var recipe = new Recipe { Title = "Lasagne" };
        DbContext.Recipes.Add(recipe);
        await DbContext.SaveChangesAsync();

        var response = await Client.PostAsJsonAsync(
            $"/api/familymembers/{member.Id}/favorites",
            new CreateFamilyMemberFavoriteDto { RecipeId = recipe.Id });
        var created = await response.Content.ReadFromJsonAsync<FamilyMemberFavoriteDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.RecipeId.Should().Be(recipe.Id);
        created.DishName.Should().Be("Lasagne");
    }

    [Fact]
    public async Task AddFavorite_WithRecipeAndStaleText_PreservesLinkAndUsesCurrentRecipeTitle()
    {
        var member = await AddMemberAsync();
        var recipe = new Recipe
        {
            Title = "Green lasagna",
            OriginalTitle = "Groene lasagne",
            ContentLanguage = "en"
        };
        DbContext.Recipes.Add(recipe);
        await DbContext.SaveChangesAsync();

        var response = await Client.PostAsJsonAsync(
            $"/api/familymembers/{member.Id}/favorites",
            new CreateFamilyMemberFavoriteDto
            {
                RecipeId = recipe.Id,
                DishName = "Groene lasagne"
            });
        var created = await response.Content.ReadFromJsonAsync<FamilyMemberFavoriteDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.RecipeId.Should().Be(recipe.Id);
        created.DishName.Should().Be("Green lasagna");
        using var freshContext = CreateFreshContext();
        freshContext.FamilyMemberFavorites.Should().ContainSingle(favorite =>
            favorite.RecipeId == recipe.Id && favorite.DishName == "Green lasagna");
    }

    [Fact]
    public async Task AddFavorite_WithoutContent_ReturnsBadRequest()
    {
        var member = await AddMemberAsync();

        var response = await Client.PostAsJsonAsync(
            $"/api/familymembers/{member.Id}/favorites",
            new CreateFamilyMemberFavoriteDto());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddFavorite_UnknownMember_ReturnsNotFound()
    {
        var response = await Client.PostAsJsonAsync(
            $"/api/familymembers/{Guid.NewGuid()}/favorites",
            new CreateFamilyMemberFavoriteDto { DishName = "Frietjes" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFavorites_ReturnsMemberFavoritesOnly()
    {
        var anna = await AddMemberAsync("Anna");
        var bert = await AddMemberAsync("Bert");
        DbContext.FamilyMemberFavorites.AddRange(
            new FamilyMemberFavorite { FamilyMemberId = anna.Id, DishName = "Pasta" },
            new FamilyMemberFavorite { FamilyMemberId = bert.Id, DishName = "Vis" });
        await DbContext.SaveChangesAsync();

        var favorites = await Client.GetFromJsonAsync<List<FamilyMemberFavoriteDto>>(
            $"/api/familymembers/{anna.Id}/favorites");

        favorites.Should().ContainSingle(f => f.DishName == "Pasta");
    }

    [Fact]
    public async Task GetFavorites_LinkedRecipeUsesItsCurrentTitleWhileFreeTextStaysUnchanged()
    {
        var member = await AddMemberAsync();
        var recipe = new Recipe
        {
            Title = "Green lasagna",
            OriginalTitle = "Groene lasagne",
            ContentLanguage = "en"
        };
        DbContext.Recipes.Add(recipe);
        DbContext.FamilyMemberFavorites.AddRange(
            new FamilyMemberFavorite
            {
                FamilyMemberId = member.Id,
                Recipe = recipe,
                DishName = "Groene lasagne"
            },
            new FamilyMemberFavorite
            {
                FamilyMemberId = member.Id,
                DishName = "Groene lasagne"
            });
        await DbContext.SaveChangesAsync();

        var favorites = await Client.GetFromJsonAsync<List<FamilyMemberFavoriteDto>>(
            $"/api/familymembers/{member.Id}/favorites");

        favorites.Should().ContainSingle(favorite =>
            favorite.RecipeId == recipe.Id && favorite.DishName == "Green lasagna");
        favorites.Should().ContainSingle(favorite =>
            favorite.RecipeId == null && favorite.DishName == "Groene lasagne");
    }

    [Fact]
    public async Task DeleteFavorite_RemovesFavorite()
    {
        var member = await AddMemberAsync();
        var favorite = new FamilyMemberFavorite { FamilyMemberId = member.Id, DishName = "Pasta" };
        DbContext.FamilyMemberFavorites.Add(favorite);
        await DbContext.SaveChangesAsync();

        var response = await Client.DeleteAsync($"/api/familymembers/{member.Id}/favorites/{favorite.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var freshContext = CreateFreshContext();
        freshContext.FamilyMemberFavorites.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteRecipe_FavoriteKeepsDenormalizedDishName()
    {
        var member = await AddMemberAsync();
        var recipe = new Recipe { Title = "Verdwijnende stoofpot" };
        DbContext.Recipes.Add(recipe);
        DbContext.FamilyMemberFavorites.Add(new FamilyMemberFavorite
        {
            FamilyMemberId = member.Id,
            Recipe = recipe,
            DishName = recipe.Title
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var response = await Client.DeleteAsync($"/api/recipes/{recipe.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var freshContext = CreateFreshContext();
        var favorite = freshContext.FamilyMemberFavorites.Single();
        favorite.DishName.Should().Be("Verdwijnende stoofpot");
        favorite.RecipeId.Should().BeNull();
    }
}
