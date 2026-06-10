using System.Net;
using System.Net.Http.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using FluentAssertions;

namespace Dishhive.Api.Tests.Integration;

public class FamilyMembersControllerIntegrationTests : TestBase
{
    [Fact]
    public async Task GetMembers_ReturnsEmptyList_WhenNoMembers()
    {
        var members = await Client.GetFromJsonAsync<List<FamilyMemberDto>>("/api/familymembers");

        members.Should().NotBeNull();
        members.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateMember_ReturnsCreatedMember()
    {
        var dto = new CreateFamilyMemberDto
        {
            Name = "Anna",
            Allergies = "noten",
            DietaryConstraints = "vegetarisch",
            PreferenceNotes = "houdt van pasta"
        };

        var response = await Client.PostAsJsonAsync("/api/familymembers", dto);
        var created = await response.Content.ReadFromJsonAsync<FamilyMemberDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.Id.Should().NotBeEmpty();
        created.Name.Should().Be("Anna");
        created.Allergies.Should().Be("noten");
        created.DietaryConstraints.Should().Be("vegetarisch");
        created.IsGuest.Should().BeFalse();
        created.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateMember_WithEmptyName_ReturnsBadRequest()
    {
        var dto = new CreateFamilyMemberDto { Name = "" };

        var response = await Client.PostAsJsonAsync("/api/familymembers", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMembers_ExcludesInactiveMembers_ByDefault()
    {
        DbContext.FamilyMembers.Add(new FamilyMember { Name = "Actief" });
        DbContext.FamilyMembers.Add(new FamilyMember { Name = "Inactief", IsActive = false });
        await DbContext.SaveChangesAsync();

        var members = await Client.GetFromJsonAsync<List<FamilyMemberDto>>("/api/familymembers");
        var allMembers = await Client.GetFromJsonAsync<List<FamilyMemberDto>>("/api/familymembers?includeInactive=true");

        members!.Should().ContainSingle(m => m.Name == "Actief");
        allMembers!.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateMember_UpdatesFields()
    {
        var member = new FamilyMember { Name = "Origineel" };
        DbContext.FamilyMembers.Add(member);
        await DbContext.SaveChangesAsync();

        var dto = new UpdateFamilyMemberDto
        {
            Name = "Bijgewerkt",
            IsGuest = true,
            Allergies = "lactose",
            IsActive = true
        };

        var response = await Client.PutAsJsonAsync($"/api/familymembers/{member.Id}", dto);
        var updated = await response.Content.ReadFromJsonAsync<FamilyMemberDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.Name.Should().Be("Bijgewerkt");
        updated.IsGuest.Should().BeTrue();
        updated.Allergies.Should().Be("lactose");
    }

    [Fact]
    public async Task UpdateMember_UnknownId_ReturnsNotFound()
    {
        var dto = new UpdateFamilyMemberDto { Name = "Niemand" };

        var response = await Client.PutAsJsonAsync($"/api/familymembers/{Guid.NewGuid()}", dto);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteMember_WithoutHistory_RemovesMember()
    {
        var member = new FamilyMember { Name = "Tijdelijk" };
        DbContext.FamilyMembers.Add(member);
        await DbContext.SaveChangesAsync();

        var response = await Client.DeleteAsync($"/api/familymembers/{member.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var freshContext = CreateFreshContext();
        freshContext.FamilyMembers.Should().NotContain(m => m.Id == member.Id);
    }

    [Fact]
    public async Task DeleteMember_WithMealHistory_DeactivatesInstead()
    {
        var member = new FamilyMember { Name = "Met geschiedenis" };
        var meal = new PlannedMeal
        {
            Date = new DateOnly(2026, 6, 1),
            DishName = "Spaghetti",
            Attendees = { new PlannedMealAttendee { FamilyMember = member } }
        };
        DbContext.PlannedMeals.Add(meal);
        await DbContext.SaveChangesAsync();

        var response = await Client.DeleteAsync($"/api/familymembers/{member.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var freshContext = CreateFreshContext();
        var stored = freshContext.FamilyMembers.Single(m => m.Id == member.Id);
        stored.IsActive.Should().BeFalse();
    }
}
