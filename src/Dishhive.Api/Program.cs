using Dishhive.Api.Services.Time;
using Dishhive.Api.Data;
using Dishhive.Api.Extensions;
using Dishhive.Api.Mcp;
using Dishhive.Api.Services.Demo;
using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Localization;
using Dishhive.Api.Services.ShoppingList;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Services.Security;
using Dishhive.Api.Services.WebSearch;
using Dishhive.Api.Services.Api;
using Dishhive.Api.Services.Recipes;
using Microsoft.AspNetCore.RateLimiting;
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
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.WebHost.ConfigureKestrel(server =>
    server.Limits.MaxRequestBodySize = 12 * 1024 * 1024);

// =============================================================================
// Services Configuration
// =============================================================================

// Validate the household zone at startup rather than silently following the host OS.
var householdZoneId = builder.Configuration["Household:TimeZone"] ?? "Europe/Brussels";
var householdZone = TimeZoneInfo.FindSystemTimeZoneById(householdZoneId);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(householdZone);
builder.Services.AddSingleton<IHouseholdClock, HouseholdClock>();

// Database Context - Skip PostgreSQL setup for test environment
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<DishhiveDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
            // Several queries load two collection navigations at once (e.g. PlannedMeal's
            // Attendees + Ratings, Recipe's Ingredients + Steps) — split-query avoids the
            // cartesian-product row multiplication of the single-query default (and the
            // MultipleCollectionIncludeWarning EF logs for every such query shape).
            npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

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
builder.Services.AddSingleton<IPublicUrlValidator, PublicUrlValidator>();
builder.Services.AddHttpClient<ISafeHttpFetcher, SafeHttpFetcher>(client =>
{
    var userAgent = builder.Configuration["RecipeImport:UserAgent"] ?? "Dishhive/1.0";
    client.DefaultRequestHeaders.Add("User-Agent", userAgent);
    client.Timeout = TimeSpan.FromSeconds(15);
})
    .ConfigurePrimaryHttpMessageHandler(PublicHttpMessageHandler.Create);
builder.Services.AddScoped<IRecipeImportService, RecipeImportService>();

// Recipe library exchange: schema.org JSON export + file import (image downloads reuse
// the same outbound HTTP configuration as URL import)
builder.Services.AddScoped<IRecipeExchangeService, RecipeExchangeService>();
builder.Services.AddScoped<RecipeMutationService>();
builder.Services.AddScoped<RecipeReviewTelemetry>();
builder.Services.AddScoped<AiReviewMetricsService>();

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

// Freezer stock available for planning = Freezy stock minus future reservations
// (see FreezerAvailabilityService); used by the planner panel and the AI suggestions.
builder.Services.AddScoped<FreezerAvailabilityService>();
builder.Services.AddScoped<FreezerReservationService>();

// Web search for the AI planner's external-recipe tools (see docs/features/ai-week-planning.md).
// Optional (disabled while WebSearch__Provider/BaseUrl are empty): a NoOp client stays registered
// so the suggestion service simply omits the search tool. Config is always registered so
// IntegrationsController can report its state.
var webSearchOptions = builder.Configuration.GetSection(WebSearchOptions.SectionName).Get<WebSearchOptions>()
    ?? new WebSearchOptions();
