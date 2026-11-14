using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Dishhive.Api.Services.Security;

public sealed class ApiSecurityOptions
{
    public const string SectionName = "ApiSecurity";
    public string ApiKey { get; set; } = string.Empty;
    public string McpReadApiKey { get; set; } = string.Empty;
    public bool RemoteMode { get; set; }
    public bool IsEnabled => !string.IsNullOrWhiteSpace(ApiKey);

    public void Validate()
    {
        if (RemoteMode && (!IsEnabled || string.IsNullOrWhiteSpace(McpReadApiKey)))
            throw new InvalidOperationException("Remote mode requires separate REST and MCP read API keys");
        if (!string.IsNullOrWhiteSpace(McpReadApiKey) && (!IsEnabled || ApiKey.Trim() == McpReadApiKey.Trim()))
            throw new InvalidOperationException("The MCP read credential must differ from the configured REST API key");
    }
}

public sealed class ApiKeyMiddleware(RequestDelegate next, ApiSecurityOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!options.IsEnabled || !RequiresProtection(context.Request.Path))
        {
            await next(context);
            return;
        }

        var supplied = context.Request.Headers["X-Dishhive-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(supplied)
            && context.Request.Headers.Authorization.FirstOrDefault() is { } authorization
            && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            supplied = authorization[7..].Trim();

        var mcp = context.Request.Path.StartsWithSegments("/mcp");
        var requiredKey = mcp ? options.McpReadApiKey : options.ApiKey;
        if (string.IsNullOrWhiteSpace(requiredKey) || !FixedTimeEquals(supplied, requiredKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Authentication required",
                Detail = "A valid Dishhive API key is required"
            }, options: null, contentType: "application/problem+json",
                cancellationToken: context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static bool RequiresProtection(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/mcp");

    private static bool FixedTimeEquals(string? supplied, string configured)
    {
        if (supplied is null) return false;
        var left = Encoding.UTF8.GetBytes(supplied);
        var right = Encoding.UTF8.GetBytes(configured.Trim());
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
