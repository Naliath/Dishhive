namespace Dishhive.Api.Services.Localization;

/// <summary>A display message identified structurally, without inspecting English prose.</summary>
public sealed record LocalizedUserText(string Key, params (string Name, object? Value)[] Parameters);

public sealed record LocalizedUserValue(IFormattable Value, string Format);