builder.Services.AddSingleton(webSearchOptions);
builder.Services.AddScoped<ISitemapRecipeSearch, SitemapRecipeSearch>();
if (webSearchOptions.IsConfigured)
{
    builder.Services.AddHttpClient<IWebSearchClient, SearxngWebSearchClient>(client =>
    {
        client.BaseAddress = new Uri(webSearchOptions.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(20);
    });
}
else
{
    builder.Services.AddSingleton<IWebSearchClient, NoOpWebSearchClient>();
}

// Shared AI boundary (see docs/features/ai-week-planning.md): every model-backed
// capability executes through IAiExecutionGate; no-op feature services are used when
// AI is unconfigured. Testing keeps those no-ops so integration tests stay deterministic.
var aiOptions = builder.Configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
aiOptions.Validate();
// Always register so IntegrationsController can report config state regardless of environment
builder.Services.AddSingleton(aiOptions);
if (!builder.Environment.IsEnvironment("Testing") && aiOptions.IsConfigured)
{
    builder.Services.AddSingleton(_ => ChatClientFactory.Create(aiOptions));
    builder.Services.AddSingleton<IAiTaskMetricsStore, AiTaskMetricsStore>();
    builder.Services.AddSingleton<IAiExecutionGate, AiExecutionGate>();
    // Suggestion services are scoped: deterministic external-recipe discovery closes
    // over scoped/typed-HttpClient dependencies. The client and gate stay singleton.
    builder.Services.AddScoped<RulesMealSuggestionService>();
    builder.Services.AddScoped<IMealSuggestionService, LlmMealSuggestionService>();
    // LLM recipe extraction fallback for import (used when structured scrapers fail).
    builder.Services.AddSingleton<ILlmRecipeExtractor, LlmRecipeExtractor>();
    // Dietary-facts classification of recipes (contains milk/gluten/…), same posture
    builder.Services.AddSingleton<IRecipeFactsExtractor, LlmRecipeFactsExtractor>();
    builder.Services.AddSingleton<IRecipeResearchQueryRefiner, RecipeResearchQueryRefiner>();
    builder.Services.AddSingleton<IRecipeDishMatcher, RecipeDishMatcher>();
    builder.Services.AddSingleton<IRecipeLocalizationService, LlmRecipeLocalizationService>();
    // Model capability test: runs once at startup (AiModelStartupTest) and gates every
    // AI suggestion call on its verdict; re-triggerable from the settings page.
    builder.Services.AddSingleton<AiModelTester>();
    builder.Services.AddSingleton<IAiModelCapabilityService, AiModelCapabilityService>();
    if (builder.Configuration.GetValue<bool>("BackgroundJobs:Enabled"))
        builder.Services.AddHostedService<AiModelStartupTest>();
}
else
{
    builder.Services.AddSingleton<IAiTaskMetricsStore, NoOpAiTaskMetricsStore>();
    builder.Services.AddSingleton<IMealSuggestionService, NoOpMealSuggestionService>();
    builder.Services.AddSingleton<ILlmRecipeExtractor, NoOpLlmRecipeExtractor>();
    builder.Services.AddSingleton<IRecipeFactsExtractor, NoOpRecipeFactsExtractor>();
    builder.Services.AddSingleton<IRecipeResearchQueryRefiner, NoOpRecipeResearchQueryRefiner>();
    builder.Services.AddSingleton<IRecipeDishMatcher, NoOpRecipeDishMatcher>();
    builder.Services.AddSingleton<IRecipeLocalizationService, NoOpRecipeLocalizationService>();
    builder.Services.AddSingleton<IAiModelCapabilityService, NoOpAiModelCapabilityService>();
}
// Background dietary-facts assessment queue. The singleton is always registered
// (controllers and the import service enqueue into it; with AI unconfigured the
// enqueue is a visible no-op), the worker loop only outside Testing.
builder.Services.AddSingleton<RecipeFactsAssessmentService>();
if (!builder.Environment.IsEnvironment("Testing")
    && builder.Configuration.GetValue<bool>("BackgroundJobs:Enabled"))
{
    builder.Services.AddHostedService(sp => sp.GetRequiredService<RecipeFactsAssessmentService>());
    builder.Services.AddHostedService<AiMetricsMaintenanceService>();
}
// Editable AI system prompt (settings-backed); registered regardless of AI state so
// the settings endpoints work, and the suggestion pipeline reads the effective prompt
builder.Services.AddScoped<AiPromptService>();
builder.Services.AddScoped<IAiPromptProvider>(sp => sp.GetRequiredService<AiPromptService>());
builder.Services.AddScoped<CollectionMentionResolver>();
builder.Services.AddScoped<SourceMentionResolver>();
builder.Services.AddScoped<RecipeSourceCatalog>();
builder.Services.AddScoped<MealSuggestionRequestBuilder>();
builder.Services.AddScoped<MealSuggestionAcceptanceService>();
builder.Services.AddScoped<AttendancePolicy>();
builder.Services.AddScoped<IExternalRecipeSessionFactory, ExternalRecipeSessionFactory>();
builder.Services.AddScoped<MealSuggestionPostProcessor>();
builder.Services.AddScoped<AiPlanningMetricsStore>();

// Demo mode: seed Dagelijkse Kost recipes and a demo household into an empty
// database when Demo:Enabled is set (see docs/features/demo-mode.md)
if (!builder.Environment.IsEnvironment("Testing")
    && builder.Configuration.GetValue<bool>("BackgroundJobs:Enabled"))
{
    builder.Services.AddHostedService<DemoDataSeeder>();
}

// Shopping list generation (computed on demand, nothing persisted)
builder.Services.AddScoped<IShoppingListService, ShoppingListService>();

// Computed read-only collections (Top rated, Quick, member favorites, …)
builder.Services.AddScoped<Dishhive.Api.Services.Collections.AutoCollectionProvider>();

// Controllers
builder.Services.AddSingleton<SupportedLanguageCatalog>();
builder.Services.AddScoped<UserMessageLocalizer>();
builder.Services.AddControllers(options => options.Filters.Add<ApiProblemFilter>());
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
    context.ProblemDetails.Extensions.TryAdd("code", $"http_{context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode}");
});
var securityOptions = builder.Configuration.GetSection(ApiSecurityOptions.SectionName)
    .Get<ApiSecurityOptions>() ?? new ApiSecurityOptions();
