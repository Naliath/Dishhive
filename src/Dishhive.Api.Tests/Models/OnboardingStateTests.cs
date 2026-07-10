using Dishhive.Api.Models;
using FluentAssertions;

namespace Dishhive.Api.Tests.ModelTests;

public class EnumNamesTests
{
    [Fact]
    public void ToName_DefinedValues_ReturnCanonicalNames()
    {
        var cases = new[]
        {
            (State: OnboardingState.InProgress, Value: "InProgress"),
            (State: OnboardingState.Completed, Value: "Completed"),
            (State: OnboardingState.Skipped, Value: "Skipped")
        };

        foreach (var (state, expectedValue) in cases)
        {
            EnumNames.ToName(state).Should().Be(expectedValue);
        }
    }

    [Fact]
    public void TryParse_DefinedNames_IsCaseInsensitive()
    {
        EnumNames.TryParse<OnboardingState>("InProgress", out var canonical).Should().BeTrue();
        EnumNames.TryParse<OnboardingState>("completed", out var camelCase).Should().BeTrue();

        canonical.Should().Be(OnboardingState.InProgress);
        camelCase.Should().Be(OnboardingState.Completed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("unknown")]
    public void TryParse_UnknownOrNumericValue_ReturnsFalse(string? value)
    {
        EnumNames.TryParse<OnboardingState>(value, out _).Should().BeFalse();
    }
}
