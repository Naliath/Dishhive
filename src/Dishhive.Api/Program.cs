using Dishhive.Api;
using Dishhive.Api.Data;
using Dishhive.Api.Extensions;
using Dishhive.Api.Services.Agents;
using Dishhive.Api.Services.Agents.Planning;
using Dishhive.Api.Services.Agents.RecipeImport;
using Dishhive.Api.Services.FreezyIntegration;
using Dishhive.Api.Services.History;
using Dishhive.Api.Services.Planning;
using Dishhive.Api.Services.RecipeImport;
using Dishhive.Api.Services.RecipeImport.Providers;
using Dishhive.Api.Services.Shopping;
using Dishhive.Api.Services.Units;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// Database
// =============================================================================
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<DishhiveDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    builder.Services.AddHealthChecks()
        .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!);
}
else
{
    // In Testing, TestWebApplicationFactory injects an InMemory DbContext.
    builder.Services.AddHealthChecks();
}

// =============================================================================
// Recipe import (pluggable providers)
// =============================================================================
var importUserAgent = builder.Configuration.GetValue<string>("Dishhive:RecipeImport:UserAgent")
    ?? $"Dishhive/{AppVersion.Version} (+local recipe import)";
var importTimeout = builder.Configuration.GetValue<int?>("Dishhive:RecipeImport:RequestTimeoutSeconds") ?? 15;

builder.Services.AddHttpClient<DagelijkseKostRecipeProvider>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", importUserAgent);
    client.Timeout = TimeSpan.FromSeconds(importTimeout);
});
// Register every provider both as itself (to keep the typed HttpClient binding) AND as IRecipeSourceProvider.
builder.Services.AddTransient<IRecipeSourceProvider>(sp => sp.GetRequiredService<DagelijkseKostRecipeProvider>());

// Learned-source provider (LLM-taught, then static): registered LAST so static providers win.
builder.Services.AddHttpClient<LearnedRecipeSourceProvider>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", importUserAgent);
    client.Timeout = TimeSpan.FromSeconds(importTimeout);
});
builder.Services.AddTransient<IRecipeSourceProvider>(sp => sp.GetRequiredService<LearnedRecipeSourceProvider>());

builder.Services.AddSingleton<IRecipeSourceRegistry, RecipeSourceRegistry>();
builder.Services.AddScoped<IRecipeImportService, RecipeImportService>();
builder.Services.AddScoped<ILearnedSourceStore, LearnedSourceStore>();

// =============================================================================
// Freezy integration
// =============================================================================
var freezyBaseUrl = builder.Configuration.GetValue<string>("Dishhive:Freezy:BaseUrl") ?? "http://localhost:5000";
builder.Services.AddHttpClient<IFreezyClient, FreezyHttpClient>(client =>
{
    client.BaseAddress = new Uri(freezyBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("User-Agent", $"Dishhive/{AppVersion.Version}");
});

// =============================================================================
// Other services
// =============================================================================
builder.Services.AddSingleton<IUnitConversionService, UnitConversionService>();
builder.Services.AddScoped<IHistoryMaterializationService, HistoryMaterializationService>();
builder.Services.AddScoped<IShoppingListGenerationService, ShoppingListGenerationService>();

// =============================================================================
// AI agents (Microsoft Agent Framework integration). Disabled by default —
// see docs/features/ai-agents.md.
// =============================================================================
builder.Services.Configure<AiAgentOptions>(builder.Configuration.GetSection(AiAgentOptions.SectionName));
builder.Services.AddSingleton<IChatClientFactory, ChatClientFactory>();

builder.Services.AddHttpClient<IRecipeImportAgent, RecipeImportAgent>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", importUserAgent);
    client.Timeout = TimeSpan.FromSeconds(importTimeout);
});

builder.Services.AddScoped<MealPlanningTools>();
builder.Services.AddScoped<IMealPlanningAgent, MealPlanningAgent>();

// AgentMealSuggestionStrategy plugs the agent into the existing IMealSuggestionStrategy seam;
// returns null when AI is disabled, so the original no-op behavior is preserved.
builder.Services.AddScoped<IMealSuggestionStrategy, AgentMealSuggestionStrategy>();

// =============================================================================
// MVC + OpenAPI
// =============================================================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Dishhive API",
        Version = AppVersion.Version,
        Description = "Family week-menu planning API"
    });
});

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
// Pipeline
// =============================================================================
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Dishhive API v1");
    options.RoutePrefix = "swagger";
});

if (app.Environment.IsDevelopment())
{
    app.UseCors("Development");
}

app.MapHealthChecks("/health");
app.MapControllers();

app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api")
                     && !context.Request.Path.StartsWithSegments("/swagger")
                     && !context.Request.Path.StartsWithSegments("/health"), appBuilder =>
{
    appBuilder.UseDefaultFiles();
    appBuilder.UseStaticFiles();
});

app.MapFallback(context =>
{
    if (context.Request.Path.StartsWithSegments("/api") ||
        context.Request.Path.StartsWithSegments("/swagger") ||
        context.Request.Path.StartsWithSegments("/health"))
    {
        context.Response.StatusCode = 404;
        return Task.CompletedTask;
    }

    var indexPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
    if (!File.Exists(indexPath))
    {
        // SPA may not be present in dev API-only runs; return a small placeholder.
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync("Dishhive API is running. SPA assets are not bundled in this build. See /swagger.");
    }

    context.Request.Path = "/index.html";
    return context.Response.SendFileAsync(indexPath);
});

if (!app.Environment.IsEnvironment("Testing"))
{
    await app.MigrateDatabaseAsync();
}

app.Run();

public partial class Program { }
