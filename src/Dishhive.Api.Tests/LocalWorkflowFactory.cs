using Dishhive.Api.Data;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Tests.Mocks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dishhive.Api.Tests;

/// <summary>Real application endpoints with controlled external boundaries only.</summary>
internal sealed class LocalWorkflowFactory(string? connectionString = null, string? webRoot = null)
    : TestWebApplicationFactory
{
    public const string ImportUrl = "https://dagelijksekost.vrt.be/gerechten/cremeux-citroen-bodem-witte-chocolade-gepofte-rijst-rode-bessen";
    public FixtureSuggestionService Suggestions { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        if (webRoot is not null) builder.UseWebRoot(webRoot);
        builder.ConfigureTestServices(services =>
        {
            if (connectionString is not null)
            {
                services.RemoveAll<DishhiveDbContext>();
                services.RemoveAll<DbContextOptions<DishhiveDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<DishhiveDbContext>>();
                services.AddDbContext<DishhiveDbContext>(options => options.UseNpgsql(connectionString));
            }
            services.RemoveAll<IMealSuggestionService>();
            services.AddSingleton<IMealSuggestionService>(Suggestions);
            var freezer = Substitute.For<IFreezyClient>();
            freezer.GetFrozenItemsAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<FrozenItem>>([]));
            services.RemoveAll<IFreezyClient>();
            services.AddSingleton(freezer);

            var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "dagelijkse-kost-recipe.html"));
            var fetcher = new MockHttpMessageHandler().RespondWith(ImportUrl, html)
                .RespondWith("https://storage.googleapis.com/", Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="), "image/png");
            services.RemoveAll<ISafeHttpFetcher>();
            services.AddSingleton<ISafeHttpFetcher>(new SafeHttpFetcher(new HttpClient(fetcher), new AllowAllPublicUrlValidator()));
        });
    }

    /// <summary>A deterministic model-boundary response, normalized by the production post-processor.</summary>
    internal sealed class FixtureSuggestionService : IMealSuggestionService
    {
        public bool IsEnabled => true;
        public MealSuggestionRequest? LastRequest { get; private set; }

        public Task<IReadOnlyList<MealSuggestion>> SuggestAsync(MealSuggestionRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var recipe = request.KnownRecipes.FirstOrDefault();
            var proposals = request.DaysToFill.Select(date => new MealSuggestion
            {
                Date = date, RecipeId = recipe?.Id, DishName = recipe?.Title ?? "Attendance test dinner",
                Reason = "Controlled local workflow response"
            }).ToList();
            return Task.FromResult(new MealSuggestionPostProcessor(NullLogger<MealSuggestionPostProcessor>.Instance)
                .Finalize(proposals, request));
        }
    }
}
