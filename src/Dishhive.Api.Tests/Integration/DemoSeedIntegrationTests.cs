using System.Text.Json;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Demo;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Dishhive.Api.Tests.Integration;

public sealed class DemoSeedIntegrationTests : TestBase
{
    [Fact]
    public async Task EmbeddedBackup_IsCompleteAndMatchesTheSelectedSourcesAndThemes()
    {
        using var archive = DemoDataSeeder.OpenSeedArchive();
        using var stream = await RecipeBackupArchive.ReadDocumentAsync(archive);
        using var json = await JsonDocument.ParseAsync(stream);
        var nodes = json.RootElement.GetProperty("@graph").EnumerateArray().ToList();
        Assert.Equal(DemoData.RecipeUrls.Count, nodes.Count);
        Assert.Equal(DemoData.RecipeUrls.Order(), nodes.Select(n => n.GetProperty("url").GetString()!).Order());
        Assert.Equal(nodes.Count, nodes.Select(n => n.GetProperty("name").GetString()!.Trim().ToLowerInvariant()).Distinct().Count());
        Assert.All(nodes, node =>
        {
            Assert.NotEmpty(node.GetProperty("recipeIngredient").EnumerateArray());
            Assert.NotEmpty(node.GetProperty("recipeInstructions").EnumerateArray());
            Assert.StartsWith("data:image/", node.GetProperty("image").GetString());
            var imageBytes = Convert.FromBase64String(node.GetProperty("image").GetString()!.Split(',', 2)[1]);
            using var imageData = SKData.CreateCopy(imageBytes);
            using var codec = SKCodec.Create(imageData);
            Assert.NotNull(codec);
            Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
            Assert.InRange(Math.Max(codec.Info.Width, codec.Info.Height), 1, DemoSeedImages.MaxDimension);
            Assert.Equal("aiDetected", node.GetProperty("dishhive:dietaryFacts").GetProperty("status").GetString());
        });
        var collections = nodes.SelectMany(n => n.GetProperty("dishhive:collections").EnumerateArray()
            .Select(c => c.GetString())).ToList();
        foreach (var name in new[] { "Belgian classics", "Asian inspired", "Desserts & baking", "Vegetarian meals", "Plant based cooking" })
            Assert.True(collections.Count(c => c == name) >= 20, $"Expected at least 20 recipes in {name}");
    }

    [Fact]
    public async Task DemoSeeding_RestoresEnrichedRecipesAndHouseholdOffline_AndRunsOnlyOnce()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Demo:Enabled"] = "true" }).Build();
        async Task SeedAsync()
        {
            using var seeder = new DemoDataSeeder(Factory.Services.GetRequiredService<IServiceScopeFactory>(),
                config, NullLogger<DemoDataSeeder>.Instance, Factory.Services.GetRequiredService<IHouseholdClock>());
            await seeder.StartAsync(default);
            await seeder.ExecuteTask!;
        }
        await SeedAsync();
        using var fresh = CreateFreshContext();
        Assert.Equal(DemoData.RecipeUrls.Count, await fresh.Recipes.CountAsync());
        Assert.Equal(4, await fresh.FamilyMembers.CountAsync());
        Assert.Equal(14, await fresh.PlannedMeals.CountAsync());
        Assert.True(await fresh.UserSettings.AnyAsync(s => s.Key == UserSettingKeys.DemoDataSeeded));
        Assert.All(await fresh.Recipes.ToListAsync(), recipe =>
        {
            Assert.Equal(DietaryFactsStatus.AiDetected, recipe.DietaryFactsStatus);
            Assert.Equal(recipe.Revision, recipe.DietaryFactsAssessedRevision);
            Assert.NotEmpty(recipe.ImageData!);
        });
        Assert.Empty(await fresh.RecipeEnrichmentJobs.ToListAsync());
        await SeedAsync();
        Assert.Equal(DemoData.RecipeUrls.Count, await fresh.Recipes.CountAsync());
        Assert.Equal(4, await fresh.FamilyMembers.CountAsync());
    }
}
