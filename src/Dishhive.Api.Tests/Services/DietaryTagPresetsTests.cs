using Dishhive.Api.Models;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class DietaryTagPresetsTests
{
    [Theory]
    [InlineData("noten")]
    [InlineData("Noten")]
    [InlineData("  NUTS  ")]
    public void Resolve_TreeNutSynonyms_CaseAndWhitespaceInsensitive(string name)
    {
        DietaryTagPresets.Resolve(name, DietaryTagKind.Allergy)
            .Should().Equal(IngredientClass.TreeNuts);
    }

    [Theory]
    [InlineData("lactose")]
    [InlineData("melk")]
    [InlineData("zuivel")]
    [InlineData("dairy")]
    public void Resolve_MilkSynonyms_AllMapToMilk(string name)
    {
        DietaryTagPresets.Resolve(name, DietaryTagKind.Allergy)
            .Should().Equal(IngredientClass.Milk);
    }

    [Fact]
    public void Resolve_Shellfish_CoversCrustaceansAndMolluscs()
    {
        DietaryTagPresets.Resolve("shellfish", DietaryTagKind.Allergy)
            .Should().BeEquivalentTo([IngredientClass.Crustaceans, IngredientClass.Molluscs]);
    }

    [Fact]
    public void Resolve_Vegetarian_ExcludesAllMeatFishAndGelatin()
    {
        DietaryTagPresets.Resolve("vegetarisch", DietaryTagKind.Diet)
            .Should().BeEquivalentTo(
            [
                IngredientClass.RedMeat, IngredientClass.Poultry, IngredientClass.Pork,
                IngredientClass.Fish, IngredientClass.Crustaceans, IngredientClass.Molluscs,
                IngredientClass.Gelatin
            ]);
    }

    [Fact]
    public void Resolve_Vegan_IsVegetarianPlusAnimalProducts()
    {
        var vegetarian = DietaryTagPresets.Resolve("vegetarian", DietaryTagKind.Diet);
        var vegan = DietaryTagPresets.Resolve("vegan", DietaryTagKind.Diet);

        vegan.Should().Contain(vegetarian);
        vegan.Should().Contain([IngredientClass.Milk, IngredientClass.Eggs, IngredientClass.Honey]);
    }

    [Fact]
    public void Resolve_Pescatarian_AllowsFish()
    {
        var classes = DietaryTagPresets.Resolve("pescotarisch", DietaryTagKind.Diet);

        classes.Should().Contain([IngredientClass.RedMeat, IngredientClass.Poultry, IngredientClass.Pork]);
        classes.Should().NotContain(IngredientClass.Fish);
    }

    [Fact]
    public void Resolve_NoRedMeat_AlsoExcludesPork()
    {
        // Colloquially pork IS red meat; the taxonomy splits it, so the preset covers both
        DietaryTagPresets.Resolve("geen rood vlees", DietaryTagKind.Diet)
            .Should().BeEquivalentTo([IngredientClass.RedMeat, IngredientClass.Pork]);
    }

    [Fact]
    public void Resolve_KindMatters_DietNameIsNotAnAllergyPreset()
    {
        DietaryTagPresets.Resolve("vegetarisch", DietaryTagKind.Allergy).Should().BeEmpty();
        DietaryTagPresets.Resolve("noten", DietaryTagKind.Diet).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_UnknownName_YieldsEmptySet()
    {
        // Unmapped tags stay prompt-only (not machine-checkable), like before the feature
        DietaryTagPresets.Resolve("koosjer", DietaryTagKind.Diet).Should().BeEmpty();
        DietaryTagPresets.Resolve("iets vaags", DietaryTagKind.Allergy).Should().BeEmpty();
    }
}
