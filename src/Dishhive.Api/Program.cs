using Dishhive.Api.Data;
using Dishhive.Api.Extensions;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.ShoppingList;
using Dishhive.Api.Services.Suggestions;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

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

// Recipe import: pluggable source providers + import service with its own HttpClient
builder.Services.AddSingleton<IRecipeSourceProvider, DagelijkseKostProvider>();
builder.Services.AddHttpClient<IRecipeImportService, RecipeImportService>((serviceProvider, client) =>
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

// Extension point for future AI-assisted planning (no-op by design, see week-planner.md)
builder.Services.AddSingleton<IMealSuggestionService, NoOpMealSuggestionService>();

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
