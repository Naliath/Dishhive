using Dishhive.Api.Services.Demo;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// Consistency checks on the static demo dataset; the live import path itself
/// is covered by the recipe import tests.
/// </summary>
public class DemoDataTests
{
    [Fact]
    public void RecipeUrls_ContainsTwentyDistinctDagelijkseKostUrls()
    {
        DemoData.RecipeUrls.Should().HaveCount(20);
        DemoData.RecipeUrls.Should().OnlyHaveUniqueItems();
        DemoData.RecipeUrls.Should().OnlyContain(url =>
            url.StartsWith("https://dagelijksekost.vrt.be/gerechten/"));
    }

    [Fact]
    public void Members_AreTheRocinanteCrew()
    {
        DemoData.Members.Select(m => m.Name).Should().BeEquivalentTo(
            "James Holden", "Naomi Nagata", "Alex Kamal", "Amos Burton");
    }

    [Fact]
    public void Members_IncludeOneVegetarianAndSomeDietaryNeeds()
    {
        DemoData.Members.Should().ContainSingle(m => m.DietTags.Contains("Vegetarian"));

        DemoData.Members.Count(m => m.AllergyTags.Count > 0 || m.DietTags.Count > 0)
            .Should().BeGreaterThanOrEqualTo(2, "a few members should have specific dietary needs");
    }

    [Fact]
    public void Collections_ReferenceSeededRecipeUrls_AndHaveValidNames()
    {
        DemoData.Collections.Should().NotBeEmpty();
        DemoData.Collections.Select(c => c.Name).Should().OnlyHaveUniqueItems();

        foreach (var collection in DemoData.Collections)
        {
            // Brackets delimit #[Name] references in planning instructions
            collection.Name.Should().NotContainAny("[", "]");
            collection.RecipeUrls.Should().NotBeEmpty();
            collection.RecipeUrls.Should().OnlyHaveUniqueItems();
            collection.RecipeUrls.Should().BeSubsetOf(DemoData.RecipeUrls,
                $"members of '{collection.Name}' must link to recipes the demo seeder imports");
        }
    }

    [Fact]
    public void Members_FavoriteRecipes_ReferenceSeededRecipeUrls()
    {
        foreach (var member in DemoData.Members)
        {
            member.FavoriteRecipeUrls.Should().NotBeEmpty();
            member.FavoriteRecipeUrls.Should().BeSubsetOf(DemoData.RecipeUrls,
                $"favorites of {member.Name} must link to recipes the demo seeder imports");
        }
    }
}