securityOptions.Validate();
builder.Services.AddSingleton(securityOptions);
builder.Services.AddRateLimiter(limits =>
{
    limits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limits.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == "expensive"
            ? System.Threading.RateLimiting.RateLimitPartition.GetConcurrencyLimiter("expensive", _ => new()
            { PermitLimit = 2, QueueLimit = 0 })
            : System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("ordinary"));
    limits.AddFixedWindowLimiter("expensive", policy =>
    {
        policy.PermitLimit = 10;
        policy.Window = TimeSpan.FromMinutes(1);
        policy.QueueLimit = 0;
    });
});

// Native OpenAPI
builder.Services.AddOpenApi(options =>
{
    // NSwag 14's upload support expects OpenAPI 3.0 binary schemas.
    options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
    options.AddSchemaTransformer((schema, context, cancellationToken) =>
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (type.IsEnum && schema.Type == JsonSchemaType.Integer)
        {
            schema.Enum = Enum.GetValues(type).Cast<object>()
                .Select(value => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(Convert.ToInt32(value))!).ToList();
            schema.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            schema.Extensions["x-enumNames"] = new JsonNodeExtension(
                new System.Text.Json.Nodes.JsonArray(Enum.GetNames(type).Select(name => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(name)!).ToArray()));
        }
        if (schema.Properties != null && context.JsonTypeInfo.Kind == System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object)
        {
            if (context.JsonTypeInfo.Type.Namespace == "Dishhive.Api.Models.DTOs")
                schema.AdditionalPropertiesAllowed = false;
            schema.Required ??= new HashSet<string>();
            foreach (var property in context.JsonTypeInfo.Properties)
                if (!property.IsGetNullable && schema.Properties.ContainsKey(property.Name))
                    schema.Required.Add(property.Name);
        }
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, cancellationToken) =>
    {
        if (context.Description.ActionDescriptor is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor action)
            operation.OperationId = $"{action.ControllerName}_{action.ActionName}";
        // Inline upload schemas: NSwag 14 does not emit FileParameter for referenced
        // IFormFile schemas, even though it uses that type in the generated methods.
        if (operation.RequestBody?.Content?.TryGetValue("multipart/form-data", out var multipart) == true
            && multipart.Schema?.Properties != null)
            foreach (var parameter in context.Description.ParameterDescriptions.Where(parameter => parameter.Type == typeof(IFormFile)))
                multipart.Schema.Properties[parameter.Name] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" };
        return Task.CompletedTask;
    });
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

        // Attribute controller tokens retain their CLR casing in ApiExplorer. Publish
        // the same lowercase API paths used by the application and browser fixtures.
        var paths = new OpenApiPaths();
        foreach (var (path, item) in document.Paths)
        {
            var segments = path.Split('/');
            if (segments.Length > 2 && segments[1] == "api") segments[2] = segments[2].ToLowerInvariant();
            paths.Add(string.Join('/', segments), item);
        }
        document.Paths = paths;

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
              .AllowAnyMethod()
              .WithExposedHeaders("X-Total-Count");
    });
});

// MCP server: read-only planning/recipe tools for local AI clients on /mcp
// (see docs/features/mcp-server.md). Stateless streamable HTTP — the tools are
// simple request/response reads, so no session tracking is needed and clients
// can reconnect freely. Remote deployments require a separate read credential.
builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "Dishhive", Version = AppVersion.Version };
    })
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<DishhiveMcpTools>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();

// =============================================================================
// Middleware Pipeline
// =============================================================================

// OpenAPI document + Scalar UI
app.MapOpenApi();
app.MapScalarApiReference();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseRateLimiter();

// CORS
if (app.Environment.IsDevelopment())
{
    app.UseCors("Development");
}

// Health check endpoint
app.MapHealthChecks("/health");

// API Controllers - map before static files
app.MapControllers();

// MCP endpoint (streamable HTTP) for local AI clients
app.MapMcp("/mcp");

// Serve static files (Angular app) - only for non-API routes
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api")
                     && !context.Request.Path.StartsWithSegments("/openapi")
                     && !context.Request.Path.StartsWithSegments("/scalar")
                     && !context.Request.Path.StartsWithSegments("/mcp")
                     && !context.Request.Path.StartsWithSegments("/health"), appBuilder =>
{
    appBuilder.UseDefaultFiles();
    appBuilder.UseStaticFiles();
});

// Fallback to index.html for Angular routing (SPA) - exclude API/OpenAPI routes
app.MapFallback(context =>
{
    // Don't fallback for API, OpenAPI, Scalar, MCP, or health routes
    if (context.Request.Path.StartsWithSegments("/api") ||
        context.Request.Path.StartsWithSegments("/openapi") ||
        context.Request.Path.StartsWithSegments("/scalar") ||
        context.Request.Path.StartsWithSegments("/mcp") ||
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
if (!app.Environment.IsEnvironment("Testing")
    && app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await app.MigrateDatabaseAsync();
}

app.Run();

// Make Program accessible to test projects
public partial class Program { }
