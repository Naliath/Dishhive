using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Collections;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Tests.Services;

/// <summary>
/// #[Collection Name] mention extraction (pure regex) and resolution against
/// manual and auto collections. See docs/features/ai-week-planning.md.
/// </summary>
public class CollectionMentionResolverTests : IDisposable
{
    private readonly DishhiveDbContext _context;
    private readonly CollectionMentionResolver _resolver;

    public CollectionMentionResolverTests()
    {
        var options = new DbContextOptionsBuilder<DishhiveDbContext>()
            .UseInMemoryDatabase($"MentionTests_{Guid.NewGuid()}")
            .Options;
        _context = new DishhiveDbContext(options);
        _resolver = new CollectionMentionResolver(_context, new AutoCollectionProvider(_context));
    }

    // ---------------------------------------------------------------- extraction

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something quick with fish")]
    [InlineData("unclosed #[Easy Weekday")]   // no terminator: not a reference
    [InlineData("no name #[] here")]          // empty name
    public void ExtractMentions_NoCompleteReference_ReturnsEmpty(string? text)
    {
        CollectionMentionResolver.ExtractMentions(text).Should().BeEmpty();
    }

    [Fact]
    public void ExtractMentions_FindsReferences_InSurroundingProse()
    {
        var mentions = CollectionMentionResolver.ExtractMentions(
            "friday something from #[Easy Weekday Dishes], else #[Comfort Food]");

        mentions.Should().Equal("Easy Weekday Dishes", "Comfort Food");
    }

    [Fact]
    public void ExtractMentions_AdjacentAndDuplicateReferences_AreDistinct()
    {
        var mentions = CollectionMentionResolver.ExtractMentions(
            "#[A]#[B] and again #[a]");

        mentions.Should().Equal("A", "B");
    }

    [Fact]
    public void ExtractMentions_NameWithParens_IsSupported()
    {
        // Parens are legal in names — only brackets are banned (the delimiter)
        CollectionMentionResolver.ExtractMentions("use #[Quick (max 30 min)] today")
            .Should().Equal("Quick (max 30 min)");
    }

    // ---------------------------------------------------------------- resolution

    [Fact]
    public async Task Resolve_ManualCollection_MatchesCaseInsensitively_WithCanonicalName()
    {
        await SeedCollectionAsync("Easy Weekday Dishes", "Wrap", "Pasta pesto");

        var constraints = await _resolver.ResolveAsync(
            [(new DateOnly(2026, 6, 19), "something from #[easy weekday dishes]")]);

        var constraint = constraints.Should().ContainSingle().Subject;
        constraint.Name.Should().Be("Easy Weekday Dishes");
        constraint.RecipeTitles.Should().BeEquivalentTo("Wrap", "Pasta pesto");
        constraint.Dates.Should().Equal(new DateOnly(2026, 6, 19));
    }

    [Fact]
    public async Task Resolve_DanglingReference_ProducesNoConstraint()
    {
        var constraints = await _resolver.ResolveAsync(
            [(new DateOnly(2026, 6, 19), "something from #[Renamed Collection]")]);

        constraints.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_SameCollectionFromDayAndGlobalTexts_MergesDates()
    {
        await SeedCollectionAsync("Comfort Food", "Stew");

        var constraints = await _resolver.ResolveAsync(
        [
            (new DateOnly(2026, 6, 17), "#[Comfort Food] please"),
            (new DateOnly(2026, 6, 19), "again #[comfort food]"),
            (null, "prefer #[Comfort Food] this week")
        ]);

        var constraint = constraints.Should().ContainSingle().Subject;
        constraint.Dates.Should().Equal(new DateOnly(2026, 6, 17), new DateOnly(2026, 6, 19));
    }

    [Fact]
    public async Task Resolve_TitlesAreCappedAndLeastRecentlyPlannedFirst()
    {
        var titles = Enumerable.Range(1, 20).Select(i => $"Dish {i:00}").ToArray();
        await SeedCollectionAsync("Big", titles);

        // Dish 01 was planned recently; never-planned dishes outrank it
        var constraints = await _resolver.ResolveAsync(
            [(null, "#[Big]")],
            new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dish 01"] = new DateOnly(2026, 6, 10)
            });

        var constraint = constraints.Should().ContainSingle().Subject;
        constraint.RecipeTitles.Should().HaveCount(15);
        constraint.RecipeTitles.Should().NotContain("Dish 01");
        constraint.RecipeTitles[0].Should().Be("Dish 02");
    }

    [Fact]
    public async Task Resolve_AutoCollection_ResolvesByName()
    {
        _context.Recipes.Add(new Recipe { Id = Guid.NewGuid(), Title = "Wrap", TotalTimeMinutes = 20 });
        _context.Recipes.Add(new Recipe { Id = Guid.NewGuid(), Title = "Stoofpot", TotalTimeMinutes = 180 });
        await _context.SaveChangesAsync();

        var constraints = await _resolver.ResolveAsync([(null, "#[Quick (max 30 min)]")]);

        var constraint = constraints.Should().ContainSingle().Subject;
        constraint.Name.Should().Be("Quick (max 30 min)");
        constraint.RecipeTitles.Should().Equal("Wrap");
    }

    [Fact]
    public async Task Resolve_DisabledAutoCollection_DoesNotResolve()
    {
        _context.Recipes.Add(new Recipe { Id = Guid.NewGuid(), Title = "Wrap", TotalTimeMinutes = 20 });
        _context.UserSettings.Add(new Models.UserSetting
        {
            Key = AutoCollectionProvider.DisabledSettingKey,
            Value = "[\"auto-quick\"]"
        });
        await _context.SaveChangesAsync();

        var constraints = await _resolver.ResolveAsync([(null, "#[Quick (max 30 min)]")]);

        constraints.Should().BeEmpty();
    }

    private async Task SeedCollectionAsync(string name, params string[] recipeTitles)
    {
        var cookbook = new Cookbook { Id = Guid.NewGuid(), Name = name };
        foreach (var title in recipeTitles)
        {
            var recipe = new Recipe { Id = Guid.NewGuid(), Title = title };
            _context.Recipes.Add(recipe);
            cookbook.Entries.Add(new CookbookEntry { Cookbook = cookbook, RecipeId = recipe.Id });
        }
        _context.Cookbooks.Add(cookbook);
        await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
