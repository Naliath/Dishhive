using Dishhive.Api.Data;
using Dishhive.Api.Mcp;
using Dishhive.Api.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// MCP tool behavior against an in-memory database. The tools return anonymous
/// objects that the MCP server serializes to JSON for the client, so assertions
/// go through the same serialization (what the AI actually sees).
/// </summary>
public class DishhiveMcpToolsTests : IDisposable
{
    // 2026-06-15 is a Monday
    private static readonly DateOnly Monday = new(2026, 6, 15);

    private readonly DishhiveDbContext _context;
    private readonly DishhiveMcpTools _tools;

    public DishhiveMcpToolsTests()
    {
        var options = new DbContextOptionsBuilder<DishhiveDbContext>()
            .UseInMemoryDatabase($"McpToolsTests_{Guid.NewGuid()}")
            .Options;
        _context = new DishhiveDbContext(options);
        _tools = new DishhiveMcpTools(_context);
    }

    public void Dispose() => _context.Dispose();

    private static JsonElement ToJson(object result) => JsonSerializer.SerializeToElement(result);

    private FamilyMember AddMember(string name)
    {
        var member = new FamilyMember { Id = Guid.NewGuid(), Name = name };
        _context.FamilyMembers.Add(member);
        return member;
    }

    private PlannedMeal AddMeal(DateOnly date, string dishName,
        EatenStatus? eaten = null, MealType mealType = MealType.Dinner)
    {
        var meal = new PlannedMeal { Id = Guid.NewGuid(), Date = date, DishName = dishName, MealType = mealType, Eaten = eaten };
        _context.PlannedMeals.Add(meal);
        return meal;
    }

    private Recipe AddRecipe(string title, string? category = null, string? keywords = null)
    {
        var recipe = new Recipe { Id = Guid.NewGuid(), Title = title, Category = category, Keywords = keywords };
        _context.Recipes.Add(recipe);
        return recipe;
    }

    // ------------------------------------------------------------------ week plan

    [Fact]
    public async Task GetWeekPlan_MidWeekDate_NormalizesToMonday()
    {
        AddMeal(Monday, "Maandagkost");
        AddMeal(Monday.AddDays(6), "Zondagskost");
        AddMeal(Monday.AddDays(7), "Volgende week"); // outside
        await _context.SaveChangesAsync();

        // Wednesday of the same week
        var result = ToJson(await _tools.GetWeekPlan(Monday.AddDays(2).ToString("yyyy-MM-dd")));

        result.GetProperty("weekStart").GetString().Should().Be("2026-06-15");
        result.GetProperty("weekEnd").GetString().Should().Be("2026-06-21");
        var dishes = result.GetProperty("meals").EnumerateArray()
            .Select(m => m.GetProperty("dish").GetString()).ToList();
        dishes.Should().Equal("Maandagkost", "Zondagskost");
    }

    [Fact]
    public async Task GetWeekPlan_InvalidDate_ReturnsHelpfulError()
    {
        var result = await _tools.GetWeekPlan("next tuesday");

        result.Should().BeOfType<string>().Which.Should().Contain("yyyy-MM-dd");
    }

    [Fact]
    public async Task GetWeekPlan_ResolvesAttendeeNames()
    {
        var anna = AddMember("Anna");
        var meal = AddMeal(Monday, "Stoofpot");
        meal.Attendees.Add(new PlannedMealAttendee { PlannedMeal = meal, FamilyMemberId = anna.Id });
        await _context.SaveChangesAsync();

        var result = ToJson(await _tools.GetWeekPlan(Monday.ToString("yyyy-MM-dd")));

        result.GetProperty("meals")[0].GetProperty("attendees")[0].GetString().Should().Be("Anna");
    }

    // ------------------------------------------------------------- eaten on date

    [Fact]
    public async Task GetEatenOnDate_RendersAllThreeEatenStates()
    {
        AddMeal(Monday, "Gegeten", EatenStatus.Eaten);
        AddMeal(Monday, "Overgeslagen", EatenStatus.Skipped, MealType.Lunch);
        AddMeal(Monday, "Onbeoordeeld", eaten: null, MealType.Breakfast);
        await _context.SaveChangesAsync();

        var result = ToJson(await _tools.GetEatenOnDate(Monday.ToString("yyyy-MM-dd")));

        var byDish = result.GetProperty("meals").EnumerateArray()
            .ToDictionary(m => m.GetProperty("dish").GetString()!, m => m.GetProperty("eaten").GetString());
        byDish["Gegeten"].Should().Be("eaten");
        byDish["Overgeslagen"].Should().Be("skipped");
        byDish["Onbeoordeeld"].Should().Be("not marked");
    }

    [Fact]
    public async Task GetEatenOnDate_IncludesPerMemberRatingsWithNames()
    {
        var anna = AddMember("Anna");
        var bart = AddMember("Bart");
        var meal = AddMeal(Monday, "Lasagne", EatenStatus.Eaten);
        meal.Ratings.Add(new MealRating { PlannedMeal = meal, FamilyMemberId = anna.Id, Rating = 5 });
        meal.Ratings.Add(new MealRating { PlannedMeal = meal, FamilyMemberId = bart.Id, Rating = 3 });
        await _context.SaveChangesAsync();

        var result = ToJson(await _tools.GetEatenOnDate(Monday.ToString("yyyy-MM-dd")));

        var ratings = result.GetProperty("meals")[0].GetProperty("ratings").EnumerateArray()
            .Select(r => (r.GetProperty("member").GetString(), r.GetProperty("rating").GetInt32()))
            .ToList();
        ratings.Should().Equal(("Anna", 5), ("Bart", 3));
    }

