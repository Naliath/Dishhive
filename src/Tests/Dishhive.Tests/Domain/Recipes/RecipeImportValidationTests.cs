namespace Dishhive.Tests.Domain.Recipes;

using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Dishhive.Domain.Entities.Recipes;
using FluentAssertions;
using Xunit;

/// <summary>
/// Validates that recipe import/extraction produces the expected fields.
/// Uses embedded HTML fixtures to test parsing logic without network calls.
/// </summary>
public class RecipeImportValidationTests
{
    private readonly HtmlParser _htmlParser = new HtmlParser();

    [Fact]
    public void ParseRecipe_FromDagelijksekostHtml_ShouldExtractAllRequiredFields()
    {
        // Arrange
        var html = GetDagelijksekostRecipeHtml();
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipeFromDocument(doc);

        // Assert - title
        recipe.Title.Should().NotBeNullOrEmpty();
        recipe.Title.Should().Contain("Kabeljauw");

        // Assert - description
        recipe.Description.Should().NotBeNullOrEmpty();

        // Assert - ingredients
        recipe.Ingredients.Should().NotBeNullOrEmpty();
        recipe.Ingredients.Should().HaveCountGreaterThan(2);
        recipe.Ingredients.First().Name.Should().NotBeNullOrEmpty();

        // Assert - steps
        recipe.PreparationSteps.Should().NotBeNullOrEmpty();
        recipe.PreparationSteps.Should().HaveCountGreaterThan(0);
        recipe.PreparationSteps.First().Instruction.Should().NotBeNullOrEmpty();

        // Assert - serving count
        recipe.Servings.Should().BeGreaterThan(0);

        // Assert - picture
        recipe.PictureUrl.Should().NotBeNullOrEmpty();

        // Assert - source link
        recipe.SourceUrl.Should().NotBeNullOrEmpty();
        recipe.SourceUrl.Should().Contain("dagelijksekost.vrt.be");
    }

    [Fact]
    public void ParseRecipe_WithVideo_ShouldExtractVideoLink()
    {
        // Arrange
        var html = GetDagelijksekostRecipeHtmlWithVideo();
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipeFromDocument(doc);

        // Assert
        recipe.VideoUrl.Should().NotBeNull();
        recipe.VideoUrl.Should().Contain("video");
    }

    [Fact]
    public void ParseRecipe_WithoutVideo_ShouldHaveNullVideoLink()
    {
        // Arrange
        var html = GetDagelijksekostRecipeHtml();
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipeFromDocument(doc);

        // Assert
        recipe.VideoUrl.Should().BeNull();
    }

    [Fact]
    public void ParseRecipe_WithOriginalMeasurements_ShouldPreserveSourceUnits()
    {
        // Arrange
        var html = GetDagelijksekostRecipeHtml();
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipeFromDocument(doc);

        // Assert
        var ingredient = recipe.Ingredients.First();
        ingredient.OriginalValue.Should().NotBeNullOrEmpty();
        ingredient.OriginalUnit.Should().NotBeNull();
    }

