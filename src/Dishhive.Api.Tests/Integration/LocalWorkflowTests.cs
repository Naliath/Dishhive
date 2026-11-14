using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Dishhive.Api.Tests.Integration;

/// <summary>HTTP workflows against migrated disposable PostgreSQL, with no live model or network fetches.</summary>
public sealed class LocalWorkflowTests : IAsyncLifetime
{
    private LocalWorkflowFactory _factory = null!;
    private HttpClient _client = null!;
    private static DateOnly Day => DateOnly.FromDateTime(DateTime.Today).AddDays(14);
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "dishhive-web", "package.json")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Cannot locate Dishhive source root");
        }
    }

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("DISHHIVE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var connection = new NpgsqlConnectionStringBuilder(configured) { Database = $"dishhive_workflow_{Guid.NewGuid():N}" };
        var browser = Environment.GetEnvironmentVariable("DISHHIVE_TEST_BROWSER") == "1";
        var webRoot = browser ? Path.Combine(Root, "src", "dishhive-web", "dist", "dishhive-web", "browser") : null;
        _factory = new LocalWorkflowFactory(connection.ConnectionString, webRoot);
        if (browser) _factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        _client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DishhiveDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is null) return;
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DishhiveDbContext>().Database.EnsureDeletedAsync();
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [PostgresFact]
    public async Task ImportEditGenerateAccept_RetainsRecipeGuestsShoppingQuantitiesAndIdempotency()
    {
        var (recipe, household, guest) = await Prepare();
        var idea = await _client.PostAsJsonAsync("/api/plannedmeals", new { date = Day, vagueInstruction = "Choose a dish" });
        idea.StatusCode.Should().Be(HttpStatusCode.Created);
        var batch = await Generate([household.Id, guest.Id]);
        var suggestion = batch.Suggestions.Should().ContainSingle().Subject;
        suggestion.RecipeId.Should().Be(recipe.Id);
        suggestion.DishName.Should().Be(recipe.Title);
        suggestion.AttendeeIds.Should().BeEquivalentTo([household.Id, guest.Id]);
        suggestion.CandidateVersion.Should().NotBeNullOrWhiteSpace();

        var first = await Accept(batch);
        first.Status.Should().Be("created");
        var retry = await Accept(batch);
        retry.Status.Should().Be("alreadyApplied");
        retry.PlannedMealId.Should().Be(first.PlannedMealId);
        var meals = (await _client.GetFromJsonAsync<List<PlannedMealDto>>($"/api/plannedmeals?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}"))!;
        var saved = meals.Should().ContainSingle().Subject;
        saved.RecipeId.Should().Be(recipe.Id);
        saved.RecipeTitle.Should().Be(recipe.Title);
        saved.AttendeeIds.Should().BeEquivalentTo([household.Id, guest.Id]);
        saved.VagueInstruction.Should().BeNull();
        var shopping = (await _client.GetFromJsonAsync<ShoppingListDto>($"/api/shoppinglist?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}"))!;
        var ingredient = shopping.Items.Should().ContainSingle().Subject;
        ingredient.Name.Should().Be("rice");
        ingredient.Quantity.Should().Be(50m); // 100g / four recipe servings * two selected people
        ingredient.Unit.Should().Be("g");
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        (await context.PlannedMeals.CountAsync()).Should().Be(1);
        (await context.PlannedMealAttendees.CountAsync()).Should().Be(2);
        (await context.MealSuggestionCandidates.SingleAsync()).ReviewOutcome.Should().Be("accepted");
    }

    [PostgresFact]
    public async Task EditedAfterReview_RequiresNewGenerationBeforeAcceptance()
    {
        var (recipe, household, guest) = await Prepare();
        var oldBatch = await Generate([household.Id, guest.Id]);
        await Edit(recipe.Id, "Changed after review", 200m);
        var rejected = await Accept(oldBatch);
        rejected.Status.Should().Be("needsReview");
        rejected.RecipeId.Should().Be(recipe.Id);
        (await _client.GetFromJsonAsync<List<PlannedMealDto>>($"/api/plannedmeals?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}"))!.Should().BeEmpty();
        var newBatch = await Generate([household.Id, guest.Id]);
        newBatch.Suggestions.Single().CandidateVersion.Should().NotBe(oldBatch.Suggestions.Single().CandidateVersion);
        (await Accept(newBatch)).Status.Should().Be("created");
    }

    [PostgresFact]
    public async Task GuestDeactivatedAfterGeneration_DoesNotSubstituteHouseholdOrCreateMeal()
    {
        var (_, _, guest) = await Prepare();
        var batch = await Generate([guest.Id]);
        var change = await _client.PutAsJsonAsync($"/api/familymembers/{guest.Id}", new UpdateFamilyMemberDto
            { Name = guest.Name, IsGuest = true, IsActive = false });
        change.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Accept(batch)).Status.Should().Be("planningFailed");
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<DishhiveDbContext>().PlannedMeals.CountAsync()).Should().Be(0);
    }

    [LocalBrowserFact]
    public async Task Browser_ImportEditQuickPlanGenerateAccept_UsesRealLocalApiAndPostgreSql()
    {
        var address = _factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = Path.Combine(Root, "src", "dishhive-web"),
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var argument in new[] { "node_modules/@playwright/test/cli.js", "test", "--config", "playwright.api.config.ts" })
            start.ArgumentList.Add(argument);
        start.Environment["DISHHIVE_WORKFLOW_BASE_URL"] = address;
        var artifacts = Environment.GetEnvironmentVariable("DISHHIVE_LOCAL_ARTIFACTS")
            ?? Path.Combine(Root, "artifacts", "local-verification");
        Directory.CreateDirectory(artifacts);
        start.Environment["DISHHIVE_LOCAL_ARTIFACTS"] = artifacts;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var log = await output + await error;
        await File.WriteAllTextAsync(Path.Combine(artifacts, "api-browser.log"), log);
        process.ExitCode.Should().Be(0, "the real API browser workflow must pass: {0}", log);
    }

    private async Task<(RecipeDto, FamilyMemberDto, FamilyMemberDto)> Prepare()
    {
        var household = await Member("Household", false);
        var guest = await Member("Guest", true);
        var imported = await _client.PostAsJsonAsync("/api/recipes/import", new ImportRecipeRequestDto { Url = LocalWorkflowFactory.ImportUrl });
        imported.StatusCode.Should().Be(HttpStatusCode.Created);
        var recipe = (await imported.Content.ReadFromJsonAsync<RecipeDto>())!;
        recipe.SourceProvider.Should().Be("dagelijkse-kost");
        recipe.Ingredients.Should().HaveCount(15);
        recipe = await Edit(recipe.Id, "Edited workflow rice", 100m);
        return (recipe, household, guest);
    }

    private async Task<RecipeDto> Edit(Guid id, string title, decimal quantity)
    {
        var response = await _client.PutAsJsonAsync($"/api/recipes/{id}", new UpdateRecipeDto
        {
            Title = title, Servings = 4, TotalTimeMinutes = 20,
            Ingredients = [new() { Name = "rice", Quantity = quantity, Unit = "g" }],
            Steps = [new() { Instruction = "Cook the rice." }]
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirm = await _client.PutAsJsonAsync($"/api/recipes/{id}/facts", new UpdateRecipeFactsDto { Contains = [] });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await _client.GetFromJsonAsync<RecipeDto>($"/api/recipes/{id}"))!;
    }

    private async Task<FamilyMemberDto> Member(string name, bool guest)
    {
        var response = await _client.PostAsJsonAsync("/api/familymembers", new CreateFamilyMemberDto { Name = name, IsGuest = guest });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FamilyMemberDto>())!;
    }

    private async Task<MealSuggestionsDto> Generate(List<Guid> attendees)
    {
        var response = await _client.PostAsJsonAsync("/api/plannedmeals/suggestions", new SuggestWeekRequestDto
            { WeekStart = Day, SelectedDates = [Day], AttendeeIds = attendees });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<MealSuggestionsDto>())!;
    }

    private async Task<AcceptMealSuggestionResultDto> Accept(MealSuggestionsDto batch)
    {
        var item = batch.Suggestions.Single();
        var response = await _client.PostAsJsonAsync("/api/plannedmeals/suggestions/accept", new AcceptMealSuggestionsRequestDto
        {
            BatchId = batch.BatchId, Suggestions = [new()
            {
                Id = item.Id, Date = item.Date, MealType = item.MealType, Course = item.Course, DishName = item.DishName,
                RecipeId = item.RecipeId, AttendeeIds = item.AttendeeIds, CandidateVersion = item.CandidateVersion,
                SourceUrl = item.SourceUrl, FreezyItemRef = item.FreezyItemRef, FreezyItemQuantity = item.FreezyItemQuantity
            }]
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AcceptMealSuggestionsResponseDto>())!.Results.Single();
    }
}

public sealed class LocalBrowserFactAttribute : FactAttribute
{
    public LocalBrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DISHHIVE_TEST_BROWSER") != "1"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISHHIVE_TEST_POSTGRES")))
            Skip = "Run scripts/verify-local.ps1 to include the real API browser workflow.";
    }
}