    [Fact]
    public async Task GetEatenOnDate_EmptyDay_ExplainsInsteadOfEmptyList()
    {
        var result = await _tools.GetEatenOnDate("2026-06-15");

        result.Should().BeOfType<string>().Which.Should().Contain("Nothing was planned on 2026-06-15");
    }

    // ------------------------------------------------------------ recipe search

    [Fact]
    public async Task SearchRecipes_MatchesTitleAndKeywords()
    {
        AddRecipe("Spaghetti bolognese");
        AddRecipe("Witloofrolletjes", keywords: "klassieker, spaghetti-alternatief");
        AddRecipe("Viscurry");
        await _context.SaveChangesAsync();

        var result = ToJson(await _tools.SearchRecipes(query: "spaghetti"));

        result.GetProperty("totalMatches").GetInt32().Should().Be(2);
        result.GetProperty("recipes").EnumerateArray()
            .Select(r => r.GetProperty("title").GetString())
            .Should().BeEquivalentTo("Spaghetti bolognese", "Witloofrolletjes");
    }

    [Fact]
    public async Task SearchRecipes_TagsAreAndMatched()
    {
        var quick = new RecipeTag { Id = Guid.NewGuid(), Name = "quick" };
        var pasta = new RecipeTag { Id = Guid.NewGuid(), Name = "pasta" };
        _context.RecipeTags.AddRange(quick, pasta);
        var both = AddRecipe("Snelle pasta");
        both.Tags.Add(new RecipeTagAssignment { Recipe = both, RecipeTag = quick });
        both.Tags.Add(new RecipeTagAssignment { Recipe = both, RecipeTag = pasta });
        var onlyQuick = AddRecipe("Snelle wok");
        onlyQuick.Tags.Add(new RecipeTagAssignment { Recipe = onlyQuick, RecipeTag = quick });
        await _context.SaveChangesAsync();

        var result = ToJson(await _tools.SearchRecipes(tags: "quick, pasta"));

        result.GetProperty("recipes").EnumerateArray()
            .Select(r => r.GetProperty("title").GetString())
            .Should().Equal("Snelle pasta");
    }

    [Fact]
    public async Task SearchRecipes_CapsAt25AndSaysSo()
    {
        for (var i = 1; i <= 30; i++)
        {
            AddRecipe($"Gerecht {i:00}");
        }
        await _context.SaveChangesAsync();

        var result = ToJson(await _tools.SearchRecipes());

        result.GetProperty("totalMatches").GetInt32().Should().Be(30);
        result.GetProperty("returned").GetInt32().Should().Be(25);
        result.GetProperty("note").GetString().Should().Contain("first 25 of 30");
    }

    [Fact]
    public async Task SearchRecipes_IncludesDietaryFacts()
    {
        var recipe = AddRecipe("Notencake");
        recipe.DietaryFactsStatus = DietaryFactsStatus.AiDetected;
        recipe.DietaryFacts.Add(new RecipeDietaryFact { Recipe = recipe, IngredientClass = IngredientClass.TreeNuts });
        await _context.SaveChangesAsync();

        var result = ToJson(await _tools.SearchRecipes(query: "noten"));

        var hit = result.GetProperty("recipes")[0];
        hit.GetProperty("factsAssessed").GetBoolean().Should().BeTrue();
        hit.GetProperty("contains")[0].GetString().Should().Be("TreeNuts");
    }

    // ------------------------------------------------------------- recipe detail

    [Fact]
    public async Task GetRecipe_ReturnsIngredientsStepsAndFacts()
    {
        var recipe = AddRecipe("Stoofvlees", category: "Hoofdgerecht");
        recipe.Ingredients.Add(new RecipeIngredient
        {
            Recipe = recipe, SortOrder = 0, Name = "rundvlees", OriginalText = "500 g rundvlees"
        });
        recipe.Steps.Add(new RecipeStep { Recipe = recipe, StepNumber = 1, Instruction = "Stoof het vlees." });
        recipe.DietaryFactsStatus = DietaryFactsStatus.UserConfirmed;
        recipe.DietaryFacts.Add(new RecipeDietaryFact { Recipe = recipe, IngredientClass = IngredientClass.RedMeat });
        await _context.SaveChangesAsync();

        var result = ToJson(await _tools.GetRecipe(recipe.Id.ToString()));

        result.GetProperty("title").GetString().Should().Be("Stoofvlees");
        result.GetProperty("ingredients")[0].GetString().Should().Be("500 g rundvlees");
        result.GetProperty("steps")[0].GetString().Should().Be("Stoof het vlees.");
        result.GetProperty("dietaryFacts").GetProperty("contains")[0].GetString().Should().Be("RedMeat");
        result.GetProperty("dietaryFacts").GetProperty("status").GetString().Should().Be("UserConfirmed");
    }

    [Fact]
    public async Task GetRecipe_InvalidId_ReturnsHelpfulError()
    {
        (await _tools.GetRecipe("not-a-guid")).Should().BeOfType<string>()
            .Which.Should().Contain("not a valid recipe id");
        (await _tools.GetRecipe(Guid.NewGuid().ToString())).Should().BeOfType<string>()
            .Which.Should().Contain("No recipe with id");
    }
}
