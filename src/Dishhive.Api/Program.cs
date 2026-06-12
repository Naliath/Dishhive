using Dishhive.Api.Data;
using Dishhive.Api.Extensions;
using Dishhive.Api.Services.Demo;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.ShoppingList;
using Dishhive.Api.Services.Suggestions;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

// one-time seed generation: dotnet run --project src/Dishhive.Api -- --generate-demo-seed
if (args.Contains("--generate-demo-seed"))
{
    await Dishhive.Api.Services.Demo.DemoSeedGenerator.RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// Services Configuration
// =============================================================================

// Database Context - Skip PostgreSQL setup for test environment
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<DishhiveDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    // Health Checks - Include database check only in non-test environments
    builder.Services.AddHealthChecks()
        .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!);
}
else
{
    // In testing, DbContext will be configured by TestWebApplicationFactory
    // Add basic health checks without database dependency
    builder.Services.AddHealthChecks();
}

// Recipe import: pluggable source providers + import service with its own HttpClient.
// Dedicated providers are registered first; the recipe-scrapers sidecar fallback is
// registered last so it only handles sites without a dedicated implementation
// (RecipeImportService picks the first provider whose CanHandle matches).
builder.Services.AddSingleton<IRecipeSourceProvider, DagelijkseKostProvider>();
builder.Services.AddHttpClient<IRecipeScrapersClient, RecipeScrapersClient>(client =>
{
    var scraperBaseUrl = builder.Configuration["RecipeScrapers:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(scraperBaseUrl))
    {
        client.BaseAddress = new Uri(scraperBaseUrl.TrimEnd('/') + "/");
    }
    // Long ceiling for package updates (pip install); individual calls use shorter
    // per-call timeouts inside RecipeScrapersClient
    client.Timeout = TimeSpan.FromMinutes(6);
});
builder.Services.AddTransient<IRecipeSourceProvider, RecipeScrapersFallbackProvider>();
builder.Services.AddHttpClient<IRecipeImportService, RecipeImportService>((serviceProvider, client) =>
{
    var userAgent = builder.Configuration["RecipeImport:UserAgent"] ?? "Dishhive/1.0";
    client.DefaultRequestHeaders.Add("User-Agent", userAgent);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Recipe library exchange: schema.org JSON export + file import (image downloads reuse
// the same outbound HTTP configuration as URL import)
builder.Services.AddHttpClient<IRecipeExchangeService, RecipeExchangeService>((serviceProvider, client) =>
{
    var userAgent = builder.Configuration["RecipeImport:UserAgent"] ?? "Dishhive/1.0";
    client.DefaultRequestHeaders.Add("User-Agent", userAgent);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Freezy integration (optional; disabled when Freezy:BaseUrl is empty)
builder.Services.AddHttpClient<IFreezyClient, FreezyHttpClient>(client =>
{
    var freezyBaseUrl = builder.Configuration["Freezy:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(freezyBaseUrl))
    {
        client.BaseAddress = new Uri(freezyBaseUrl.TrimEnd('/') + "/");
    }
    client.Timeout = TimeSpan.FromSeconds(2);
});

// AI week-plan suggestions (see docs/features/ai-week-planning.md): LLM-backed with
// a deterministic rules fallback when Ai:Provider is configured, no-op otherwise.
// Testing always gets the no-op so integration tests stay deterministic.
var aiOptions = builder.Configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
// Always register so IntegrationsController can report config state regardless of environment
builder.Services.AddSingleton(aiOptions);
if (!builder.Environment.IsEnvironment("Testing") && aiOptions.IsConfigured)
{
    builder.Services.AddSingleton(_ => ChatClientFactory.Create(aiOptions));
    builder.Services.AddSingleton<RulesMealSuggestionService>();
    builder.Services.AddSingleton<IMealSuggestionService, LlmMealSuggestionService>();
}
else
{
    builder.Services.AddSingleton<IMealSuggestionService, NoOpMealSuggestionService>();
}
builder.Services.AddScoped<MealSuggestionRequestBuilder>();

// Demo mode: seed Dagelijkse Kost recipes and a demo household into an empty
// database when Demo:Enabled is set (see docs/features/demo-mode.md)
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<DemoDataSeeder>();
}

// Shopping list generation (computed on demand, nothing persisted)
builder.Services.AddScoped<IShoppingListService, ShoppingListService>();

// Controllers
builder.Services.AddControllers();

// Native OpenAPI
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new()
        {
            Title = "Dishhive API",
            Version = AppVersion.Version,
            Description = "API for family week menu planning with recipes, Freezy leftovers and shopping lists"
        };

        document.Info.License = new()
        {
            Name = "MIT",
            Url = new Uri("https://opensource.org/licenses/MIT")
        };

        document.Servers ??= new List<OpenApiServer>();
        document.Servers.Clear();
        document.Servers.Add(new OpenApiServer
        {
            Url = "/",
            Description = "Relative server root"
        });

        return Task.CompletedTask;
    });
});

// CORS for development (Angular dev server on port 4300)
builder.Services.AddCors(options =>
{
    options.AddPolicy("Development", policy =>
    {
        policy.WithOrigins("http://localhost:4300")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// =============================================================================
// Middleware Pipeline
// =============================================================================

// OpenAPI document + Scalar UI
app.MapOpenApi();
app.MapScalarApiReference();

// CORS
if (app.Environment.IsDevelopment())
{
    app.UseCors("Development");
}

// Health check endpoint
app.MapHealthChecks("/health");

// API Controllers - map before static files
app.MapControllers();

// Serve static files (Angular app) - only for non-API routes
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api")
                     && !context.Request.Path.StartsWithSegments("/openapi")
                     && !context.Request.Path.StartsWithSegments("/scalar")
                     && !context.Request.Path.StartsWithSegments("/health"), appBuilder =>
{
    appBuilder.UseDefaultFiles();
    appBuilder.UseStaticFiles();
});

// Fallback to index.html for Angular routing (SPA) - exclude API/OpenAPI routes
app.MapFallback(context =>
{
    // Don't fallback for API, OpenAPI, Scalar, or health routes
    if (context.Request.Path.StartsWithSegments("/api") ||
        context.Request.Path.StartsWithSegments("/openapi") ||
        context.Request.Path.StartsWithSegments("/scalar") ||
        context.Request.Path.StartsWithSegments("/health"))
    {
        context.Response.StatusCode = 404;
        return Task.CompletedTask;
    }

    context.Request.Path = "/index.html";
    return context.Response.SendFileAsync(
        Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html"));
});

// =============================================================================
// Database Migration (auto-apply in production, skip in testing)
// =============================================================================
if (!app.Environment.IsEnvironment("Testing"))
{
    await app.MigrateDatabaseAsync();
}

app.Run();

// Make Program accessible to test projects
public partial class Program { }
