namespace Dishhive.Api.Services.Api;

/// <summary>A reviewable application failure, translated at the HTTP boundary.</summary>
public sealed class ApiProblemException(
    int status, string code, string titleKey, string? detailKey = null,
    string? detail = null, params (string Name, object? Value)[] parameters) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string TitleKey { get; } = titleKey;
    public string? DetailKey { get; } = detailKey;
    public string? Detail { get; } = detail;
    public (string Name, object? Value)[] Parameters { get; } = parameters;
}
