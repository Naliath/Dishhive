using Dishhive.Api.Data;
using Dishhive.Api.Services;
using Dishhive.Api.Services.Sources;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

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
    builder.Services.AddHealthChecks();
}

// HTTP Client for recipe import (Dagelijkse Kost scraping)
builder.Services.AddHttpClient<IRecipeSourceProvider, DagelijksekostSourceProvider>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Dishhive/1.0 (household meal planner)");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Freezy integration
builder.Services.Configure<FreezyIntegrationOptions>(
    builder.Configuration.GetSection("FreezyIntegration"));
builder.Services.AddHttpClient<IFreezyIntegrationService, FreezyIntegrationService>();

// Image downloader — separate HttpClient with a longer timeout for image downloads
builder.Services.AddHttpClient<ImageDownloadService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Recipe import service
builder.Services.AddScoped<IRecipeImportService, RecipeImportService>();

// Week plan suggestion (stub — replace with AI provider later)
builder.Services.AddSingleton<IWeekPlanSuggestionProvider, StubWeekPlanSuggestionProvider>();

// Measurement conversion
builder.Services.AddSingleton<IMeasurementConversionService, MeasurementConversionService>();

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// OpenAPI / Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Dishhive API",
        Version = Dishhive.Api.AppVersion.Version,
        Description = "API for household meal planning — Dishhive"
    });
});

// CORS for development
builder.Services.AddCors(options =>
{
    options.AddPolicy("Development", policy =>
    {
        policy.WithOrigins("http://localhost:4201")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// =============================================================================
// Middleware Pipeline
// =============================================================================

// Swagger (always enabled for self-hosted app)
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Dishhive API v1");
    options.RoutePrefix = "swagger";
});

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
                     && !context.Request.Path.StartsWithSegments("/swagger")
                     && !context.Request.Path.StartsWithSegments("/health"), appBuilder =>
{
    appBuilder.UseDefaultFiles();
    appBuilder.UseStaticFiles();
});

// Fallback to index.html for Angular routing (SPA) - exclude API/swagger routes
app.MapFallback(context =>
{
    if (context.Request.Path.StartsWithSegments("/api") ||
        context.Request.Path.StartsWithSegments("/swagger") ||
        context.Request.Path.StartsWithSegments("/health"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    context.Response.ContentType = "text/html";
    return context.Response.SendFileAsync(
        Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html"));
});

// Apply EF migrations on startup (non-test environments only)
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
    db.Database.Migrate();
}

// Seed database with sample data (controlled by Seeding:Enabled config / SEEDING__ENABLED env var)
var seedingEnabled = app.Configuration.GetValue<bool>("Seeding:Enabled", defaultValue: true);
if (seedingEnabled)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
    await DatabaseSeeder.SeedAsync(db);
}

app.Run();

// Make Program accessible for integration tests
public partial class Program { }
