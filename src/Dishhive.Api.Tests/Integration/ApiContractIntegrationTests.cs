using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Dishhive.Api.Tests.Integration;

public class ApiContractIntegrationTests : TestBase
{
    [Fact]
    public async Task PagedRecipes_ExposeCountAndClampBounds_WithoutChangingLegacyList()
    {
        for (var i = 0; i < 3; i++)
            (await Client.PostAsJsonAsync("/api/recipes", new CreateRecipeDto { Title = $"Recipe {i}" })).EnsureSuccessStatusCode();
        var page = await Client.GetFromJsonAsync<RecipePageDto>("/api/recipes/page?page=2&pageSize=2");
        Assert.Equal(3, page!.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Single(page.Items);
        var legacy = await Client.GetAsync("/api/recipes?page=2&pageSize=2");
        Assert.Equal("3", Assert.Single(legacy.Headers.GetValues("X-Total-Count")));
        Assert.Equal(page.Items[0].Id, Assert.Single((await legacy.Content.ReadFromJsonAsync<List<RecipeListItemDto>>())!).Id);
        var clamped = await Client.GetFromJsonAsync<RecipePageDto>("/api/recipes/page?page=-1&pageSize=900");
        Assert.Equal(1, clamped!.Page);
        Assert.Equal(500, clamped.PageSize);
    }

    [Theory]
    [InlineData("/api/recipes/00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/does-not-exist")]
    public async Task MissingResource_HasProblemStatusCodeAndTrace(string path)
    {
        var response = await Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblem(response, 404, "http_404");
    }

    [Fact]
    public async Task Validation_PreservesFieldErrors()
    {
        var response = await Client.PostAsJsonAsync("/api/recipes", new { title = "", servings = 0 });
        var json = await AssertProblem(response, 400, "validation_failed");
        Assert.True(json.GetProperty("errors").TryGetProperty("Title", out _));
    }

    [Theory]
    [InlineData("concurrency", 409, "concurrent_edit")]
    [InlineData("duplicate", 409, "duplicate")]
    [InlineData("reference", 409, "reference_changed")]
    [InlineData("http", 503, "dependency_unavailable")]
    [InlineData("unexpected", 500, "unexpected_error")]
    public async Task SaveFailures_HaveSafeConsistentProblemResponses(string failure, int status, string code)
    {
        await using var factory = new TestWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DishhiveDbContext>();
                services.RemoveAll<DbContextOptions<DishhiveDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<DishhiveDbContext>>();
                services.AddDbContext<DishhiveDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .AddInterceptors(new FailingSave(failure)));
            }));
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/recipes", new { title = "Draft" });
        await AssertProblem(response, status, code);
        Assert.DoesNotContain("private-database-detail", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenApi_OperationsAreUnique_AndUploadsAreInlineBinary()
    {
        var spec = await Client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var ids = new List<string>();
        foreach (var path in spec.GetProperty("paths").EnumerateObject())
        foreach (var operation in path.Value.EnumerateObject().Where(property => property.Value.TryGetProperty("operationId", out _)))
            ids.Add(operation.Value.GetProperty("operationId").GetString()!);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Contains('_', id));
        var file = spec.GetProperty("paths").GetProperty("/api/recipes/{id}/image").GetProperty("put")
            .GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data")
            .GetProperty("schema").GetProperty("properties").GetProperty("file");
        Assert.Equal("binary", file.GetProperty("format").GetString());
        var required = spec.GetProperty("components").GetProperty("schemas").GetProperty("RecipeDto")
            .GetProperty("required").EnumerateArray().Select(value => value.GetString());
        Assert.Contains("ingredients", required);
        Assert.Contains("dietaryFacts", required);
    }

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
        return body;
    }

    private sealed class FailingSave(string failure) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw failure switch
        {
            "concurrency" => new DbUpdateConcurrencyException("private-database-detail"),
            "duplicate" => new DbUpdateException("private-database-detail", new PostgresException("private-database-detail", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation)),
            "reference" => new DbUpdateException("private-database-detail", new PostgresException("private-database-detail", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation)),
            "http" => new HttpRequestException("private-database-detail"),
            _ => new InvalidOperationException("private-database-detail")
        };
    }
}
