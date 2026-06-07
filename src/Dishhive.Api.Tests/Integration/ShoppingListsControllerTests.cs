using System.Net;
using System.Net.Http.Json;
using System.Text;
using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Models.Planning;
using Dishhive.Api.Models.Recipes;
using Dishhive.Api.Models.Shopping;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Dishhive.Api.Tests.Integration;

public class ShoppingListsControllerTests : TestBase
{
    [Fact]
    public async Task Generate_from_week_plan_aggregates_ingredients()
    {
        // Seed: plan with two slots referencing two recipes that share an ingredient.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            var pasta = new Recipe
            {
                Title = "Pasta",
                Servings = 2,
                Ingredients =
                {
                    new RecipeIngredient { Order = 0, Name = "Olive oil", Quantity = 30, Unit = "ml" },
                    new RecipeIngredient { Order = 1, Name = "Tomato", Quantity = 4, Unit = null }
                }
            };
            var soup = new Recipe
            {
                Title = "Soup",
                Servings = 2,
                Ingredients =
                {
                    new RecipeIngredient { Order = 0, Name = "olive oil", Quantity = 15, Unit = "ml" },
                    new RecipeIngredient { Order = 1, Name = "Onion", Quantity = 2, Unit = null }
                }
            };
            db.Recipes.AddRange(pasta, soup);

            var plan = new WeekPlan { WeekStart = new DateOnly(2026, 5, 4) };
            plan.Slots.Add(new MealSlot { DayOfWeek = DayOfWeek.Monday, MealType = MealType.Dinner, RecipeId = pasta.Id });
            plan.Slots.Add(new MealSlot { DayOfWeek = DayOfWeek.Tuesday, MealType = MealType.Dinner, RecipeId = soup.Id });
            plan.Slots.Add(new MealSlot { DayOfWeek = DayOfWeek.Wednesday, MealType = MealType.Dinner, VagueIntent = "leftovers" });
            plan.Slots.Add(new MealSlot { DayOfWeek = DayOfWeek.Thursday, MealType = MealType.Dinner, FrozenItemRef = "freezy-1" });

            db.WeekPlans.Add(plan);
            await db.SaveChangesAsync();

            // Set FK after Recipes have ids.
            plan.Slots.Where(s => s.DayOfWeek == DayOfWeek.Monday).First().RecipeId = pasta.Id;
            plan.Slots.Where(s => s.DayOfWeek == DayOfWeek.Tuesday).First().RecipeId = soup.Id;
            await db.SaveChangesAsync();
        }

        Guid planId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            planId = db.WeekPlans.First().Id;
        }

        var resp = await Client.PostAsync($"/api/shopping-lists/from-week-plan/{planId}", new StringContent(""));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await resp.Content.ReadFromJsonAsync<ShoppingListDto>();
        list.Should().NotBeNull();

        // Olive oil is merged across recipes (case-insensitive), summing 30 + 15 = 45 ml.
        var olive = list!.Items.Single(i => i.Name.Equals("Olive oil", StringComparison.OrdinalIgnoreCase));
        olive.Quantity.Should().Be(45m);
        olive.Unit.Should().Be("ml");

        // Tomato + Onion present.
        list.Items.Should().Contain(i => i.Name == "Tomato");
        list.Items.Should().Contain(i => i.Name == "Onion");

        // Freezy slot is skipped.
        list.Items.Should().NotContain(i => i.Name.Contains("freezy-1"));

        // Vague-intent reminder is included.
        list.Items.Should().Contain(i => i.Section == "Reminders" && i.Name.Contains("leftovers"));
    }

    [Fact]
    public async Task Generate_from_unknown_plan_returns_404()
    {
        var resp = await Client.PostAsync($"/api/shopping-lists/from-week-plan/{Guid.NewGuid()}", new StringContent(""));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Item_can_be_toggled_via_PUT()
    {
        var create = await Client.PostAsJsonAsync("/api/shopping-lists", new CreateShoppingListDto { Title = "manual" });
        var list = await create.Content.ReadFromJsonAsync<ShoppingListDto>();

        var add = await Client.PostAsJsonAsync($"/api/shopping-lists/{list!.Id}/items",
            new CreateShoppingListItemDto { Name = "Bread" });
        add.StatusCode.Should().Be(HttpStatusCode.Created);

        var added = await Client.GetFromJsonAsync<ShoppingListDto>($"/api/shopping-lists/{list.Id}");
        var item = added!.Items.Single();

        var put = await Client.PutAsJsonAsync($"/api/shopping-lists/{list.Id}/items/{item.Id}",
            new UpdateShoppingListItemDto { Name = "Bread", Checked = true });
        put.IsSuccessStatusCode.Should().BeTrue();

        var refreshed = await Client.GetFromJsonAsync<ShoppingListDto>($"/api/shopping-lists/{list.Id}");
        refreshed!.Items.Single().Checked.Should().BeTrue();
    }

    [Fact]
    public async Task Markdown_export_returns_text()
    {
        var create = await Client.PostAsJsonAsync("/api/shopping-lists", new CreateShoppingListDto { Title = "Export" });
        var list = await create.Content.ReadFromJsonAsync<ShoppingListDto>();
        await Client.PostAsJsonAsync($"/api/shopping-lists/{list!.Id}/items",
            new CreateShoppingListItemDto { Name = "Eggs", Quantity = 6 });

        var resp = await Client.GetAsync($"/api/shopping-lists/{list.Id}/export?format=markdown");
        resp.IsSuccessStatusCode.Should().BeTrue();
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("# Export");
        body.Should().Contain("Eggs");
    }
}
