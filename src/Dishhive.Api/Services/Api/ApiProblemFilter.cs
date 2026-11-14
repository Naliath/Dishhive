using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Dishhive.Api.Services.Api;

public sealed class ApiProblemFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: ProblemDetails problem } result)
        {
            problem.Status ??= result.StatusCode ?? 500;
            if (problem is ValidationProblemDetails) problem.Extensions["code"] = "validation_failed";
            else problem.Extensions.TryAdd("code", $"http_{problem.Status}");
            problem.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
        }
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}
