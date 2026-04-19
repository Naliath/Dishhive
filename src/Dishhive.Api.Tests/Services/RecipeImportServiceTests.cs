using System.Net;
using System.Reflection;
using Dishhive.Api.Services.Sources;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// Tests for the Dagelijkse Kost recipe source provider.
/// Uses a saved HTML fixture to ensure tests are deterministic and network-independent.
/// </summary>
public class RecipeImportServiceTests
{
    private readonly ILogger<DagelijksekostSourceProvider> _logger;

    public RecipeImportServiceTests()
    {
        _logger = Substitute.For<ILogger<DagelijksekostSourceProvider>>();
    }

    // -------------------------------------------------------------------------
    // CanHandle tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CanHandle_WithDagelijksekostUrl_ReturnsTrue()
    {
        var provider = CreateProvider();
        provider.CanHandle("https://dagelijksekost.vrt.be/gerechten/spaghetti-bolognese").Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WithWwwSubdomain_ReturnsTrue()
    {
        var provider = CreateProvider();
        provider.CanHandle("https://www.dagelijksekost.vrt.be/gerechten/some-recipe").Should().BeTrue();
    }

    [Fact]
    public void CanHandle_WithDifferentDomain_ReturnsFalse()
    {
        var provider = CreateProvider();
        provider.CanHandle("https://www.google.com/search?q=pasta").Should().BeFalse();
    }

    [Fact]
    public void CanHandle_WithEmptyString_ReturnsFalse()
    {
        var provider = CreateProvider();
        provider.CanHandle(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_WithRelativeUrl_ReturnsFalse()
    {
        var provider = CreateProvider();
        provider.CanHandle("/gerechten/spaghetti").Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // ParseHtml fixture tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ImportAsync_WithDagelijksekostFixture_ExtractsTitle()
    {
        var result = ParseFixture();

        result.Should().NotBeNull();
        result!.Title.Should().Be("Spaghetti bolognese");
    }

    [Fact]
    public void ImportAsync_WithDagelijksekostFixture_ExtractsDescription()
    {
        var result = ParseFixture();

        result.Should().NotBeNull();
        result!.Description.Should().NotBeNullOrWhiteSpace();
        result.Description.Should().Contain("klassieker");
    }

    [Fact]
    public void ImportAsync_WithDagelijksekostFixture_ExtractsIngredients()
    {
        var result = ParseFixture();

        result.Should().NotBeNull();
        result!.Ingredients.Should().NotBeEmpty();
        result.Ingredients.Should().HaveCountGreaterThan(3);
        result.Ingredients.Should().Contain(i => i.RawText.Contains("spaghetti"));
    }

    [Fact]
    public void ImportAsync_WithDagelijksekostFixture_ExtractsSteps()
    {
        var result = ParseFixture();

        result.Should().NotBeNull();
        result!.Steps.Should().NotBeEmpty();
        result.Steps.Should().HaveCountGreaterThan(3);
        result.Steps.First().Should().Contain("olijfolie");
    }

    [Fact]
    public void ImportAsync_WithDagelijksekostFixture_ExtractsServings()
    {
        var result = ParseFixture();

        result.Should().NotBeNull();
        result!.Servings.Should().Be(4);
    }

    [Fact]
    public void ImportAsync_WithDagelijksekostFixture_ExtractsPictureUrl()
    {
        var result = ParseFixture();

        result.Should().NotBeNull();
        result!.PictureUrl.Should().NotBeNullOrWhiteSpace();
        result.PictureUrl.Should().StartWith("https://");
    }

    [Fact]
    public void ImportAsync_WithDagelijksekostFixture_ExtractsVideoUrl()
    {
        var result = ParseFixture();

        result.Should().NotBeNull();
        result!.VideoUrl.Should().NotBeNullOrWhiteSpace();
        result.VideoUrl.Should().Contain("vrt");
    }

    [Fact]
    public void ImportAsync_WithDagelijksekostFixture_ExtractsSourceLink()
    {
        const string sourceUrl = "https://dagelijksekost.vrt.be/gerechten/spaghetti-bolognese";
        var result = ParseFixture(sourceUrl);

        result.Should().NotBeNull();
        result!.SourceUrl.Should().Be(sourceUrl);
    }

    [Fact]
    public void ImportAsync_WithDagelijksekostFixture_SetsSourceName()
    {
        var result = ParseFixture();

        result.Should().NotBeNull();
        result!.SourceName.Should().Be("DagelijkseKost");
    }

    [Fact]
    public void ImportAsync_WithDagelijksekostFixture_StoresRawData()
    {
        var result = ParseFixture();

        result.Should().NotBeNull();
        result!.SourceRawData.Should().NotBeNullOrWhiteSpace();
    }

    // -------------------------------------------------------------------------
    // Error / edge case tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseHtml_WithEmptyHtml_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = provider.ParseHtml(string.Empty, "https://dagelijksekost.vrt.be/test");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseHtml_WithHtmlWithoutJsonLd_ReturnsNull()
    {
        var provider = CreateProvider();
        var html = "<html><body><h1>Test Recipe</h1></body></html>";
        var result = provider.ParseHtml(html, "https://dagelijksekost.vrt.be/test");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseHtml_WithMalformedJsonLd_ReturnsNull()
    {
        var provider = CreateProvider();
        var html = """
            <html>
            <head>
              <script type="application/ld+json">{ this is not valid json }</script>
            </head>
            <body><h1>Test</h1></body>
            </html>
            """;
        var result = provider.ParseHtml(html, "https://dagelijksekost.vrt.be/test");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseHtml_WithNonRecipeJsonLd_ReturnsNull()
    {
        var provider = CreateProvider();
        var html = """
            <html>
            <head>
              <script type="application/ld+json">{"@type": "WebSite", "name": "Dagelijkse Kost"}</script>
            </head>
            <body><h1>Test</h1></body>
            </html>
            """;
        var result = provider.ParseHtml(html, "https://dagelijksekost.vrt.be/test");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ImportFromUrlAsync_WhenHttpRequestFails_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler);
        var provider = new DagelijksekostSourceProvider(httpClient, _logger);

        var result = await provider.ImportFromUrlAsync("https://dagelijksekost.vrt.be/gerechten/some-recipe");

        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Live HTTP tests — require network access, skipped in CI
    // Run with: dotnet test --filter "Category=Live"
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Live")]
    public async Task ImportFromUrlAsync_LiveSpaghettiBolognaise_ReturnsCompleteRecipe()
    {
        const string url = "https://dagelijksekost.vrt.be/gerechten/spaghetti-bolognaise-0";
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (compatible; DishhiveBot/1.0)");

        var provider = new DagelijksekostSourceProvider(httpClient, _logger);

        var result = await provider.ImportFromUrlAsync(url);

        result.Should().NotBeNull();
        result!.Title.Should().NotBeNullOrWhiteSpace();
        result.SourceUrl.Should().Be(url);
        result.SourceName.Should().Be("DagelijkseKost");

        result.Ingredients.Should().NotBeEmpty("a real recipe should have ingredients");
        result.Ingredients.Should().HaveCountGreaterThan(3);
        result.Ingredients.Should().AllSatisfy(i => i.RawText.Should().NotBeNullOrWhiteSpace());

        result.Steps.Should().NotBeEmpty("a real recipe should have preparation steps");
        result.Steps.Should().HaveCountGreaterThan(2,
            "kookmodus page should return all steps, not just the first two");
        result.Steps.Should().AllSatisfy(s => s.Should().NotBeNullOrWhiteSpace());

        result.Servings.Should().BePositive();
        result.PictureUrl.Should().StartWith("https://");
        result.SourceRawData.Should().NotBeNullOrWhiteSpace();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private DagelijksekostSourceProvider CreateProvider() =>
        new DagelijksekostSourceProvider(new HttpClient(), _logger);

    private Models.DTOs.ImportedRecipeDto? ParseFixture(
        string sourceUrl = "https://dagelijksekost.vrt.be/gerechten/spaghetti-bolognese")
    {
        var html = LoadFixture("dagelijksekost-sample.html");
        var provider = CreateProvider();
        return provider.ParseHtml(html, sourceUrl);
    }

    private static string LoadFixture(string filename)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(filename, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            throw new FileNotFoundException($"Test fixture not found: {filename}");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Mock HTTP handler for testing HTTP error scenarios.
/// </summary>
internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _content;

    public MockHttpMessageHandler(HttpStatusCode statusCode, string content = "")
    {
        _statusCode = statusCode;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_content)
        });
    }
}
