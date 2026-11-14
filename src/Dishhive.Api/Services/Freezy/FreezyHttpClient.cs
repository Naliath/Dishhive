using System.Text.Json;

namespace Dishhive.Api.Services.Freezy;

/// <summary>
/// HTTP implementation of <see cref="IFreezyClient"/> against Freezy's REST API.
/// Acts as an anti-corruption layer: Freezy's wire format is deserialized into a private
/// DTO here and mapped to Dishhive's <see cref="FrozenItem"/> read model.
/// </summary>
public class FreezyHttpClient : IFreezyClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<FreezyHttpClient> _logger;

    public FreezyHttpClient(HttpClient httpClient, ILogger<FreezyHttpClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public bool IsConfigured => _httpClient.BaseAddress != null;

    public string? BaseUrl => _httpClient.BaseAddress?.ToString().TrimEnd('/');

    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return false;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await _httpClient.GetAsync("api/items", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<FrozenItem>> GetFrozenItemsAsync(CancellationToken cancellationToken = default)
        => (await ReadStockAsync(false, cancellationToken)).Items;

    public Task<FreezyStockSnapshot> GetStockSnapshotAsync(CancellationToken cancellationToken = default)
        => ReadStockAsync(true, cancellationToken);

    private async Task<FreezyStockSnapshot> ReadStockAsync(bool authoritative, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return new(false, false, DateTime.UtcNow, []);
        }

        try
        {
            using var response = await _httpClient.GetAsync("api/items", cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var items = await JsonSerializer.DeserializeAsync<List<FreezyItemDto>>(stream, JsonOptions, cancellationToken);

            if (authoritative && (items == null || items.Any(item => string.IsNullOrWhiteSpace(item.Id)
                || string.IsNullOrWhiteSpace(item.Name) || item.Quantity is null or < 0)
                || items.Select(item => item.Id).Distinct().Count() != items.Count))
                throw new JsonException("Freezy returned an incomplete stock snapshot");
            var mapped = (items ?? [])
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .Select(i => new FrozenItem
                {
                    Id = i.Id ?? string.Empty,
                    Name = i.Name!,
                    Quantity = i.Quantity ?? 1,
                    Unit = i.Unit,
                    ExpirationDate = i.ExpirationDate,
                    Notes = i.Notes
                })
                .OrderBy(i => i.ExpirationDate ?? DateTime.MaxValue)
                .ToList();
            return new(true, true, DateTime.UtcNow, mapped);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Freezy being down must never break Dishhive planning
            _logger.LogWarning(ex, "Could not reach Freezy at {BaseUrl}; returning no frozen items", _httpClient.BaseAddress);
            return new(true, false, DateTime.UtcNow, []);
        }
    }

    /// <summary>Tolerant mirror of Freezy's FreezerItemDto wire format (all fields optional)</summary>
    private sealed record FreezyItemDto
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public int? Quantity { get; init; }
        public string? Unit { get; init; }
        public DateTime? ExpirationDate { get; init; }
        public string? Notes { get; init; }
    }
}
