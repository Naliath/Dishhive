using Dishhive.Api.Services.Localization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Dishhive.Api.Services.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken token)
    {
        if (context.RequestAborted.IsCancellationRequested) return false;
        var failure = exception switch
        {
            ApiProblemException known => known,
            DbUpdateConcurrencyException => new(409, "concurrent_edit", "errors.conflictTitle", "errors.conflictDetail"),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } }
                => new(409, "duplicate", "errors.conflictTitle", "errors.duplicateDetail"),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } }
                => new(409, "reference_changed", "errors.conflictTitle", "errors.referenceDetail"),
            HttpRequestException => new(503, "dependency_unavailable", "errors.unavailableTitle", "errors.unavailableDetail"),
            TimeoutException => new(503, "dependency_unavailable", "errors.unavailableTitle", "errors.unavailableDetail"),
            NpgsqlException { IsTransient: true } => new(503, "dependency_unavailable", "errors.unavailableTitle", "errors.unavailableDetail"),
            DbUpdateException { InnerException: NpgsqlException { IsTransient: true } }
                => new(503, "dependency_unavailable", "errors.unavailableTitle", "errors.unavailableDetail"),
            _ => new ApiProblemException(500, "unexpected_error", "errors.unexpectedTitle", "errors.unexpectedDetail")
        };
        if (failure.Status >= 500) logger.LogError(exception, "Request failed with {Code}", failure.Code);
        else logger.LogInformation("Request rejected with {Code}", failure.Code);
        // Localization uses the database. An unavailable database must still produce a safe response.
        string title = failure.Status == 409 ? "The saved data changed" : "The request could not be completed";
        string? detail = failure.Detail;
        try
        {
            var messages = context.RequestServices.GetRequiredService<UserMessageLocalizer>();
            title = await messages.GetAsync(failure.TitleKey, token, failure.Parameters);
            if (failure.DetailKey != null) detail = await messages.GetAsync(failure.DetailKey, token, failure.Parameters);
        }
        catch (Exception localizationError) when (localizationError is not OperationCanceledException)
        {
            logger.LogWarning("Could not localize error response: {Type}", localizationError.GetType().Name);
        }
        context.Response.StatusCode = failure.Status;
        var problem = new ProblemDetails { Status = failure.Status, Title = title, Detail = detail };
        problem.Extensions["code"] = failure.Code;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new()
            { HttpContext = context, ProblemDetails = problem });
        return true;
    }
}