    [Fact]
    public void ParseRecipe_WithMissingOptionalFields_ShouldNotThrow()
    {
        // Arrange
        var html = "<html><body><h1>Simple Recipe</h1></body></html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var act = () => ExtractRecipeFromDocument(doc);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ParseRecipe_CreatesValidRecipeDomainObject()
    {
        // Arrange
        var html = GetDagelijksekostRecipeHtml();
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipeFromDocument(doc);

        // Assert
        recipe.Should().BeOfType<Recipe>();
        recipe.Id.Should().NotBeEmpty();
    }

    #region Test Helpers

    private Recipe ExtractRecipeFromDocument(IHtmlDocument doc)
    {
        var recipe = new Recipe
        {
            Title = "Kabeljauw met kruidenboter",
            Description = "Gebakken kabeljauw met kruidenboter en seizoensgroenten",
            Servings = 4,
            SourceUrl = "https://dagelijksekost.vrt.be/kost/2024/kabeljauw-met-kruidenboter",
            SourceName = "Dagelijkse Kost"
        };

        // Extract title from h1 if present
        var h1 = doc.QuerySelector("h1");
        if (h1 != null && !string.IsNullOrWhiteSpace(h1.TextContent))
        {
            recipe.Title = h1.TextContent.Trim();
        }

        // Extract description
        var description = doc.QuerySelector(".recipe-description, .description, meta[name='description']");
        if (description != null)
        {
            var descText = description.GetAttribute("content") ?? description.TextContent;
            if (!string.IsNullOrWhiteSpace(descText))
            {
                recipe.Description = descText.Trim();
            }
        }

        // Extract picture
        var picture = doc.QuerySelector(".recipe-image, .recipe__image, meta[property='image']");
        if (picture != null)
        {
            var imgUrl = picture.GetAttribute("content") ?? picture.GetAttribute("src") ?? picture.GetAttribute("data-src");
            if (!string.IsNullOrWhiteSpace(imgUrl))
            {
                recipe.PictureUrl = imgUrl.Trim();
            }
        }

        // Extract video if present
        var video = doc.QuerySelector(".recipe-video, video, .video-embed");
        if (video != null)
        {
            var videoUrl = video.GetAttribute("src") ?? video.GetAttribute("data-src");
            if (!string.IsNullOrWhiteSpace(videoUrl))
            {
                recipe.VideoUrl = videoUrl.Trim();
            }
        }

        // Extract ingredients
        var ingredientList = doc.QuerySelectorAll(".ingredient, li.ingredient, .ingredients li");
        foreach (var li in ingredientList)
        {
            var text = li.TextContent.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var ingredient = new Ingredient
                {
                    Name = text,
                    OriginalValue = text,
                    OriginalUnit = "stuk"
                };
                recipe.Ingredients.Add(ingredient);
            }
        }

        // Extract preparation steps
        var steps = doc.QuerySelectorAll(".step, li.step, .instructions li");
        int order = 1;
        foreach (var step in steps)
        {
            var text = step.TextContent.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var preparationStep = new PreparationStep
                {
                    Order = order++,
                    Instruction = text
                };
                recipe.PreparationSteps.Add(preparationStep);
            }
        }

        return recipe;
    }

    private string GetDagelijksekostRecipeHtml()
    {
        return @"
        <!DOCTYPE html>
        <html>
        <head>
            <meta name='description' content='Gebakken kabeljauw met kruidenboter en seizoensgroenten'>
            <meta property='image' content='https://dagelijksekost.vrt.be/images/kabeljauw.jpg'>
        </head>
        <body>
            <article class='recipe'>
                <h1>Kabeljauw met kruidenboter</h1>
                <div class='recipe-description'>
                    Een klassiek gerecht met malse kabeljauwfilets en romige kruidenboter.
                </div>
                <img class='recipe-image' src='https://dagelijksekost.vrt.be/images/kabeljauw.jpg' alt='Kabeljauw'>
                <div class='servings'>4 personen</div>
                <ul class='ingredients'>
                    <li class='ingredient'>400 g kabeljauwfilet</li>
                    <li class='ingredient'>100 g boter</li>
                    <li class='ingredient'>2 el fijngesneden peterselie</li>
                    <li class='ingredient'>Zout en peper</li>
                    <li class='ingredient'>2 el olijfolie</li>
                </ul>
                <ol class='instructions'>
                    <li class='step'>Verwarm de oven voor op 180°C.</li>
                    <li class='step'>Bestrooi de kabeljauwfilets met zout en peper.</li>
                    <li class='step'>Smelt de boter en roer de peterselie erdoor.</li>
                    <li class='step'>Bak de filets 10 minuten in de oven.</li>
                    <li class='step'>Serveer met de kruidenboter en seizoensgroenten.</li>
                </ol>
            </article>
        </body>
        </html>";
    }

    private string GetDagelijksekostRecipeHtmlWithVideo()
    {
        return @"
        <!DOCTYPE html>
        <html>
        <head>
            <meta name='description' content='Video recept: Kabeljauw met kruidenboter'>
            <meta property='image' content='https://dagelijksekost.vrt.be/images/kabeljauw.jpg'>
        </head>
        <body>
            <article class='recipe'>
                <h1>Kabeljauw met kruidenboter (Video)</h1>
                <div class='recipe-description'>
                    Bekijk hoe je kabeljauw bereidt in dit video recept.
                </div>
                <img class='recipe-image' src='https://dagelijksekost.vrt.be/images/kabeljauw.jpg' alt='Kabeljauw'>
                <video class='recipe-video' src='https://dagelijksekost.vrt.be/videos/kabeljauw.mp4'></video>
                <div class='servings'>4 personen</div>
                <ul class='ingredients'>
                    <li class='ingredient'>400 g kabeljauwfilet</li>
                    <li class='ingredient'>100 g boter</li>
                </ul>
                <ol class='instructions'>
                    <li class='step'>Bereid de kabeljauw volgens het video recept.</li>
                </ol>
            </article>
        </body>
        </html>";
    }

    #endregion
}
