using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class PlannedMealsControllerIntegrationTests : TestBase
{
    private static readonly DateOnly Monday = new(2026, 6, 15);

    [Fact]
    public async Task SetRecipe_LinksRecipeAndResolvesVagueInstruction()
    {
        var recipe = new Recipe { Title = "Lasagne verde" };
        var meal = new PlannedMeal { Date = Monday, VagueInstruction = "iets met pasta" };
        DbContext.Recipes.Add(recipe);
        DbContext.PlannedMeals.Add(meal);
        await DbContext.SaveChangesAsync();

        var response = await Client.PutAsJsonAsync(
            $"/api/plannedmeals/{meal.Id}/recipe", new SetMealRecipeDto { RecipeId = recipe.Id });
        var updated = await response.Content.ReadFromJsonAsync<PlannedMealDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.RecipeId.Should().Be(recipe.Id);
        updated.DishName.Should().Be("Lasagne verde");
        updated.VagueInstruction.Should().BeNull();
    }

    [Fact]
    public async Task SetRecipe_UnknownRecipe_ReturnsBadRequest()
    {
        var meal = new PlannedMeal { Date = Monday, DishName = "Iets" };
        DbContext.PlannedMeals.Add(meal);
        await DbContext.SaveChangesAsync();

        var response = await Client.PutAsJsonAsync(
            $"/api/plannedmeals/{meal.Id}/recipe", new SetMealRecipeDto { RecipeId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetRecipe_UnknownMeal_ReturnsNotFound()
    {
        var recipe = new Recipe { Title = "Lasagne" };
        DbContext.Recipes.Add(recipe);
        await DbContext.SaveChangesAsync();

        var response = await Client.PutAsJsonAsync(
            $"/api/plannedmeals/{Guid.NewGuid()}/recipe", new SetMealRecipeDto { RecipeId = recipe.Id });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMeals_ReturnsMealsInRange_OrderedByDate()
    {
        DbContext.PlannedMeals.AddRange(
            new PlannedMeal { Date = Monday.AddDays(2), DishName = "Vis" },
            new PlannedMeal { Date = Monday, DishName = "Spaghetti" },
            new PlannedMeal { Date = Monday.AddDays(10), DishName = "Buiten bereik" });
        await DbContext.SaveChangesAsync();

        var meals = await Client.GetFromJsonAsync<List<PlannedMealDto>>(
            $"/api/plannedmeals?from={Monday:yyyy-MM-dd}&to={Monday.AddDays(6):yyyy-MM-dd}");

        meals.Should().HaveCount(2);
        meals![0].DishName.Should().Be("Spaghetti");
        meals[1].DishName.Should().Be("Vis");
    }

    [Fact]
    public async Task GetMeals_InvalidRange_ReturnsBadRequest()
    {
        var response = await Client.GetAsync(
            $"/api/plannedmeals?from={Monday.AddDays(6):yyyy-MM-dd}&to={Monday:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMeal_WithDishName_CreatesMeal()
    {
        var dto = new CreatePlannedMealDto { Date = Monday, DishName = "Stoofvlees" };

        var response = await Client.PostAsJsonAsync("/api/plannedmeals", dto);
        var created = await response.Content.ReadFromJsonAsync<PlannedMealDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.DishName.Should().Be("Stoofvlees");
        created.MealType.Should().Be(MealType.Dinner);
    }

    [Fact]
    public async Task CreateMeal_WithVagueInstructionOnly_CreatesMeal()
    {
        var dto = new CreatePlannedMealDto { Date = Monday, VagueInstruction = "iets met vis" };

        var response = await Client.PostAsJsonAsync("/api/plannedmeals", dto);
        var created = await response.Content.ReadFromJsonAsync<PlannedMealDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.VagueInstruction.Should().Be("iets met vis");
        created.DishName.Should().BeNull();
    }

    [Fact]
    public async Task CreateMeal_WithoutAnyContent_ReturnsBadRequest()
    {
        var dto = new CreatePlannedMealDto { Date = Monday };

        var response = await Client.PostAsJsonAsync("/api/plannedmeals", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMeal_MultipleDishesInSameMeal_AreAllowed()
    {
        DbContext.PlannedMeals.Add(new PlannedMeal { Date = Monday, MealType = MealType.Dinner, DishName = "Hoofdgerecht" });
        await DbContext.SaveChangesAsync();

        var appetizer = new CreatePlannedMealDto
        {
            Date = Monday, MealType = MealType.Dinner, Course = Course.Appetizer, DishName = "Soepje"
        };
        var dessert = new CreatePlannedMealDto
        {
            Date = Monday, MealType = MealType.Dinner, Course = Course.Dessert, DishName = "Tiramisu"
        };

        (await Client.PostAsJsonAsync("/api/plannedmeals", appetizer)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await Client.PostAsJsonAsync("/api/plannedmeals", dessert)).StatusCode.Should().Be(HttpStatusCode.Created);

        var meals = await Client.GetFromJsonAsync<List<PlannedMealDto>>(
            $"/api/plannedmeals?from={Monday:yyyy-MM-dd}&to={Monday:yyyy-MM-dd}");

        meals.Should().HaveCount(3);
    }

    [Fact]
    public async Task CreateMeal_DefaultsToMainCourse_AndPersistsCourse()
    {
        var dto = new CreatePlannedMealDto { Date = Monday, DishName = "Stoofvlees" };

        var response = await Client.PostAsJsonAsync("/api/plannedmeals", dto);
        var created = await response.Content.ReadFromJsonAsync<PlannedMealDto>();

        created!.Course.Should().Be(Course.Main);

        var dessertDto = new CreatePlannedMealDto { Date = Monday, Course = Course.Dessert, DishName = "Rijstpap" };
        var dessertResponse = await Client.PostAsJsonAsync("/api/plannedmeals", dessertDto);
        var dessert = await dessertResponse.Content.ReadFromJsonAsync<PlannedMealDto>();

        dessert!.Course.Should().Be(Course.Dessert);
    }

    [Fact]
    public async Task CreateMeal_SameDateDifferentMealType_IsAllowed()
    {
        DbContext.PlannedMeals.Add(new PlannedMeal { Date = Monday, MealType = MealType.Dinner, DishName = "Avond" });
        await DbContext.SaveChangesAsync();

        var dto = new CreatePlannedMealDto { Date = Monday, MealType = MealType.Lunch, DishName = "Middag" };
        var response = await Client.PostAsJsonAsync("/api/plannedmeals", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateMeal_WithRecipe_DenormalizesDishNameFromTitle()
    {
        var recipe = new Recipe { Title = "Lasagne van de chef" };
        DbContext.Recipes.Add(recipe);
        await DbContext.SaveChangesAsync();

        var dto = new CreatePlannedMealDto { Date = Monday, RecipeId = recipe.Id };
        var response = await Client.PostAsJsonAsync("/api/plannedmeals", dto);
        var created = await response.Content.ReadFromJsonAsync<PlannedMealDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.DishName.Should().Be("Lasagne van de chef");
        created.RecipeTitle.Should().Be("Lasagne van de chef");
    }

    [Fact]
    public async Task CreateMeal_WithUnknownRecipe_ReturnsBadRequest()
    {
        var dto = new CreatePlannedMealDto { Date = Monday, RecipeId = Guid.NewGuid() };

        var response = await Client.PostAsJsonAsync("/api/plannedmeals", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMeal_WithAttendees_PersistsAttendeeIds()
    {
        var anna = new FamilyMember { Name = "Anna" };
        var bert = new FamilyMember { Name = "Bert" };
        DbContext.FamilyMembers.AddRange(anna, bert);
        await DbContext.SaveChangesAsync();

        var dto = new CreatePlannedMealDto
        {
            Date = Monday,
            DishName = "Frietjes",
            FamilyMemberIds = [anna.Id, bert.Id]
        };

        var response = await Client.PostAsJsonAsync("/api/plannedmeals", dto);
        var created = await response.Content.ReadFromJsonAsync<PlannedMealDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.AttendeeIds.Should().BeEquivalentTo([anna.Id, bert.Id]);
    }

    [Fact]
    public async Task UpdateMeal_ReplacesContentAndAttendees()
    {
        var anna = new FamilyMember { Name = "Anna" };
        var bert = new FamilyMember { Name = "Bert" };
        var meal = new PlannedMeal
        {
            Date = Monday,
            DishName = "Origineel",
            Attendees = { new PlannedMealAttendee { FamilyMember = anna } }
        };
        DbContext.FamilyMembers.Add(bert);
        DbContext.PlannedMeals.Add(meal);
        await DbContext.SaveChangesAsync();

        var dto = new UpdatePlannedMealDto
        {
            Date = Monday,
            DishName = "Bijgewerkt",
            FamilyMemberIds = [bert.Id]
        };

        var response = await Client.PutAsJsonAsync($"/api/plannedmeals/{meal.Id}", dto);
        var updated = await response.Content.ReadFromJsonAsync<PlannedMealDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.DishName.Should().Be("Bijgewerkt");
        updated.AttendeeIds.Should().BeEquivalentTo([bert.Id]);
    }

    [Fact]
    public async Task DeleteMeal_RemovesMeal()
    {
        var meal = new PlannedMeal { Date = Monday, DishName = "Weg ermee" };
        DbContext.PlannedMeals.Add(meal);
        await DbContext.SaveChangesAsync();

        var response = await Client.DeleteAsync($"/api/plannedmeals/{meal.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var freshContext = CreateFreshContext();
        freshContext.PlannedMeals.Should().NotContain(m => m.Id == meal.Id);
    }
}
