using Dishhive.Api.Models.Settings;
using Dishhive.Api.Services.Units;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class UnitConversionServiceTests
{
    private readonly UnitConversionService _sut = new();

    [Fact]
    public void Grams_convert_to_ounces_for_imperial()
    {
        var (qty, unit) = _sut.ConvertForSystem(100m, "g", MeasurementSystem.Imperial);
        unit.Should().Be("oz");
        qty.Should().BeApproximately(3.527m, 0.01m);
    }

    [Fact]
    public void Grams_stay_grams_for_metric()
    {
        var (qty, unit) = _sut.ConvertForSystem(100m, "g", MeasurementSystem.Metric);
        unit.Should().Be("g");
        qty.Should().Be(100m);
    }

    [Fact]
    public void Unknown_unit_passes_through_unchanged()
    {
        var (qty, unit) = _sut.ConvertForSystem(2m, "pinch", MeasurementSystem.Imperial);
        qty.Should().Be(2m);
        unit.Should().Be("pinch");
    }
}
