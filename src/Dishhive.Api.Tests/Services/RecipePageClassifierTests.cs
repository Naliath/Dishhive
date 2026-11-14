using Dishhive.Api.Services.WebSearch;

namespace Dishhive.Api.Tests.Services;

public class RecipePageClassifierTests
{
    [Theory]
    [InlineData("https://dagelijksekost.vrt.be/gerechten/vegan-curry", true)]
    [InlineData("https://www.dagelijksekost.vrt.be/gerechten/kip-met-rijst", true)]
    [InlineData("https://dagelijksekost.vrt.be/kookvragen/vegan-vervanger", false)]
    [InlineData("https://dagelijksekost.vrt.be/themas/vegan", false)]
    [InlineData("https://dagelijksekost.vrt.be/kookmagazine/vegan-maaltijden", false)]
    [InlineData("https://dagelijksekost.vrt.be/gerechten/", false)]
    [InlineData("https://other.example/gerechten/something", true)]
    [InlineData("https://other.example/category/vegan", false)]
    public void KnownRoutesExcludeNonRecipePagesWithoutGuessingDietarySuitability(string url, bool expected)
        => Assert.Equal(expected, RecipePageClassifier.IsPotentialRecipeUri(url));
}
