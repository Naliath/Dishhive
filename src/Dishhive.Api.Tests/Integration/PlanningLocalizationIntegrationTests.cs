using System.Net.Http.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Localization;
using Dishhive.Api.Services.Suggestions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dishhive.Api.Tests.Integration;

public sealed class PlanningLocalizationIntegrationTests : TestBase
{
    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public async Task UnmetRequirementsUseStableMetadataAndNameTheUnfilledDate(string language)
    {
        var day = TestHouseholdClock.System.Today.AddDays(7);
        var validation = PlanningIntentValidator.ValidateFinal([], new(), new() { DaysToFill = [day] });
        var issue = Assert.Single(validation.UnmetRequirements) with { Message = "Unrelated diagnostic prose" };
        var service = Substitute.For<IMealSuggestionService>();
        service.IsEnabled.Returns(true);
        service.LastResult.Returns(new MealSuggestionResult(PlanCompletionStatus.Unable, [], [issue]));
        service.SuggestAsync(Arg.Any<MealSuggestionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MealSuggestion>>([]));
        using var factory = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMealSuggestionService>();
            services.AddSingleton(service);
        }));
        using var client = factory.CreateClient();
        DbContext.UserSettings.Add(new() { Key = UserSettingKeys.PreferredLanguage, Value = language });
        await DbContext.SaveChangesAsync();

        var response = await client.PostAsJsonAsync("/api/plannedmeals/suggestions", new SuggestWeekRequestDto
            { WeekStart = day, SelectedDates = [day] });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<MealSuggestionsDto>();
        var unmet = Assert.Single(result!.UnmetRequirements);
        Assert.Equal(issue.Id, unmet.Id);
        Assert.Contains(language == "nl" ? "hoofdgerecht" : "dinner main", unmet.Message);
        Assert.Contains(day.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo(language)), unmet.Message);
        Assert.DoesNotContain("{p0}", unmet.Message);
        Assert.DoesNotContain("Unrelated", unmet.Message);
    }

    [Fact]
    public async Task CanonicalWarningsTranslateClassesAndRetainMemberAndTagText()
    {
        DbContext.UserSettings.Add(new() { Key = UserSettingKeys.PreferredLanguage, Value = "nl" });
        await DbContext.SaveChangesAsync();
        var memberId = Guid.NewGuid();
        var recipeId = Guid.NewGuid();
        const string memberName = "Nora \"guest\"";
        const string tagName = "My own label";
        var request = new MealSuggestionRequest
        {
            Members = [new() { Id = memberId, Name = memberName, Diets = [new() { Name = tagName, ExcludedClasses = [IngredientClass.Milk] }] }],
            KnownRecipes = [new() { Id = recipeId, Title = "Soup", FactsAssessed = true, ContainsClasses = [IngredientClass.Milk] }]
        };
        var suggestion = Assert.Single(new MealSuggestionPostProcessor(NullLogger<MealSuggestionPostProcessor>.Instance)
            .Finalize([new() { RecipeId = recipeId, DishName = "Soup", AttendeeIds = [memberId] }], request));
        Assert.NotNull(suggestion.DietWarningText);
        var text = await new UserMessageLocalizer(DbContext).GetAsync(suggestion.DietWarningText);
        Assert.Contains("Melk", text);
        Assert.Contains(memberName, text);
        Assert.Contains(tagName, text);
        Assert.DoesNotContain("Contains", text);
    }

    [Fact]
    public async Task ExpiredReviewReturnsLocalizedReasonWithoutChangingItsStatus()
    {
        DbContext.UserSettings.Add(new() { Key = UserSettingKeys.PreferredLanguage, Value = "nl" });
        await DbContext.SaveChangesAsync();
        var result = await Scope.ServiceProvider.GetRequiredService<MealSuggestionAcceptanceService>()
            .AcceptAsync(new() { BatchId = Guid.NewGuid(), Suggestions = [new() { Id = Guid.NewGuid(), DishName = "Soup" }] });
        var item = Assert.Single(result.Results);
        Assert.Equal("needsReview", item.Status);
        Assert.Contains("verlopen", item.Error);
    }
}
