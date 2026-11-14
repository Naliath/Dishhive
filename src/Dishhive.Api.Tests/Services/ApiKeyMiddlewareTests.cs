using Dishhive.Api.Services.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace Dishhive.Api.Tests.Services;

public class ApiKeyMiddlewareTests
{
    [Fact]
    public async Task ProtectedApi_RejectsMissingKey()
    {
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, new ApiSecurityOptions { ApiKey = "expected-secret" });
        var context = Context("/api/recipes");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.ContentType.Should().StartWith("application/problem+json");
        context.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(context.Response.Body);
        problem.RootElement.GetProperty("title").GetString().Should().Be("Authentication required");
        nextCalled.Should().BeFalse();
    }

    [Theory]
    [InlineData("X-Dishhive-Api-Key", "expected-secret")]
    [InlineData("Authorization", "Bearer expected-secret")]
    public async Task ProtectedApi_AcceptsSupportedCredential(string header, string value)
    {
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, new ApiSecurityOptions { ApiKey = "expected-secret" });
        var context = Context("/api/recipes");
        context.Request.Headers[header] = value;

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("/mcp", "read-secret", true)]
    [InlineData("/mcp", "write-secret", false)]
    [InlineData("/api/recipes", "read-secret", false)]
    [InlineData("/api/recipes", "write-secret", true)]
    public async Task ReadCredential_CannotAuthorizeRestMutations(string path, string key, bool expected)
    {
        var called = false;
        var middleware = new ApiKeyMiddleware(_ => { called = true; return Task.CompletedTask; },
            new ApiSecurityOptions { ApiKey = "write-secret", McpReadApiKey = "read-secret" });
        var context = Context(path);
        context.Request.Method = "POST";
        context.Request.Headers.Authorization = $"Bearer {key}";
        await middleware.InvokeAsync(context);
        called.Should().Be(expected);
    }

    [Fact]
    public void RemoteMode_RequiresDistinctCredentials()
    {
        var options = new ApiSecurityOptions { RemoteMode = true, ApiKey = "same", McpReadApiKey = "same" };
        options.Invoking(value => value.Validate()).Should().Throw<InvalidOperationException>();
        options.McpReadApiKey = "";
        options.Invoking(value => value.Validate()).Should().Throw<InvalidOperationException>();
        options.McpReadApiKey = "read";
        options.Invoking(value => value.Validate()).Should().NotThrow();
    }

    [Fact]
    public async Task HealthEndpoint_RemainsAvailableForContainerProbes()
    {
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, new ApiSecurityOptions { ApiKey = "expected-secret" });

        await middleware.InvokeAsync(Context("/health"));

        nextCalled.Should().BeTrue();
    }

    private static DefaultHttpContext Context(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
