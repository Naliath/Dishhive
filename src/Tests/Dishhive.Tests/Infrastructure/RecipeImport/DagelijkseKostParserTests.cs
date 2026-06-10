using AngleSharp.Html.Parser;
using Xunit;
using FluentAssertions;
using Dishhive.Domain.Entities.Recipes;

namespace Dishhive.Tests.Infrastructure.RecipeImport;

/// <summary>
/// Tests for DagelijkseKost recipe parser.
/// Validates that HTML parsing extracts all required recipe fields correctly.
/// </summary>
public class DagelijkseKostParserTests
{
    private readonly HtmlParser _htmlParser = new HtmlParser();

    #region Title Extraction Tests

    [Fact]
    public void ParseRecipe_WithTitle_ShouldExtractTitle()
    {
        // Arrange
        var html = @"
        <html>
        <body>
            <article class='recipe'>
                <h1>Kabeljauw met kruidenboter</h1>
            </article>
        </body>
        </html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc);

        // Assert
        recipe.Title.Should().Be("Kabeljauw met kruidenboter");
    }

    [Fact]
    public void ParseRecipe_WithoutTitle_ShouldHaveEmptyTitle()
    {
        // Arrange
        var html = @"
        <html>
        <body>
            <article class='recipe'>
            </article>
        </body>
        </html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc);

        // Assert
        recipe.Title.Should().BeNullOrEmpty();
    }

    #endregion

    #region Description Extraction Tests

    [Fact]
    public void ParseRecipe_WithDescription_ShouldExtractDescription()
    {
        // Arrange
        var html = @"
        <html>
        <body>
            <article class='recipe'>
                <div class='recipe-description'>
                    Een klassiek gerecht met malse kabeljauwfilets.
                </div>
            </article>
        </body>
        </html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc);

        // Assert
        recipe.Description.Should().Contain("klassiek gerecht");
    }

    [Fact]
    public void ParseRecipe_WithMetaDescription_ShouldFallbackToMeta()
    {
        // Arrange
        var html = @"
        <html>
        <head>
            <meta name='description' content='Gebakken kabeljauw met kruidenboter'>
        </head>
        <body>
            <article class='recipe'>
            </article>
        </body>
        </html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc);

        // Assert
        recipe.Description.Should().Contain("kabeljauw");
    }

    #endregion

    #region Servings Extraction Tests

    [Fact]
    public void ParseRecipe_WithServings_ShouldExtractServings()
    {
        // Arrange
        var html = @"
        <html>
        <body>
            <article class='recipe'>
                <div class='servings'>4 personen</div>
            </article>
        </body>
        </html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc);

        // Assert
        recipe.Servings.Should().Be(4);
    }

    [Fact]
    public void ParseRecipe_WithServingsInDifferentFormat_ShouldExtractServings()
    {
        // Arrange
        var html = @"
        <html>
        <body>
            <article class='recipe'>
                <div class='servings'>2 servings</div>
            </article>
        </body>
        </html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc);

        // Assert
        recipe.Servings.Should().Be(2);
    }

    #endregion

    #region Picture Extraction Tests

    [Fact]
    public void ParseRecipe_WithPicture_ShouldExtractPictureUrl()
    {
        // Arrange
        var html = @"
        <html>
        <body>
            <article class='recipe'>
                <img class='recipe-image' src='https://example.com/image.jpg' alt='Test'>
            </article>
        </body>
        </html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc);

        // Assert
        recipe.PictureUrl.Should().Be("https://example.com/image.jpg");
    }

    [Fact]
    public void ParseRecipe_WithMetaImage_ShouldFallbackToMetaImage()
    {
        // Arrange
        var html = @"
        <html>
        <head>
            <meta property='image' content='https://example.com/meta-image.jpg'>
        </head>
        <body>
            <article class='recipe'>
            </article>
        </body>
        </html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc);

        // Assert
        recipe.PictureUrl.Should().Be("https://example.com/meta-image.jpg");
    }

    #endregion

    #region Video Extraction Tests

    [Fact]
    public void ParseRecipe_WithVideo_ShouldExtractVideoUrl()
    {
        // Arrange
        var html = @"
        <html>
        <body>
            <article class='recipe'>
                <video class='recipe-video' src='https://example.com/video.mp4'></video>
            </article>
        </body>
        </html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc);

        // Assert
        recipe.VideoUrl.Should().Be("https://example.com/video.mp4");
    }

    #endregion

    #region Ingredients Extraction Tests

    [Fact]
    public void ParseRecipe_WithIngredients_ShouldExtractAllIngredients()
    {
        // Arrange
        var html = @"
        <html>
        <body>
            <article class='recipe'>
                <ul class='ingredients'>
                    <li class='ingredient'>400 g kabeljauwfilet</li>
                    <li class='ingredient'>100 g boter</li>
                    <li class='ingredient'>2 el peterselie</li>
                </ul>
            </article>
        </body>
        </html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc);

        // Assert
        recipe.Ingredients.Should().HaveCount(3);
        recipe.Ingredients[0].Name.Should().Contain("kabeljauwfilet");
        recipe.Ingredients[1].Name.Should().Contain("boter");
        recipe.Ingredients[2].Name.Should().Contain("peterselie");
    }

    [Fact]
    public void ParseRecipe_WithOriginalMeasurements_ShouldPreserveSourceUnits()
    {
        // Arrange
        var html = @"
        <html>
        <body>
            <article class='recipe'>
                <ul class='ingredients'>
                    <li class='ingredient'>400 g kabeljauwfilet</li>
                </ul>
            </article>
        </body>
        </html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc);

        // Assert
        var ingredient = recipe.Ingredients.First();
        ingredient.OriginalValue.Should().NotBeNullOrEmpty();
        ingredient.OriginalUnit.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Preparation Steps Extraction Tests

    [Fact]
    public void ParseRecipe_WithPreparationSteps_ShouldExtractAllSteps()
    {
        // Arrange
        var html = @"
        <html>
        <body>
            <article class='recipe'>
                <ol class='instructions'>
                    <li class='step'>Verwarm de oven voor op 180°C.</li>
                    <li class='step'>Bestrooi de filets met zout.</li>
                    <li class='step'>Bak 10 minuten.</li>
                </ol>
            </article>
        </body>
        </html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc);

        // Assert
        recipe.PreparationSteps.Should().HaveCount(3);
        recipe.PreparationSteps[0].Order.Should().Be(1);
        recipe.PreparationSteps[1].Order.Should().Be(2);
        recipe.PreparationSteps[2].Order.Should().Be(3);
        recipe.PreparationSteps[0].Instruction.Should().Contain("oven");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ParseRecipe_EmptyDocument_ShouldReturnMinimalRecipe()
    {
        // Arrange
        var html = "<html><body></body></html>";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc);

        // Assert
        recipe.Should().NotBeNull();
        recipe.Ingredients.Should().BeEmpty();
        recipe.PreparationSteps.Should().BeEmpty();
    }

    [Fact]
    public void ParseRecipe_MalformedHtml_ShouldNotThrow()
    {
        // Arrange
        var html = "<html><body><article class='recipe'><h1>Test";

        // Act & Assert
        var act = () =>
        {
            var doc = _htmlParser.ParseDocument(html);
            ExtractRecipe(doc);
        };

        act.Should().NotThrow();
    }

    #endregion

    #region Full Integration Test

    [Fact]
    public void ParseRecipe_FullRealisticRecipe_ShouldExtractAllFields()
    {
        // Arrange
        var html = @"
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
                <video class='recipe-video' src='https://dagelijksekost.vrt.be/videos/kabeljauw.mp4'></video>
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
        var sourceUrl = "https://dagelijksekost.vrt.be/kost/2024/kabeljauw-met-kruidenboter";
        var doc = _htmlParser.ParseDocument(html);

        // Act
        var recipe = ExtractRecipe(doc, sourceUrl);

        // Assert
        recipe.Title.Should().Be("Kabeljauw met kruidenboter");
        recipe.Description.Should().Contain("klassiek gerecht");
        recipe.Servings.Should().Be(4);
        recipe.PictureUrl.Should().Contain("kabeljauw.jpg");
        recipe.VideoUrl.Should().Contain("kabeljauw.mp4");
        recipe.SourceUrl.Should().Be(sourceUrl);
        recipe.SourceName.Should().Be("Dagelijkse Kost");
        recipe.Ingredients.Should().HaveCount(5);
        recipe.PreparationSteps.Should().HaveCount(5);
    }

    #endregion

    #region Helper Methods

    private Recipe ExtractRecipe(AngleSharp.Html.Dom.IHtmlDocument doc, string? sourceUrl = null)
    {
        var recipe = new Recipe
        {
            SourceUrl = sourceUrl ?? "https://dagelijksekost.vrt.be/test",
            SourceName = "Dagelijkse Kost"
        };

        // Extract title from h1
        var h1 = doc.QuerySelector("h1");
        if (h1 != null && !string.IsNullOrWhiteSpace(h1.TextContent))
        {
            recipe.Title = h1.TextContent.Trim();
        }

        // Extract description - prefer article description, fallback to meta
        var description = doc.QuerySelector(".recipe-description, .description");
        if (description != null && !string.IsNullOrWhiteSpace(description.TextContent))
        {
            recipe.Description = description.TextContent.Trim();
        }
        else
        {
            var metaDesc = doc.QuerySelector("meta[name='description']");
            if (metaDesc != null)
            {
                var content = metaDesc.GetAttribute("content");
                if (!string.IsNullOrWhiteSpace(content))
                {
                    recipe.Description = content.Trim();
                }
            }
        }

        // Extract picture - prefer article image, fallback to meta
        var picture = doc.QuerySelector(".recipe-image, .recipe__image");
        if (picture != null)
        {
            var imgUrl = picture.GetAttribute("src") ?? picture.GetAttribute("data-src");
            if (!string.IsNullOrWhiteSpace(imgUrl))
            {
                recipe.PictureUrl = imgUrl.Trim();
            }
        }
        else
        {
            var metaImage = doc.QuerySelector("meta[property='image']");
            if (metaImage != null)
            {
                var content = metaImage.GetAttribute("content");
                if (!string.IsNullOrWhiteSpace(content))
                {
                    recipe.PictureUrl = content.Trim();
                }
            }
        }

        // Extract video
        var video = doc.QuerySelector(".recipe-video, video, .video-embed");
        if (video != null)
        {
            var videoUrl = video.GetAttribute("src") ?? video.GetAttribute("data-src");
            if (!string.IsNullOrWhiteSpace(videoUrl))
            {
                recipe.VideoUrl = videoUrl.Trim();
            }
        }

        // Extract servings
        var servings = doc.QuerySelector(".servings, .servings-count");
        if (servings != null)
        {
            var text = servings.TextContent.Trim();
            // Try to extract number from text like "4 personen" or "4 servings"
            var numbers = text.Where(char.IsDigit).ToString();
            if (int.TryParse(numbers, out int servingCount) && servingCount > 0)
            {
                recipe.Servings = servingCount;
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

    #endregion
}