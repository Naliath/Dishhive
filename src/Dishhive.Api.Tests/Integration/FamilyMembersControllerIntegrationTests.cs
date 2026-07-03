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
            AllergyTags = Tags("Noten"),
            DietTags = Tags("Vegetarisch"),
            PreferenceNotes = "houdt van pasta"
        };

        var response = await Client.PostAsJsonAsync("/api/familymembers", dto);
        var created = await response.Content.ReadFromJsonAsync<FamilyMemberDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.Id.Should().NotBeEmpty();
        created.Name.Should().Be("Anna");
        created.AllergyTags.Select(t => t.Name).Should().Equal("Noten");
        created.DietTags.Select(t => t.Name).Should().Equal("Vegetarisch");
        created.IsGuest.Should().BeFalse();
        created.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateMember_SeedsExcludedClassesFromPresets()
    {
        var created = await CreateMemberAsync("Anna",
            allergyTags: ["Noten"], dietTags: ["Vegetarisch"]);

        created.AllergyTags.Single().ExcludedClasses.Should().Equal("TreeNuts");
        created.DietTags.Single().ExcludedClasses.Should().BeEquivalentTo(
            "RedMeat", "Poultry", "Pork", "Fish", "Crustaceans", "Molluscs", "Gelatin");
    }

    [Fact]
    public async Task CreateMember_UnknownTagName_GetsEmptyClasses()
    {
        var created = await CreateMemberAsync("Anna", dietTags: ["Koosjer"]);

        created.DietTags.Single().ExcludedClasses.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateMember_ExplicitClasses_OverridePreset()
    {
        // This vegetarian eats fish: the member's own definition wins over the preset
        var dto = new CreateFamilyMemberDto
        {
            Name = "Anna",
            DietTags =
            [
                new DietaryTagEntryDto
                {
                    Name = "Vegetarisch",
                    ExcludedClasses = ["RedMeat", "Poultry", "Pork", "Gelatin"]
                }
            ]
        };

        var response = await Client.PostAsJsonAsync("/api/familymembers", dto);
        var created = await response.Content.ReadFromJsonAsync<FamilyMemberDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created!.DietTags.Single().ExcludedClasses
            .Should().BeEquivalentTo("RedMeat", "Poultry", "Pork", "Gelatin");
    }

    [Fact]
    public async Task SharedTag_CanHaveDifferentClassesPerMember()
    {
        // One shared "Vegetarisch" tag, two readings of it
        var strict = await CreateMemberAsync("Strikt", dietTags: ["Vegetarisch"]);
        var response = await Client.PostAsJsonAsync("/api/familymembers", new CreateFamilyMemberDto
        {
            Name = "EetVis",
            DietTags =
            [
                new DietaryTagEntryDto
                {
                    Name = "Vegetarisch",
                    ExcludedClasses = ["RedMeat", "Poultry", "Pork", "Gelatin"]
                }
            ]
        });
        var eetVis = await response.Content.ReadFromJsonAsync<FamilyMemberDto>();

        var tags = await Client.GetFromJsonAsync<List<DietaryTagDto>>("/api/dietarytags");
        tags!.Should().ContainSingle(t => t.Kind == DietaryTagKind.Diet);
        strict.DietTags.Single().ExcludedClasses.Should().Contain("Fish");
        eetVis!.DietTags.Single().ExcludedClasses.Should().NotContain("Fish");
    }

    [Fact]
    public async Task UpdateMember_NullClasses_KeepStoredDefinition()
    {
        var created = await CreateMemberAsync("Anna", allergyTags: ["Noten"]);

        // Re-submitting the tag without classes (the normal "edited something else"
        // save) must not wipe the stored definition
        var update = new UpdateFamilyMemberDto
        {
            Name = "Anna",
            AllergyTags = [new DietaryTagEntryDto { Name = "Noten", ExcludedClasses = null }]
        };
        var response = await Client.PutAsJsonAsync($"/api/familymembers/{created.Id}", update);
        var updated = await response.Content.ReadFromJsonAsync<FamilyMemberDto>();

        updated!.AllergyTags.Single().ExcludedClasses.Should().Equal("TreeNuts");
    }

    [Fact]
    public async Task CreateMember_UnknownClassName_ReturnsBadRequest()
    {
        var dto = new CreateFamilyMemberDto
        {
            Name = "Anna",
            AllergyTags = [new DietaryTagEntryDto { Name = "Noten", ExcludedClasses = ["Kryptonite"] }]
        };

        var response = await Client.PostAsJsonAsync("/api/familymembers", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
            AllergyTags = Tags("Lactose"),
            IsActive = true
        };

        var response = await Client.PutAsJsonAsync($"/api/familymembers/{member.Id}", dto);
        var updated = await response.Content.ReadFromJsonAsync<FamilyMemberDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.Name.Should().Be("Bijgewerkt");
        updated.IsGuest.Should().BeTrue();
        updated.AllergyTags.Select(t => t.Name).Should().Equal("Lactose");
    }

    [Fact]
    public async Task Tags_AreReusedCaseInsensitively_AcrossMembers()
    {
        var first = await CreateMemberAsync("Eerste", allergyTags: ["Noten"]);
        var second = await CreateMemberAsync("Tweede", allergyTags: ["noten"]);

        // Both members link to the same tag, original casing preserved
        second.AllergyTags.Select(t => t.Name).Should().Equal("Noten");
        var tags = await Client.GetFromJsonAsync<List<DietaryTagDto>>("/api/dietarytags");
        tags!.Should().ContainSingle(t => t.Kind == DietaryTagKind.Allergy)
            .Which.Name.Should().Be("Noten");
        first.AllergyTags.Select(t => t.Name).Should().Equal("Noten");
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
            AllergyTags = Tags(new string('x', 51))
        };

        var response = await Client.PostAsJsonAsync("/api/familymembers", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMember_DeduplicatesTagsWithinRequest()
    {
        var created = await CreateMemberAsync("Anna", allergyTags: ["Noten", " noten ", "NOTEN"]);

        created.AllergyTags.Select(t => t.Name).Should().Equal("Noten");
    }

    private static List<DietaryTagEntryDto> Tags(params string[] names) => names
        .Select(n => new DietaryTagEntryDto { Name = n })
        .ToList();

    private async Task<FamilyMemberDto> CreateMemberAsync(
        string name, List<string>? allergyTags = null, List<string>? dietTags = null)
    {
        var response = await Client.PostAsJsonAsync("/api/familymembers", new CreateFamilyMemberDto
        {
            Name = name,
            AllergyTags = Tags([.. allergyTags ?? []]),
            DietTags = Tags([.. dietTags ?? []])
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
