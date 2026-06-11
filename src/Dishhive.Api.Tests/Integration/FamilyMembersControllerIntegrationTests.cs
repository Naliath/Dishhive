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
            AllergyTags = ["Noten"],
            DietTags = ["Vegetarisch"],
            PreferenceNotes = "houdt van pasta"
        };

        var response = await Client.PostAsJsonAsync("/api/familymembers", dto);
        var created = await response.Content.ReadFromJsonAsync<FamilyMemberDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.Id.Should().NotBeEmpty();
        created.Name.Should().Be("Anna");
        created.AllergyTags.Should().Equal("Noten");
        created.DietTags.Should().Equal("Vegetarisch");
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
            AllergyTags = ["Lactose"],
            IsActive = true
        };

        var response = await Client.PutAsJsonAsync($"/api/familymembers/{member.Id}", dto);
        var updated = await response.Content.ReadFromJsonAsync<FamilyMemberDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.Name.Should().Be("Bijgewerkt");
        updated.IsGuest.Should().BeTrue();
        updated.AllergyTags.Should().Equal("Lactose");
    }

    [Fact]
    public async Task Tags_AreReusedCaseInsensitively_AcrossMembers()
    {
        var first = await CreateMemberAsync("Eerste", allergyTags: ["Noten"]);
        var second = await CreateMemberAsync("Tweede", allergyTags: ["noten"]);

        // Both members link to the same tag, original casing preserved
        second.AllergyTags.Should().Equal("Noten");
        var tags = await Client.GetFromJsonAsync<List<DietaryTagDto>>("/api/dietarytags");
        tags!.Should().ContainSingle(t => t.Kind == DietaryTagKind.Allergy)
            .Which.Name.Should().Be("Noten");
        first.AllergyTags.Should().Equal("Noten");
    }

    [Fact]
    public async Task Tags_SameNameDifferentKind_AreSeparateTags()
    {
        await CreateMemberAsync("Anna", allergyTags: ["Varkensvlees"], dietTags: ["Varkensvlees"]);

        var tags = await Client.GetFromJsonAsync<List<DietaryTagDto>>("/api/dietarytags");

        tags!.Should().HaveCount(2);
        tags.Select(t => t.Kind).Should().BeEquivalentTo(
            [DietaryTagKind.Allergy, DietaryTagKind.Diet]);
    }

    [Fact]
    public async Task UpdateMember_RemovingLastUse_DeletesOrphanedTag()
    {
        var member = await CreateMemberAsync("Anna", allergyTags: ["Schaaldieren"]);

        var update = new UpdateFamilyMemberDto { Name = "Anna", AllergyTags = [] };
        var response = await Client.PutAsJsonAsync($"/api/familymembers/{member.Id}", update);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tags = await Client.GetFromJsonAsync<List<DietaryTagDto>>("/api/dietarytags");
        tags!.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateMember_RemovingSharedTag_KeepsTagForOtherMembers()
    {
        var anna = await CreateMemberAsync("Anna", allergyTags: ["Noten"]);
        await CreateMemberAsync("Bart", allergyTags: ["Noten"]);

        var update = new UpdateFamilyMemberDto { Name = "Anna", AllergyTags = [] };
        await Client.PutAsJsonAsync($"/api/familymembers/{anna.Id}", update);

        var tags = await Client.GetFromJsonAsync<List<DietaryTagDto>>("/api/dietarytags");
        tags!.Should().ContainSingle().Which.Name.Should().Be("Noten");
    }

    [Fact]
    public async Task CreateMember_WithOverlongTag_ReturnsBadRequest()
    {
        var dto = new CreateFamilyMemberDto
        {
            Name = "Anna",
            AllergyTags = [new string('x', 51)]
        };

        var response = await Client.PostAsJsonAsync("/api/familymembers", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMember_DeduplicatesTagsWithinRequest()
    {
        var created = await CreateMemberAsync("Anna", allergyTags: ["Noten", " noten ", "NOTEN"]);

        created.AllergyTags.Should().Equal("Noten");
    }

    private async Task<FamilyMemberDto> CreateMemberAsync(
        string name, List<string>? allergyTags = null, List<string>? dietTags = null)
    {
        var response = await Client.PostAsJsonAsync("/api/familymembers", new CreateFamilyMemberDto
        {
            Name = name,
            AllergyTags = allergyTags ?? [],
            DietTags = dietTags ?? []
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FamilyMemberDto>())!;
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
