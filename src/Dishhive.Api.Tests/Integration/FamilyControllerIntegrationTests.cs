using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;
using Xunit;

namespace Dishhive.Api.Tests.Integration;

public class FamilyControllerIntegrationTests : TestBase
{
    public FamilyControllerIntegrationTests()
    {
        ClearDatabase();
    }

    // ── Member CRUD ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WhenNoMembers_ReturnsEmptyList()
    {
        var response = await Client.GetAsync("/api/family");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<FamilyDtos.FamilyMemberSummaryDto>>();
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_PermanentMember_ReturnsCreated()
    {
        var response = await Client.PostAsJsonAsync("/api/family", new { name = "Alice", isGuest = false });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await response.Content.ReadFromJsonAsync<FamilyDtos.FamilyMemberDto>();
        dto!.Name.Should().Be("Alice");
        dto.IsGuest.Should().BeFalse();
    }

    [Fact]
    public async Task Create_GuestMember_SetsGuestFields()
    {
        var response = await Client.PostAsJsonAsync("/api/family", new
        {
            name = "Bob the Guest",
            isGuest = true,
            guestUntil = "2025-12-31"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<FamilyDtos.FamilyMemberDto>();
        dto!.IsGuest.Should().BeTrue();
        dto.GuestUntil.Should().Be(new DateOnly(2025, 12, 31));
    }

    [Fact]
    public async Task GetById_AfterCreate_ReturnsMember()
    {
        var created = await (await Client.PostAsJsonAsync("/api/family", new { name = "Carol" }))
            .Content.ReadFromJsonAsync<FamilyDtos.FamilyMemberDto>();

        var response = await Client.GetAsync($"/api/family/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<FamilyDtos.FamilyMemberDto>();
        dto!.Name.Should().Be("Carol");
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var response = await Client.GetAsync($"/api/family/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_ChangesName()
    {
        var created = await (await Client.PostAsJsonAsync("/api/family", new { name = "Dave" }))
            .Content.ReadFromJsonAsync<FamilyDtos.FamilyMemberDto>();

        var updateResp = await Client.PutAsJsonAsync($"/api/family/{created!.Id}",
            new { name = "David", isGuest = false });
        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var dto = await (await Client.GetAsync($"/api/family/{created.Id}"))
            .Content.ReadFromJsonAsync<FamilyDtos.FamilyMemberDto>();
        dto!.Name.Should().Be("David");
    }

    [Fact]
    public async Task Delete_RemovesMember()
    {
        var created = await (await Client.PostAsJsonAsync("/api/family", new { name = "Eve" }))
            .Content.ReadFromJsonAsync<FamilyDtos.FamilyMemberDto>();

        var deleteResp = await Client.DeleteAsync($"/api/family/{created!.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResp = await Client.GetAsync($"/api/family/{created.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAll_ExcludeGuests_FiltersGuestMembers()
    {
        await Client.PostAsJsonAsync("/api/family", new { name = "Frank", isGuest = false });
        await Client.PostAsJsonAsync("/api/family", new { name = "Grace the Guest", isGuest = true });

        var response = await Client.GetAsync("/api/family?includeGuests=false");
        var list = await response.Content.ReadFromJsonAsync<List<FamilyDtos.FamilyMemberSummaryDto>>();
        list.Should().HaveCount(1).And.ContainSingle(m => m.Name == "Frank");
    }

    // ── Preferences ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddPreference_ReturnsCreated()
    {
        var member = await (await Client.PostAsJsonAsync("/api/family", new { name = "Henry" }))
            .Content.ReadFromJsonAsync<FamilyDtos.FamilyMemberDto>();

        var prefResp = await Client.PostAsJsonAsync($"/api/family/{member!.Id}/preferences", new
        {
            preferenceType = "Allergy",
            value = "Peanuts",
            notes = "Severe"
        });
        prefResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var pref = await prefResp.Content.ReadFromJsonAsync<FamilyDtos.MemberPreferenceDto>();
        pref!.PreferenceType.Should().Be("Allergy");
        pref.Value.Should().Be("Peanuts");
    }

    [Fact]
    public async Task AddPreference_InvalidType_Returns400()
    {
        var member = await (await Client.PostAsJsonAsync("/api/family", new { name = "Iris" }))
            .Content.ReadFromJsonAsync<FamilyDtos.FamilyMemberDto>();

        var resp = await Client.PostAsJsonAsync($"/api/family/{member!.Id}/preferences", new
        {
            preferenceType = "NotAType",
            value = "Something"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeletePreference_RemovesPreference()
    {
        var member = await (await Client.PostAsJsonAsync("/api/family", new { name = "Jack" }))
            .Content.ReadFromJsonAsync<FamilyDtos.FamilyMemberDto>();
        var pref = await (await Client.PostAsJsonAsync($"/api/family/{member!.Id}/preferences",
            new { preferenceType = "Dislike", value = "Broccoli" }))
            .Content.ReadFromJsonAsync<FamilyDtos.MemberPreferenceDto>();

        var del = await Client.DeleteAsync($"/api/family/{member.Id}/preferences/{pref!.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var prefs = await (await Client.GetAsync($"/api/family/{member.Id}/preferences"))
            .Content.ReadFromJsonAsync<List<FamilyDtos.MemberPreferenceDto>>();
        prefs.Should().BeEmpty();
    }

    // ── Favorites ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddFavorite_ReturnsCreated()
    {
        var member = await (await Client.PostAsJsonAsync("/api/family", new { name = "Kate" }))
            .Content.ReadFromJsonAsync<FamilyDtos.FamilyMemberDto>();

        var favResp = await Client.PostAsJsonAsync($"/api/family/{member!.Id}/favorites",
            new { dishName = "Spaghetti Bolognese" });
        favResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var fav = await favResp.Content.ReadFromJsonAsync<FamilyDtos.FavoriteDishDto>();
        fav!.DishName.Should().Be("Spaghetti Bolognese");
    }

    [Fact]
    public async Task DeleteFavorite_RemovesFavorite()
    {
        var member = await (await Client.PostAsJsonAsync("/api/family", new { name = "Liam" }))
            .Content.ReadFromJsonAsync<FamilyDtos.FamilyMemberDto>();
        var fav = await (await Client.PostAsJsonAsync($"/api/family/{member!.Id}/favorites",
            new { dishName = "Lasagna" }))
            .Content.ReadFromJsonAsync<FamilyDtos.FavoriteDishDto>();

        var del = await Client.DeleteAsync($"/api/family/{member!.Id}/favorites/{fav!.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
