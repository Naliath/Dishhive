using System.Diagnostics;
using System.Text.Json;
using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Services.Import;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Demo;

/// <summary>Builds an offline demo backup through the real URL importer and enrichment queue.</summary>
internal static class DemoSeedGenerator
{
    internal static async Task RunAsync(IServiceProvider services, IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var manifestPath = configuration["demo-manifest"]
            ?? Path.Combine("Services", "Demo", "demo-recipes.json");
        var outputPath = Path.GetFullPath(configuration["demo-output"]
            ?? Path.Combine("Services", "Demo", "demo-seed.zip"));
        var selections = JsonSerializer.Deserialize<DemoRecipeSelection[]>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The demo selection is empty.");
        if (selections.Length < 200 || selections.Select(s => new Uri(s.Url).Host).Distinct().Count() != 5
            || selections.Select(s => s.Url).Distinct().Count() != selections.Length)
            throw new InvalidDataException("Select at least 200 distinct recipe URLs from exactly five sources.");
        if (!services.GetRequiredService<IRecipeFactsExtractor>().IsAvailable)
            throw new InvalidOperationException("Demo generation requires the application's configured AI facts provider.");

        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            if (!db.Database.GetDbConnection().Database.StartsWith("dishhive_demo_seed_", StringComparison.Ordinal))
                throw new InvalidOperationException("Generation requires a dedicated dishhive_demo_seed_* database.");
            if (await db.FamilyMembers.AnyAsync(cancellationToken))
                throw new InvalidOperationException("The generation database must not contain household data.");
        }

