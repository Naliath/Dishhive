using Dishhive.Api.Services;
using FluentAssertions;
using Xunit;

namespace Dishhive.Api.Tests.Services;

public class MeasurementConversionServiceTests
{
    private readonly MeasurementConversionService _sut = new();

    // ── Metric pass-through ───────────────────────────────────────────────

    [Theory]
    [InlineData(400, "g", "metric")]
    [InlineData(1, "kg", "metric")]
    [InlineData(250, "ml", "metric")]
    [InlineData(1.5, "l", "metric")]
    public void Convert_MetricToMetric_ReturnsUnchanged(double qty, string unit, string target)
    {
        var (q, u) = _sut.Convert((decimal)qty, unit, target);
        q.Should().Be((decimal)qty);
        u.Should().Be(unit);
    }

    // ── Metric → Imperial ─────────────────────────────────────────────────

    [Fact]
    public void Convert_Grams_ToOunces()
    {
        var (q, u) = _sut.Convert(100, "g", "imperial");
        q.Should().BeApproximately(3.53m, 0.01m);
        u.Should().Be("oz");
    }

    [Fact]
    public void Convert_Kilograms_ToPounds()
    {
        var (q, u) = _sut.Convert(1, "kg", "imperial");
        q.Should().BeApproximately(2.20m, 0.01m);
        u.Should().Be("lb");
    }

    [Fact]
    public void Convert_Millilitres_ToFlOz()
    {
        var (q, u) = _sut.Convert(100, "ml", "imperial");
        q.Should().BeApproximately(3.38m, 0.01m);
        u.Should().Be("fl oz");
    }

    [Fact]
    public void Convert_Litres_ToCups()
    {
        var (q, u) = _sut.Convert(1, "l", "imperial");
        q.Should().BeApproximately(4.23m, 0.01m);
        u.Should().Be("cups");
    }

    // ── Imperial → Metric ─────────────────────────────────────────────────

    [Fact]
    public void Convert_Ounces_ToGrams()
    {
        var (q, u) = _sut.Convert(1, "oz", "metric");
        q.Should().BeApproximately(28.35m, 0.01m);
        u.Should().Be("g");
    }

    [Fact]
    public void Convert_Pounds_ToKilograms()
    {
        var (q, u) = _sut.Convert(1, "lb", "metric");
        q.Should().BeApproximately(0.45m, 0.01m);
        u.Should().Be("kg");
    }

    [Fact]
    public void Convert_Cups_ToMillilitres()
    {
        var (q, u) = _sut.Convert(1, "cups", "metric");
        q.Should().BeApproximately(236.59m, 0.01m);
        u.Should().Be("ml");
    }

    // ── Unknown units pass-through ────────────────────────────────────────

    [Fact]
    public void Convert_UnknownUnit_ReturnsOriginalUnchanged()
    {
        var (q, u) = _sut.Convert(3, "cloves", "imperial");
        q.Should().Be(3);
        u.Should().Be("cloves");
    }

    // ── Null handling ──────────────────────────────────────────────────────

    [Fact]
    public void Convert_NullQuantity_ReturnsNullQuantity()
    {
        var (q, u) = _sut.Convert(null, "g", "imperial");
        q.Should().BeNull();
    }

    [Fact]
    public void Convert_NullUnit_ReturnsNullUnit()
    {
        var (q, u) = _sut.Convert(100, null, "imperial");
        u.Should().BeNull();
        q.Should().Be(100);
    }

    // ── Format ────────────────────────────────────────────────────────────

    [Fact]
    public void Format_MetricQuantity_ReturnsFriendlyString()
    {
        var result = _sut.Format(400, "g", "metric");
        result.Should().Be("400 g");
    }

    [Fact]
    public void Format_ImperialConversion_ReturnsConvertedString()
    {
        var result = _sut.Format(100, "g", "imperial");
        result.Should().Contain("oz");
    }

    [Fact]
    public void Format_NullQuantityAndUnit_ReturnsEmptyString()
    {
        var result = _sut.Format(null, null, "metric");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Format_QuantityNoUnit_ReturnsJustNumber()
    {
        var result = _sut.Format(3, null, "metric");
        result.Should().Be("3");
    }

    // ── Case-insensitive unit matching ─────────────────────────────────────

    [Theory]
    [InlineData("G")]
    [InlineData("Kg")]
    [InlineData("ML")]
    [InlineData("L")]
    public void Convert_UppercaseUnit_StillConverts(string unit)
    {
        var (_, u) = _sut.Convert(1, unit, "imperial");
        u.Should().NotBe(unit); // should have been converted
    }
}
