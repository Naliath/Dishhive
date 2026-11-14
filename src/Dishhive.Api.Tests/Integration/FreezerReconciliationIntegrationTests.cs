using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Suggestions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dishhive.Api.Tests.Integration;

public sealed class FreezerReconciliationIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _base = new();
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory;
    private readonly StockClient _stock = new();
    private readonly HttpClient _client;
    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.Today).AddDays(3);

    public FreezerReconciliationIntegrationTests()
    {
        _factory = _base.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFreezyClient>();
            services.AddSingleton<IFreezyClient>(_stock);
        }));
        _client = _factory.CreateClient();
    }

    private async Task<PlannedMealDto> Reserve(int quantity = 2)
    {
        var response = await _client.PostAsJsonAsync("/api/plannedmeals", new CreatePlannedMealDto
        {
            Date = Day, DishName = "Keep this dinner", Notes = "Keep these notes",
            FreezyItemRef = "stock", FreezyItemQuantity = quantity
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlannedMealDto>())!;
    }

    [Fact]
    public async Task FreezerDetailsChangedAfterSuggestionReview_AcceptanceRequiresNewReview()
    {
        var batch = Guid.NewGuid();
        var suggestion = new MealSuggestionDto { Id = Guid.NewGuid(), Date = Day, DishName = "Soup",
            FreezyItemRef = "stock", FreezyItemQuantity = 1 };
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<MealSuggestionAcceptanceService>().StoreBatchAsync(
                batch, PlanCompletionStatus.Complete, [suggestion], freezerItems: _stock.Items);
        _stock.Items = [_stock.Items[0] with { Name = "A different dish" }];
        var response = await _client.PostAsJsonAsync("/api/plannedmeals/suggestions/accept", new AcceptMealSuggestionsRequestDto
        {
            BatchId = batch, Suggestions = [new() { Id = suggestion.Id, Date = Day, DishName = "Soup",
                FreezyItemRef = "stock", FreezyItemQuantity = 1, CandidateVersion = suggestion.CandidateVersion }]
        });
        response.EnsureSuccessStatusCode();
        var result = Assert.Single((await response.Content.ReadFromJsonAsync<AcceptMealSuggestionsResponseDto>())!.Results);
        Assert.Equal("needsReview", result.Status);
        Assert.Contains("freezer item details changed", result.Error);
        using var check = _factory.Services.CreateScope();
        var review = check.ServiceProvider.GetRequiredService<DishhiveDbContext>().AiReviewEvents.Single();
        Assert.Contains("freezerDetails", review.InputChangesJson);
    }

    [Fact]
    public async Task ExternalQuantityFalls_ReleaseKeepsTheMealAndClearsTheShortage()
    {
        var meal = await Reserve();
        Assert.Empty((await Check()).Issues);
        _stock.Items = [_stock.Items[0] with { Quantity = 1 }];
        var issue = Assert.Single((await Check()).Issues);
        Assert.Equal("shortage", issue.Issue);
        Assert.Equal(1, issue.Shortage);
        var response = await Resolve(meal, "release");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var saved = await _client.GetFromJsonAsync<PlannedMealDto>($"/api/plannedmeals/{meal.Id}");
        Assert.Equal("Keep this dinner", saved!.DishName);
        Assert.Equal("Keep these notes", saved.Notes);
        Assert.Null(saved.FreezyItemRef);
        Assert.Equal(0, saved.FreezyItemQuantity);
        Assert.Empty((await Check()).Issues);
    }

    [Fact]
    public async Task UnavailableSnapshot_DoesNotReportMissing_AndEmptySnapshotDoes()
    {
        await Reserve();
        _stock.Available = false;
        var unavailable = await Check();
        Assert.Equal("unavailable", unavailable.State);
        Assert.Empty(unavailable.Issues);
        _stock.Available = true;
        _stock.Items = [];
        Assert.Equal("missing", Assert.Single((await Check()).Issues).Issue);
    }

    [Fact]
    public async Task ChangedDetails_RequireReviewOfTheCurrentFingerprint()
    {
        var meal = await Reserve();
        _stock.Items = [_stock.Items[0] with { Name = "Changed soup" }];
        var issue = Assert.Single((await Check()).Issues);
        Assert.Equal("changed", issue.Issue);
        _stock.Items = [_stock.Items[0] with { Unit = "changed unit" }];
        Assert.Equal(HttpStatusCode.Conflict, (await Resolve(meal, "confirm", issue.Fingerprint)).StatusCode);
        var current = Assert.Single((await Check()).Issues);
        Assert.Equal(HttpStatusCode.NoContent, (await Resolve(meal, "confirm", current.Fingerprint)).StatusCode);
        Assert.Empty((await Check()).Issues);
    }

    [Fact]
    public async Task StaleReservationReview_CannotClearANewAssignment()
    {
        var meal = await Reserve();
        var response = await _client.PutAsJsonAsync($"/api/plannedmeals/{meal.Id}", new UpdatePlannedMealDto
            { Date = Day, DishName = "New assignment", FreezyItemRef = "different", FreezyItemQuantity = 1 });
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Resolve(meal, "release")).StatusCode);
        Assert.Equal("different", (await _client.GetFromJsonAsync<PlannedMealDto>($"/api/plannedmeals/{meal.Id}"))!.FreezyItemRef);
    }

    [Fact]
    public async Task LegacyReservation_IsExplicitlyUnverified_AndCannotConfirmAShortage()
    {
        var meal = await Reserve();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            (await db.PlannedMeals.FindAsync(meal.Id))!.FreezyItemFingerprint = null;
            await db.SaveChangesAsync();
        }
        var issue = Assert.Single((await Check()).Issues);
        Assert.Equal("unverified", issue.Issue);
        _stock.Items = [_stock.Items[0] with { Quantity = 1 }];
        Assert.Equal(HttpStatusCode.Conflict, (await Resolve(meal, "confirm", issue.Fingerprint)).StatusCode);
    }

    private async Task<FreezerReconciliationDto> Check() => (await _client.GetFromJsonAsync<FreezerReconciliationDto>("/api/freezer/reservations"))!;
    private Task<HttpResponseMessage> Resolve(PlannedMealDto meal, string action, string? fingerprint = null) =>
        _client.PutAsJsonAsync($"/api/freezer/reservations/{meal.Id}", new ResolveFreezerReservationDto
            { Action = action, ItemRef = "stock", Quantity = meal.FreezyItemQuantity, Fingerprint = fingerprint });
    public void Dispose() { _client.Dispose(); _factory.Dispose(); _base.Dispose(); }

    private sealed class StockClient : IFreezyClient
    {
        public bool IsConfigured => true;
        public string BaseUrl => "http://fixture/";
        public bool Available { get; set; } = true;
        public IReadOnlyList<FrozenItem> Items { get; set; } = [new() { Id = "stock", Name = "Soup", Quantity = 4, Unit = "portions" }];
        public Task<bool> IsReachableAsync(CancellationToken cancellationToken = default) => Task.FromResult(Available);
        public Task<IReadOnlyList<FrozenItem>> GetFrozenItemsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Items);
        public Task<FreezyStockSnapshot> GetStockSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new FreezyStockSnapshot(true, Available, DateTime.UtcNow, Available ? Items : []));
    }
}
