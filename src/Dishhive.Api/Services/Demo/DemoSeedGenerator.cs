using Dishhive.Api.Services.Import;
using System.Text.Json;

namespace Dishhive.Api.Services.Demo;

/// <summary>
/// One-time seed data generator. Run via: dotnet run --project src/Dishhive.Api -- --generate-demo-seed
///
/// Fetches all 20 Dagelijkse Kost recipe pages, extracts structured data (using the same
/// provider as the regular import pipeline), downloads images, and writes the result to
/// Services/Demo/demo-seed.json as an embedded resource so demo seeding requires no
/// network access at runtime.
///
/// Re-run this whenever the recipe list in DemoData.RecipeUrls changes.
/// </summary>
internal static class DemoSeedGenerator
{
    // Computed at runtime: AppContext.BaseDirectory is bin/Debug/net10.0/ during dotnet run;
    // three levels up is the project directory, giving us a stable path regardless of CWD.
    private static string OutputPath =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Services", "Demo", "demo-seed.json");

    internal static async Task RunAsync()
    {
        var outputPath = Path.GetFullPath(OutputPath);
        Console.WriteLine($"Generating demo seed → {outputPath}");
        Console.WriteLine();

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Dishhive/1.0 (demo-seed-generator)");
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var provider = new DagelijkseKostProvider();
        var records = new List<SeedRecipeRecord>();

        foreach (var url in DemoData.RecipeUrls)
        {
            var slug = new Uri(url).Segments.Last().TrimEnd('/');
            Console.Write($"  {slug}... ");
            try
            {
                var html = await httpClient.GetStringAsync(url);
                var imported = await provider.ExtractAsync(html, new Uri(url));

                var record = new SeedRecipeRecord
                {
                    Title = imported.Title,
                    Description = imported.Description,
                    Servings = imported.Servings ?? 4,
                    PrepTimeMinutes = imported.PrepTimeMinutes,
                    CookTimeMinutes = imported.CookTimeMinutes,
                    TotalTimeMinutes = imported.TotalTimeMinutes,
                    Category = imported.Category,
                    Keywords = imported.Keywords,
                    VideoUrl = imported.VideoUrl,
                    SourceUrl = imported.SourceUrl ?? url,
                    SourceProvider = provider.Key,
                    SourceRawData = imported.RawData,
                    Ingredients = imported.IngredientLines.Select((line, i) =>
                    {
                        var parsed = IngredientLineParser.Parse(line);
                        return new SeedIngredientRecord
                        {
                            SortOrder = i,
                            Name = parsed.Name,
                            Quantity = parsed.Quantity,
                            Unit = parsed.Unit,
                            OriginalText = parsed.OriginalText,
                            OriginalQuantity = parsed.OriginalQuantity,
                            OriginalUnit = parsed.OriginalUnit
                        };
                    }).ToList(),
                    Steps = imported.Steps.Select((s, i) => new SeedStepRecord
                    {
                        StepNumber = i + 1,
                        Instruction = s
                    }).ToList()
                };

                await TryDownloadImageAsync(httpClient, imported.ImageUrl, record);

                records.Add(record);
                var imageTag = record.ImageDataBase64 != null ? "img" : "no-img";
                Console.WriteLine($"OK [{imageTag}] \"{imported.Title}\"");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"SKIP – {ex.GetType().Name}: {ex.Message}");
                Console.ResetColor();
            }
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(records, options);
        await File.WriteAllTextAsync(outputPath, json);

        Console.WriteLine();
        Console.WriteLine($"Wrote {records.Count}/{DemoData.RecipeUrls.Count} recipes to {outputPath}");
    }

    private static async Task TryDownloadImageAsync(
        HttpClient httpClient, string? imageUrl, SeedRecipeRecord record)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) ||
            !Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri))
        {
            return;
        }

        try
        {
            using var response = await httpClient.GetAsync(
                imageUri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
            {
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length > 0 && bytes.Length <= 5 * 1024 * 1024)
            {
                record.ImageContentType = contentType;
                record.ImageDataBase64 = Convert.ToBase64String(bytes);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.Write($"(image failed: {ex.Message}) ");
        }
    }
}
