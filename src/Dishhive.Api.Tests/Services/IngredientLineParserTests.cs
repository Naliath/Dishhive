using Dishhive.Api.Services.Import;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class IngredientLineParserTests
{
    [Fact]
    public void Parse_MetricMassLine_NormalizesToGrams()
    {
        var result = IngredientLineParser.Parse("200 gram suiker");

        result.Name.Should().Be("suiker");
        result.Quantity.Should().Be(200m);
        result.Unit.Should().Be("g");
        result.OriginalText.Should().Be("200 gram suiker");
        result.OriginalQuantity.Should().Be(200m);
        result.OriginalUnit.Should().Be("gram");
    }

    [Fact]
    public void Parse_MetricVolumeLine_NormalizesToMilliliters()
    {
        var result = IngredientLineParser.Parse("85 milliliter citroensap");

        result.Name.Should().Be("citroensap");
        result.Quantity.Should().Be(85m);
        result.Unit.Should().Be("ml");
    }

    [Fact]
    public void Parse_DecimalCommaQuantity_ParsesAsPieces()
    {
        var result = IngredientLineParser.Parse("0,5 citroenen");

        result.Name.Should().Be("citroenen");
        result.Quantity.Should().Be(0.5m);
        result.Unit.Should().Be("piece");
        result.OriginalText.Should().Be("0,5 citroenen");
    }

    [Fact]
    public void Parse_CountableLine_ParsesAsPieces()
    {
        var result = IngredientLineParser.Parse("3 eierdooiers");

        result.Name.Should().Be("eierdooiers");
        result.Quantity.Should().Be(3m);
        result.Unit.Should().Be("piece");
    }

    [Fact]
    public void Parse_CulinaryUnit_PassesThroughUnconverted()
    {
        var result = IngredientLineParser.Parse("1 blaadje gelatine");

        result.Name.Should().Be("gelatine");
        result.Quantity.Should().Be(1m);
        result.Unit.Should().Be("blaadje");
        result.OriginalUnit.Should().Be("blaadje");
    }

    [Fact]
    public void Parse_LineWithoutQuantity_KeepsFullLineAsName()
    {
        var result = IngredientLineParser.Parse("Cointreau");

        result.Name.Should().Be("Cointreau");
        result.Quantity.Should().BeNull();
        result.Unit.Should().BeNull();
        result.OriginalText.Should().Be("Cointreau");
        result.OriginalQuantity.Should().BeNull();
        result.OriginalUnit.Should().BeNull();
    }

    [Fact]
    public void Parse_ImperialMass_ConvertsToMetricAndPreservesOriginal()
    {
        var result = IngredientLineParser.Parse("2 oz butter");

        result.Name.Should().Be("butter");
        result.Quantity.Should().Be(56.70m);
        result.Unit.Should().Be("g");
        result.OriginalQuantity.Should().Be(2m);
        result.OriginalUnit.Should().Be("oz");
        result.OriginalText.Should().Be("2 oz butter");
    }

    [Fact]
    public void Parse_ImperialVolume_ConvertsToMetricAndPreservesOriginal()
    {
        var result = IngredientLineParser.Parse("2 cups flour");

        result.Quantity.Should().Be(480m);
        result.Unit.Should().Be("ml");
        result.OriginalQuantity.Should().Be(2m);
        result.OriginalUnit.Should().Be("cups");
    }

    [Fact]
    public void Parse_DeciliterLine_ConvertsToMilliliters()
    {
        var result = IngredientLineParser.Parse("2 dl room");

        result.Quantity.Should().Be(200m);
        result.Unit.Should().Be("ml");
    }

    [Fact]
    public void Parse_QuantityWithUnitButNoName_TreatsUnitTokenAsName()
    {
        // "2 eieren" - "eieren" is not a unit, so it becomes the name with piece count
        var result = IngredientLineParser.Parse("2 eieren");

        result.Name.Should().Be("eieren");
        result.Quantity.Should().Be(2m);
        result.Unit.Should().Be("piece");
    }
}