        var failures = new List<string>();
        for (var index = 0; index < selections.Length; index++)
        {
            var selection = selections[index];
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            if (await db.Recipes.AnyAsync(r => r.SourceUrl == selection.Url
                && r.ImageData != null && r.Ingredients.Any() && r.Steps.Any(), cancellationToken))
            {
                Console.WriteLine($"[{index + 1}/{selections.Length}] Already imported: {selection.Url}");
                continue;
            }
            try
            {
                var recipe = await scope.ServiceProvider.GetRequiredService<IRecipeImportService>()
                    .ImportAsync(selection.Url, cancellationToken);
                if (recipe.Ingredients.Count == 0 || recipe.Steps.Count == 0)
                    throw new InvalidDataException("The extracted recipe has no ingredients or instructions.");
                selections[index] = selection with { Url = recipe.SourceUrl! };
                Console.WriteLine($"[{index + 1}/{selections.Length}] Imported: {recipe.Title}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(selection.Url);
                Console.WriteLine($"IMPORT FAILED: {selection.Url}: {ex.Message}");
            }
            await Task.Delay(250, cancellationToken);
        }
        if (failures.Count > 0)
            throw new InvalidOperationException($"{failures.Count} recipes failed import. Fix the selection and resume using the retained generation database.");

        var worker = services.GetRequiredService<RecipeFactsAssessmentService>();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            var retryIds = await db.Recipes.Where(r =>
                (r.DietaryFactsStatus == DietaryFactsStatus.Failed
                    || r.DietaryFactsStatus == DietaryFactsStatus.Stale
                    || r.DietaryFactsStatus == DietaryFactsStatus.Unassessed)
                && !db.RecipeEnrichmentJobs.Any(j => j.RecipeId == r.Id
                    && (j.State == RecipeEnrichmentJobState.Pending || j.State == RecipeEnrichmentJobState.Running)))
                .Select(r => r.Id).ToListAsync(cancellationToken);
            await worker.EnqueueManyAsync(retryIds, cancellationToken);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromHours(2));
        var elapsed = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            while (!deadline.IsCancellationRequested)
            {
                if (await worker.ProcessNextAsync(deadline.Token)) continue;
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
                if (!await db.RecipeEnrichmentJobs.AnyAsync(j => j.State == RecipeEnrichmentJobState.Pending
                    || j.State == RecipeEnrichmentJobState.Running, deadline.Token)) break;
                await Task.Delay(500, deadline.Token);
            }
            deadline.Token.ThrowIfCancellationRequested();
        }));

        await using var exportScope = services.CreateAsyncScope();
        var context = exportScope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var recipes = await context.Recipes.Include(r => r.Ingredients).Include(r => r.Steps)
            .Include(r => r.DietaryFacts).ToListAsync(cancellationToken);
        var selectionUrls = selections.Select(s => s.Url).ToHashSet();
        if (selectionUrls.Count != selections.Length
            || recipes.Select(r => r.Title.Trim().ToLowerInvariant()).Distinct().Count() != recipes.Count
            || recipes.Count != selections.Length || recipes.Any(r => !selectionUrls.Contains(r.SourceUrl!)
            || r.DietaryFactsStatus != DietaryFactsStatus.AiDetected
            || r.DietaryFactsAssessedRevision != r.Revision || r.Ingredients.Count == 0 || r.Steps.Count == 0
            || r.ImageData is not { Length: > 0 }))
            throw new InvalidOperationException("Every selected recipe must have complete ingredients, instructions, a local image, and current AI facts before export.");

        foreach (var group in selections.GroupBy(s => s.Collection))
        {
            if (await context.Cookbooks.AnyAsync(c => c.Name == group.Key, cancellationToken)) continue;
            var urls = group.Select(s => s.Url).ToHashSet();
            var cookbook = new Cookbook { Name = group.Key };
            foreach (var recipe in recipes.Where(r => urls.Contains(r.SourceUrl!)))
                cookbook.Entries.Add(new CookbookEntry { Cookbook = cookbook, Recipe = recipe });
            context.Cookbooks.Add(cookbook);
        }
        await context.SaveChangesAsync(cancellationToken);
        var json = await exportScope.ServiceProvider.GetRequiredService<IRecipeExchangeService>()
            .ExportAsync(cancellationToken);
        json = await DemoSeedImages.OptimizeAsync(json, cancellationToken);
        var archive = RecipeBackupArchive.Create(json);
        await File.WriteAllTextAsync(manifestPath,
            JsonSerializer.Serialize(selections, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + "\n",
            cancellationToken);
        var temporaryPath = outputPath + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, archive, cancellationToken);
        File.Move(temporaryPath, outputPath, overwrite: true);
        var report = new
        {
            generatedAt = DateTime.UtcNow,
            recipes = recipes.Count,
            sources = recipes.GroupBy(r => new Uri(r.SourceUrl!).Host).ToDictionary(g => g.Key, g => g.Count()),
            collections = selections.GroupBy(s => s.Collection).ToDictionary(g => g.Key, g => g.Count()),
            assessed = recipes.Count(r => r.DietaryFactsStatus == DietaryFactsStatus.AiDetected),
            images = recipes.Count(r => r.ImageData is { Length: > 0 }),
            imageProfile = new { maxDimension = DemoSeedImages.MaxDimension, webpQuality = DemoSeedImages.WebpQuality },
            enrichmentSecondsThisRun = (int)elapsed.Elapsed.TotalSeconds,
            jsonBytes = System.Text.Encoding.UTF8.GetByteCount(json),
            zipBytes = archive.Length,
            aiRuns = await context.AiTaskRuns.GroupBy(r => new { r.Task, r.Outcome, r.Provider, r.Model, r.ContractVersion })
                .Select(g => new { g.Key.Task, g.Key.Outcome, g.Key.Provider, g.Key.Model, g.Key.ContractVersion,
                    count = g.Count(), inputTokens = g.Sum(r => r.InputTokens), outputTokens = g.Sum(r => r.OutputTokens) })
                .ToListAsync(cancellationToken)
        };
        await File.WriteAllTextAsync(Path.ChangeExtension(outputPath, ".report.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), cancellationToken);
        Console.WriteLine($"Exported {recipes.Count} enriched recipes to {outputPath} ({archive.Length:N0} bytes).");
    }
}

public sealed record DemoRecipeSelection(string Url, string Collection);
