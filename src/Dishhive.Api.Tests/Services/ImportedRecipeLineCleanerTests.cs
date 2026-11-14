using Dishhive.Api.Services.Import;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class ImportedRecipeLineCleanerTests
{
    [Theory]
    [InlineData("▢ 2 tablespoons adobo sauce", "2 tablespoons adobo sauce")]
    [InlineData("\t ▢\u00a0½ cup ketchup \r\n", "½ cup ketchup")]
    [InlineData("▢¼ cup flour", "¼ cup flour")]
    [InlineData("▢ ▢▢ sea salt", "sea salt")]
    [InlineData("• 2 eggs", "2 eggs")]
    [InlineData("◦‣⁃∙·●○ salt", "salt")]
    [InlineData("■□▪▫ flour", "flour")]
    [InlineData("☐☑☒✓✔ Mix well.", "Mix well.")]
    [InlineData("☐½ cup ketchup", "½ cup ketchup")]
    [InlineData("- 2 eggs", "2 eggs")]
    [InlineData("+ 2 eggs", "2 eggs")]
    [InlineData("* 2 eggs", "2 eggs")]
    [InlineData("– 2 eggs", "2 eggs")]
    [InlineData("— 2 eggs", "2 eggs")]
    [InlineData("• - ☑ ¼ cup flour", "¼ cup flour")]
    [InlineData("-\u00a0salt", "salt")]
    [InlineData("*", "")]
    [InlineData("▢", "")]
    [InlineData(" ▢ \t▢ ", "")]
    [InlineData("", "")]
    [InlineData(" \t", "")]
    [InlineData("½ cup ketchup", "½ cup ketchup")]
    [InlineData("1/2 cup flour", "1/2 cup flour")]
    [InlineData("1-2 cups flour", "1-2 cups flour")]
    [InlineData("-18°C", "-18°C")]
    [InlineData("+2 eggs", "+2 eggs")]
    [InlineData("–18°C", "–18°C")]
    [InlineData("*optional*", "*optional*")]
    [InlineData("**For serving**", "**For serving**")]
    [InlineData("gluten-free flour", "gluten-free flour")]
    [InlineData("Mix • gently; check ✓ when done.", "Mix • gently; check ✓ when done.")]
    [InlineData("Stir ▢ into the sauce.", "Stir ▢ into the sauce.")]
    public void Clean_RemovesOnlyKnownLeadingListMarkers(string line, string expected)
    {
        ImportedRecipeLineCleaner.Clean(line).Should().Be(expected);
    }
}
