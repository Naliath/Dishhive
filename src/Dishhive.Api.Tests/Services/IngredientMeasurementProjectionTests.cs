using Dishhive.Api.Models;
using Dishhive.Api.Services.Import;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class IngredientMeasurementProjectionTests
{
    [Fact]
    public void OldFractionImports_BecomeScalableWithoutChangingStoredData()
    {
        var ingredient = new RecipeIngredient { Name = "½ cup frozen corn", OriginalText = "½ cup frozen corn" };
        var measure = IngredientMeasurementProjection.Resolve(ingredient);
        measure.Quantity.Should().Be(0.5m);
        measure.Unit.Should().Be("cup");
        measure.Name.Should().Be("frozen corn");
        ingredient.Quantity.Should().BeNull();
    }

    [Fact]
    public void OldFixedMlConversion_UsesPreservedCupsAndTranslatedName()
    {
        var ingredient = new RecipeIngredient { Name = "bloem", OriginalText = "2 cups flour",
            Quantity = 480, Unit = "ml", OriginalQuantity = 2, OriginalUnit = "cups" };
        var measure = IngredientMeasurementProjection.Resolve(ingredient);
        measure.Quantity.Should().Be(2);
        measure.Unit.Should().Be("cup");
        measure.Name.Should().Be("bloem");
        ingredient.Quantity.Should().Be(480);
    }

    [Fact]
    public void EditedQuantity_IsNotReplacedBySourceCups()
    {
        var ingredient = new RecipeIngredient { Name = "flour", OriginalText = "2 cups flour",
            Quantity = 500, Unit = "ml", OriginalQuantity = 2, OriginalUnit = "cups" };
        IngredientMeasurementProjection.Resolve(ingredient).Quantity.Should().Be(500);
    }

    [Fact]
    public void PublishedMetricMeasurements_AreNotTreatedAsLegacyCupNormalization()
    {
        var ingredient = new RecipeIngredient { Name = "water", OriginalText = "1 cup water",
            Quantity = 240, Unit = "ml", OriginalQuantity = 1, OriginalUnit = "cup" };
        IngredientMeasurementProjection.Resolve(ingredient, "{\"metricIngredients\":[]}")
            .Quantity.Should().Be(240);
    }
}
