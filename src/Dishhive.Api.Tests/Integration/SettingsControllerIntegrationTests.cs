using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class SettingsControllerIntegrationTests : TestBase
{
    [Fact]
    public async Task Languages_AreDiscoveredFromBackendLocalizationFiles()
    {
        var languages = await Client.GetFromJsonAsync<List<SupportedLanguageDto>>(
            "/api/settings/languages");

        languages!.Select(language => language.Code).Should().BeEquivalentTo("en", "nl");
    }

    [Fact]
    public async Task Preferences_UsesTypedEnumsAndLanguagesDiscoveredFromResources()
    {
        var response = await Client.GetAsync("/api/settings/preferences");
        var preferences = await response.Content.ReadFromJsonAsync<UserPreferencesDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        preferences!.MeasurementSystem.Should().Be(MeasurementSystem.Metric);
        preferences.FirstDayOfWeek.Should().Be(FirstDayOfWeek.Monday);
        preferences.SupportedLanguages.Select(language => language.Code)
            .Should().BeEquivalentTo("en", "nl");
    }

    [Fact]
    public async Task Preferences_TypedWritesOnlyChangeTheirOwnSetting()
    {
        await Client.PatchAsJsonAsync("/api/settings/preferences",
            new UpdateUserPreferencesDto(MeasurementSystem: MeasurementSystem.Imperial));
        await Client.PatchAsJsonAsync("/api/settings/preferences",
            new UpdateUserPreferencesDto(PreferredLanguage: "nl"));
        var preferences = await Client.GetFromJsonAsync<UserPreferencesDto>("/api/settings/preferences");

        preferences.Should().BeEquivalentTo(new
        {
            MeasurementSystem = MeasurementSystem.Imperial,
            FirstDayOfWeek = FirstDayOfWeek.Monday,
            PreferredLanguage = "nl",
            TranslateImportedRecipes = false
        });
    }

    [Fact]
    public async Task Preferences_RejectsLanguageWithoutTranslationResource()
    {
        var response = await Client.PatchAsJsonAsync("/api/settings/preferences",
            new UpdateUserPreferencesDto(PreferredLanguage: "fr"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Preferences_UpdateRequiresExactlyOneValue()
    {
        var empty = await Client.PatchAsJsonAsync("/api/settings/preferences",
            new UpdateUserPreferencesDto());
        var multiple = await Client.PatchAsJsonAsync("/api/settings/preferences",
            new UpdateUserPreferencesDto(
                MeasurementSystem: MeasurementSystem.Imperial,
                FirstDayOfWeek: FirstDayOfWeek.Sunday));

        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        multiple.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetSetting_UnknownKey_ReturnsNotFound()
    {
        // measurementSystem is metric by absence: no row until the user changes it
        var response = await Client.GetAsync($"/api/settings/{UserSettingKeys.MeasurementSystem}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetSetting_NewKey_CreatesSetting()
    {
        var response = await Client.PutAsJsonAsync(
            $"/api/settings/{UserSettingKeys.MeasurementSystem}",
            new UpsertUserSettingDto { Value = "imperial" });

        var created = await response.Content.ReadFromJsonAsync<UserSettingDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.Key.Should().Be(UserSettingKeys.MeasurementSystem);
        created.Value.Should().Be("imperial");
    }

    [Fact]
    public async Task SetSetting_ExistingKey_UpdatesValue()
    {
        await Client.PutAsJsonAsync(
            $"/api/settings/{UserSettingKeys.MeasurementSystem}",
            new UpsertUserSettingDto { Value = "imperial" });

        var response = await Client.PutAsJsonAsync(
            $"/api/settings/{UserSettingKeys.MeasurementSystem}",
            new UpsertUserSettingDto { Value = "metric" });
        var updated = await response.Content.ReadFromJsonAsync<UserSettingDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.Value.Should().Be("metric");
    }

    [Fact]
    public async Task DeleteSetting_ExistingKey_RemovesSetting()
    {
        await Client.PutAsJsonAsync(
            $"/api/settings/{UserSettingKeys.MeasurementSystem}",
            new UpsertUserSettingDto { Value = "imperial" });

        var deleteResponse = await Client.DeleteAsync($"/api/settings/{UserSettingKeys.MeasurementSystem}");
        var getResponse = await Client.GetAsync($"/api/settings/{UserSettingKeys.MeasurementSystem}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
