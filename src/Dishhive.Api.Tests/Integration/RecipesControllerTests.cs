using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class RecipesControllerTests : TestBase
{
    private static CreateRecipeDto Sample(string title = "Spaghetti Bolognese") => new()
    {
        Title = title,
        Description = "Classic.",
        Servings = 4,
        ImageUrl = "https://example.com/img.jpg",
        Ingredients =
        {
            new() { Order = 0, Name = "spaghetti", Quantity = 400, Unit = "g" },
            new() { Order = 1, Name = "minced beef", Quantity = 500, Unit = "g" },
            new() { Order = 2, Name = "tomato passata", Quantity = 700, Unit = "ml" },
        },
        Steps =
        {
            new() { Order = 0, Text = "Brown the beef." },
            new() { Order = 1, Text = "Add passata, simmer 30 min." },
        },
        Tags = { "italian", "pasta", "weeknight" },
    };

    [Fact]
    public async Task Create_persists_full_aggregate()
    {
        var post = await Client.PostAsJsonAsync("/api/recipes", Sample());
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await post.Content.ReadFromJsonAsync<RecipeDto>();
        created!.Ingredients.Should().HaveCount(3);
        created.Steps.Should().HaveCount(2);
        created.Tags.Should().Contain("italian");
    }

    [Fact]
    public async Task Search_by_title_is_case_insensitive()
    {
        await Client.PostAsJsonAsync("/api/recipes", Sample("Pannekoeken"));
        await Client.PostAsJsonAsync("/api/recipes", Sample("Lasagne"));

        var results = await Client.GetFromJsonAsync<List<RecipeSummaryDto>>("/api/recipes?search=PANNE");
        results.Should().ContainSingle(r => r.Title == "Pannekoeken");
    }

    [Fact]
    public async Task Update_replaces_children()
    {
        var post = await Client.PostAsJsonAsync("/api/recipes", Sample());
        var created = await post.Content.ReadFromJsonAsync<RecipeDto>();

        var update = new UpdateRecipeDto
        {
            Title = "Renamed",
            Servings = 6,
            Ingredients = { new() { Order = 0, Name = "olive oil", Quantity = 2, Unit = "tbsp" } },
            Steps = { new() { Order = 0, Text = "Simpler now." } },
            Tags = { "italian" },
        };

        var put = await Client.PutAsJsonAsync($"/api/recipes/{created!.Id}", update);
        put.IsSuccessStatusCode.Should().BeTrue();

        var refreshed = await Client.GetFromJsonAsync<RecipeDto>($"/api/recipes/{created.Id}");
        refreshed!.Title.Should().Be("Renamed");
        refreshed.Servings.Should().Be(6);
        refreshed.Ingredients.Should().HaveCount(1);
        refreshed.Steps.Should().HaveCount(1);
        refreshed.Tags.Should().BeEquivalentTo(new[] { "italian" });
    }

    [Fact]
    public async Task Tags_endpoint_returns_distinct_sorted()
    {
        await Client.PostAsJsonAsync("/api/recipes", Sample("a"));
        await Client.PostAsJsonAsync("/api/recipes", Sample("b"));

        var tags = await Client.GetFromJsonAsync<List<string>>("/api/recipes/tags");
        tags.Should().BeEquivalentTo(new[] { "italian", "pasta", "weeknight" }, opt => opt.WithStrictOrdering());
    }

    [Fact]
    public async Task Delete_removes_recipe()
    {
        var post = await Client.PostAsJsonAsync("/api/recipes", Sample("Drop me"));
        var created = await post.Content.ReadFromJsonAsync<RecipeDto>();

        var del = await Client.DeleteAsync($"/api/recipes/{created!.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Client.GetAsync($"/api/recipes/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
