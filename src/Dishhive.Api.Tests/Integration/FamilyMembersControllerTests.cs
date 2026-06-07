using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Models.Family;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class FamilyMembersControllerTests : TestBase
{
    [Fact]
    public async Task Empty_list_returns_empty_array()
    {
        var members = await Client.GetFromJsonAsync<List<FamilyMemberDto>>("/api/family-members");
        members.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task Create_then_get_returns_member()
    {
        var create = new CreateFamilyMemberDto { DisplayName = "Sam", Notes = "lactose intolerant" };
        var post = await Client.PostAsJsonAsync("/api/family-members", create);
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await post.Content.ReadFromJsonAsync<FamilyMemberDto>();
        created!.DisplayName.Should().Be("Sam");

        var fetched = await Client.GetFromJsonAsync<FamilyMemberDto>($"/api/family-members/{created.Id}");
        fetched!.Notes.Should().Be("lactose intolerant");
    }

    [Fact]
    public async Task Update_changes_name()
    {
        var post = await Client.PostAsJsonAsync("/api/family-members", new CreateFamilyMemberDto { DisplayName = "X" });
        var created = await post.Content.ReadFromJsonAsync<FamilyMemberDto>();

        var put = await Client.PutAsJsonAsync($"/api/family-members/{created!.Id}",
            new UpdateFamilyMemberDto { DisplayName = "Renamed" });
        put.IsSuccessStatusCode.Should().BeTrue();

        var refreshed = await Client.GetFromJsonAsync<FamilyMemberDto>($"/api/family-members/{created.Id}");
        refreshed!.DisplayName.Should().Be("Renamed");
    }

    [Fact]
    public async Task Delete_returns_404_afterwards()
    {
        var post = await Client.PostAsJsonAsync("/api/family-members", new CreateFamilyMemberDto { DisplayName = "Drop" });
        var created = await post.Content.ReadFromJsonAsync<FamilyMemberDto>();

        var del = await Client.DeleteAsync($"/api/family-members/{created!.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAgain = await Client.GetAsync($"/api/family-members/{created.Id}");
        getAgain.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Preferences_can_be_added_and_listed()
    {
        var post = await Client.PostAsJsonAsync("/api/family-members", new CreateFamilyMemberDto { DisplayName = "Prefs" });
        var member = await post.Content.ReadFromJsonAsync<FamilyMemberDto>();

        var addPref = await Client.PostAsJsonAsync($"/api/family-members/{member!.Id}/preferences",
            new CreatePreferenceDto { Kind = PreferenceKind.Allergy, Value = "peanut" });
        addPref.StatusCode.Should().Be(HttpStatusCode.Created);

        var prefs = await Client.GetFromJsonAsync<List<FamilyMemberPreferenceDto>>(
            $"/api/family-members/{member.Id}/preferences");
        prefs.Should().HaveCount(1);
        prefs![0].Value.Should().Be("peanut");
        prefs[0].Kind.Should().Be(PreferenceKind.Allergy);
    }

    [Fact]
    public async Task Preferences_under_unknown_member_return_404()
    {
        var resp = await Client.GetAsync($"/api/family-members/{Guid.NewGuid()}/preferences");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
