using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace Dishhive.Api.Tests.Integration;

public class OnboardingControllerIntegrationTests : TestBase
{
    [Fact]
    public async Task Start_EmptyDatabase_StartsAndRemainsVisible()
    {
        var firstResponse = await Client.PostAsync("/api/onboarding/start", null);
        var secondResponse = await Client.PostAsync("/api/onboarding/start", null);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await firstResponse.Content.ReadFromJsonAsync<OnboardingStatusDto>())!.ShouldShow.Should().BeTrue();
        (await secondResponse.Content.ReadFromJsonAsync<OnboardingStatusDto>())!.ShouldShow.Should().BeTrue();

        using var context = CreateFreshContext();
        context.UserSettings.Single(s => s.Key == UserSettingKeys.OnboardingStatus)
            .Value.Should().Be(EnumNames.ToName(OnboardingState.InProgress));
    }

    [Fact]
    public async Task Start_DatabaseWithUserData_DoesNotStartOnboarding()
    {
        DbContext.FamilyMembers.Add(new FamilyMember { Name = "Existing member" });
        await DbContext.SaveChangesAsync();

        var response = await Client.PostAsync("/api/onboarding/start", null);
        var status = await response.Content.ReadFromJsonAsync<OnboardingStatusDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        status!.ShouldShow.Should().BeFalse();
        using var context = CreateFreshContext();
        context.UserSettings.Should().NotContain(s => s.Key == UserSettingKeys.OnboardingStatus);
    }

    [Fact]
    public async Task Start_DemoModeEnabled_DoesNotRaceTheBackgroundSeed()
    {
        using var factory = new TestWebApplicationFactory().WithWebHostBuilder(
            builder => builder.UseSetting("Demo:Enabled", "true"));
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/onboarding/start", null);
        var status = await response.Content.ReadFromJsonAsync<OnboardingStatusDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        status!.ShouldShow.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_StartedOnboarding_HidesItPermanently(bool skipped)
    {
        await Client.PostAsync("/api/onboarding/start", null);

        var completeResponse = await Client.PostAsJsonAsync(
            "/api/onboarding/complete",
            new CompleteOnboardingDto(skipped));
        var restartResponse = await Client.PostAsync("/api/onboarding/start", null);

        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await restartResponse.Content.ReadFromJsonAsync<OnboardingStatusDto>())!.ShouldShow.Should().BeFalse();
        using var context = CreateFreshContext();
        var expectedState = skipped ? OnboardingState.Skipped : OnboardingState.Completed;
        context.UserSettings.Single(s => s.Key == UserSettingKeys.OnboardingStatus)
            .Value.Should().Be(EnumNames.ToName(expectedState));
    }

    [Fact]
    public async Task Start_InProgressOnboardingWithSavedData_ResumesOnboarding()
    {
        await Client.PostAsync("/api/onboarding/start", null);
        await Client.PutAsJsonAsync(
            $"/api/settings/{UserSettingKeys.MeasurementSystem}",
            new UpsertUserSettingDto { Value = "imperial" });
        await Client.PostAsJsonAsync(
            "/api/familymembers",
            new CreateFamilyMemberDto { Name = "Ada" });

        var response = await Client.PostAsync("/api/onboarding/start", null);
        var status = await response.Content.ReadFromJsonAsync<OnboardingStatusDto>();

        status!.ShouldShow.Should().BeTrue();
    }
}
