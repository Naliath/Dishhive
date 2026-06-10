using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.ShoppingList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// Tests for the shopping list math: scaling by attendance, aggregation across recipes,
/// freezer-meal skipping, pass-through lines and reminders
/// </summary>
public class ShoppingListServiceTests : IDisposable
{
    private static readonly DateOnly Monday = new(2026, 6, 15);

    private readonly DishhiveDbContext _context;
    private readonly ShoppingListService _service;

    public ShoppingListServiceTests()
    {
        var options = new DbContextOptionsBuilder<DishhiveDbContext>()
            .UseInMemoryDatabase($"ShoppingListTests_{Guid.NewGuid()}")
            .Options;
        _context = new DishhiveDbContext(options);
        _service = new ShoppingListService(_context);
    }

    private static Recipe RecipeWith(string title, int servings, params RecipeIngredient[] ingredients)
    {
        var recipe = new Recipe { Title = title, Servings = servings };
        recipe.Ingredients.AddRange(ingredients);
        return recipe;
    }

    private static RecipeIngredient Ingredient(string name, decimal? quantity, string? unit)
        => new() { Name = name, Quantity = quantity, Unit = unit, OriginalText = $"{quantity} {unit} {name}".Trim() };

    [Fact]
    public async Task Generate_ScalesQuantitiesByAttendeesVersusServings()
    {
        var recipe = RecipeWith("Puree", servings: 4, Ingredient("aardappelen", 1000m, "g"));
        var meal = new PlannedMeal
        {
            Date = Monday,
            Recipe = recipe,
            DishName = recipe.Title,
            Attendees =
            {
                new PlannedMealAttendee { FamilyMember = new FamilyMember { Name = "Anna" } },
                new PlannedMealAttendee { FamilyMember = new FamilyMember { Name = "Bert" } }
            }
        };
        _context.PlannedMeals.Add(meal);
        await _context.SaveChangesAsync();

        var list = await _service.GenerateAsync(Monday, Monday.AddDays(6));

        // 2 attendees / 4 servings = factor 0.5
        list.Items.Should().ContainSingle(i => i.Name == "aardappelen" && i.Quantity == 500m && i.Unit == "g");
    }

    [Fact]
    public async Task Generate_WithoutAttendees_UsesFactorOne()
    {
        var recipe = RecipeWith("Soep", servings: 4, Ingredient("wortelen", 300m, "g"));
        _context.PlannedMeals.Add(new PlannedMeal { Date = Monday, Recipe = recipe, DishName = recipe.Title });
        await _context.SaveChangesAsync();

        var list = await _service.GenerateAsync(Monday, Monday);

        list.Items.Should().ContainSingle(i => i.Quantity == 300m);
    }

    [Fact]
    public async Task Generate_AggregatesSameIngredientAcrossRecipes()
    {
        var pasta = RecipeWith("Pasta", servings: 1, Ingredient("uien", 100m, "g"));
        var stoofvlees = RecipeWith("Stoofvlees", servings: 1, Ingredient("Uien", 200m, "g"));
        _context.PlannedMeals.AddRange(
            new PlannedMeal { Date = Monday, Recipe = pasta, DishName = pasta.Title },
            new PlannedMeal { Date = Monday.AddDays(1), Recipe = stoofvlees, DishName = stoofvlees.Title });
        await _context.SaveChangesAsync();

        var list = await _service.GenerateAsync(Monday, Monday.AddDays(6));

        var onions = list.Items.Should().ContainSingle(i => i.Quantity == 300m).Subject;
        onions.SourceRecipes.Should().BeEquivalentTo("Pasta", "Stoofvlees");
    }

    [Fact]
    public async Task Generate_SkipsFreezerSourcedMeals()
    {
        var recipe = RecipeWith("Lasagne", servings: 4, Ingredient("gehakt", 500m, "g"));
        _context.PlannedMeals.Add(new PlannedMeal
        {
            Date = Monday,
            Recipe = recipe,
            DishName = recipe.Title,
            FreezyItemRef = "freezy-item-1"
        });
        await _context.SaveChangesAsync();

        var list = await _service.GenerateAsync(Monday, Monday);

        list.Items.Should().BeEmpty();
        list.Reminders.Should().BeEmpty();
    }

    [Fact]
    public async Task Generate_MealsWithoutRecipe_BecomeReminders()
    {
        _context.PlannedMeals.AddRange(
            new PlannedMeal { Date = Monday, DishName = "Frietjes van de frituur" },
            new PlannedMeal { Date = Monday.AddDays(1), VagueInstruction = "iets met vis" });
        await _context.SaveChangesAsync();

        var list = await _service.GenerateAsync(Monday, Monday.AddDays(6));

        list.Reminders.Should().HaveCount(2);
        list.Reminders[0].Text.Should().Be("Frietjes van de frituur");
        list.Reminders[1].Text.Should().Be("iets met vis");
    }

    [Fact]
    public async Task Generate_UnparseableIngredients_PassThroughWithoutQuantity()
    {
        var recipe = RecipeWith("Dessert", servings: 4,
            new RecipeIngredient { Name = "Cointreau", OriginalText = "Cointreau" });
        _context.PlannedMeals.Add(new PlannedMeal { Date = Monday, Recipe = recipe, DishName = recipe.Title });
        await _context.SaveChangesAsync();

        var list = await _service.GenerateAsync(Monday, Monday);

        list.Items.Should().ContainSingle(i => i.Name == "Cointreau" && i.Quantity == null && i.Unit == null);
    }

    [Fact]
    public async Task Generate_ExcludesMealsOutsideRange()
    {
        var recipe = RecipeWith("Pasta", servings: 1, Ingredient("pasta", 500m, "g"));
        _context.PlannedMeals.Add(new PlannedMeal { Date = Monday.AddDays(10), Recipe = recipe, DishName = recipe.Title });
        await _context.SaveChangesAsync();

        var list = await _service.GenerateAsync(Monday, Monday.AddDays(6));

        list.Items.Should().BeEmpty();
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
