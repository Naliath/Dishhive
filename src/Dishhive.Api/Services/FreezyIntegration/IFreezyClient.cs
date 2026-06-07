using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Dishhive.Api.Services.FreezyIntegration;

/// <summary>Lightweight Dishhive-shaped reference to an item that lives in Freezy.</summary>
public sealed record FrozenItemReference(
    string FreezyItemId,
    string Name,
    int Quantity,
    string Unit,
    DateTime? ExpirationDate,
    string? LabelIcon);

public interface IFreezyClient
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FrozenItemReference>> GetFrozenItemsAsync(CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="IFreezyClient"/> implementation. Uses a typed <see cref="HttpClient"/>
/// against Freezy's public API. Returns an empty list when Freezy is unreachable so the UI
/// degrades gracefully.
/// </summary>
public sealed class FreezyHttpClient : IFreezyClient
{
    private readonly HttpClient _http;
    private readonly ILogger<FreezyHttpClient> _logger;

    public FreezyHttpClient(HttpClient http, ILogger<FreezyHttpClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync("/health", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Freezy availability check failed");
            return false;
        }
    }

    public async Task<IReadOnlyList<FrozenItemReference>> GetFrozenItemsAsync(CancellationToken ct = default)
    {
        try
        {
            var items = await _http.GetFromJsonAsync<List<FreezyItemDto>>("/api/items", ct)
                ?? new List<FreezyItemDto>();

            return items.Select(i => new FrozenItemReference(
                FreezyItemId: i.Id.ToString(),
                Name: i.Name ?? "(unnamed)",
                Quantity: i.Quantity,
                Unit: i.Unit ?? "pieces",
                ExpirationDate: i.ExpirationDate,
                LabelIcon: i.LabelIcon)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Freezy items; returning empty list");
            return Array.Empty<FrozenItemReference>();
        }
    }

    /// <summary>Internal DTO mirroring Freezy's <c>FreezerItem</c> response shape.</summary>
    private sealed class FreezyItemDto
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("quantity")] public int Quantity { get; set; } = 1;
        [JsonPropertyName("unit")] public string? Unit { get; set; }
        [JsonPropertyName("expirationDate")] public DateTime? ExpirationDate { get; set; }
        [JsonPropertyName("labelIcon")] public string? LabelIcon { get; set; }
    }
}
