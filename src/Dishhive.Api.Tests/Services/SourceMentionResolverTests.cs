using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// @[Source] mention extraction (pure regex) and resolution against the known-source
/// catalog (dedicated providers + previously-imported hosts, plus bare domains).
/// See docs/features/ai-week-planning.md.
/// </summary>
public class SourceMentionResolverTests : IDisposable
{
    private readonly DishhiveDbContext _context;
    private readonly SourceMentionResolver _resolver;

    public SourceMentionResolverTests()
    {
        var options = new DbContextOptionsBuilder<DishhiveDbContext>()
            .UseInMemoryDatabase($"SourceMentionTests_{Guid.NewGuid()}")
            .Options;
        _context = new DishhiveDbContext(options);
        var catalog = new RecipeSourceCatalog(_context, [new DagelijkseKostProvider()]);
        _resolver = new SourceMentionResolver(catalog);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something quick with fish")]
    [InlineData("unclosed @[Dagelijkse")]
    [InlineData("empty @[] here")]
    public void ExtractMentions_NoCompleteReference_ReturnsEmpty(string? text)
    {
        SourceMentionResolver.ExtractMentions(text).Should().BeEmpty();
    }

    [Fact]
    public void ExtractMentions_FindsReferences_InSurroundingProse()
    {
        SourceMentionResolver.ExtractMentions("vegetarian from @[Dagelijkse Kost] or @[example.com]")
            .Should().Equal("Dagelijkse Kost", "example.com");
    }

    [Fact]
    public void ExtractMentions_AcceptsHandTypedBareDomain()
    {
        SourceMentionResolver.ExtractMentions(
                "dessert from @laurasbakery.nl, but contact cook@example.com for questions")
            .Should().Equal("laurasbakery.nl");
    }

    [Fact]
    public async Task Resolve_KnownProviderName_ResolvesToHost()
    {
        var constraints = await _resolver.ResolveAsync(
            [(new DateOnly(2026, 6, 19), "something from @[dagelijkse kost]")]);

        var constraint = constraints.Should().ContainSingle().Subject;
        constraint.Host.Should().Be("dagelijksekost.vrt.be");
        constraint.Dates.Should().Equal(new DateOnly(2026, 6, 19));
    }

    [Fact]
    public async Task Resolve_BareDomain_IsTakenAsItsOwnHost()
    {
        var constraints = await _resolver.ResolveAsync([(null, "find one on @[www.15gram.be]")]);

        var constraint = constraints.Should().ContainSingle().Subject;
        constraint.Host.Should().Be("15gram.be"); // www. stripped
    }

    [Fact]
    public async Task Resolve_AtPrefixedBareDomain_IsTakenAsItsOwnHost()
    {
        var constraints = await _resolver.ResolveAsync([(null, "find one on @laurasbakery.nl")]);

        constraints.Should().ContainSingle().Which.Host.Should().Be("laurasbakery.nl");
    }

    [Fact]
    public async Task Resolve_UnknownNonDomainName_ProducesNoConstraint()
    {
        var constraints = await _resolver.ResolveAsync([(null, "from @[Grandma's binder]")]);

        constraints.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_PreviouslyImportedHost_IsResolvable()
    {
        _context.Recipes.Add(new Recipe
        {
            Id = Guid.NewGuid(),
            Title = "Imported dish",
            SourceUrl = "https://cooking.example/recipes/pasta"
        });
        await _context.SaveChangesAsync();

        var constraints = await _resolver.ResolveAsync([(null, "another from @[cooking.example]")]);

        constraints.Should().ContainSingle().Which.Host.Should().Be("cooking.example");
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
