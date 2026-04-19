using Dishhive.Api.Models.DTOs;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Dishhive.Api.Services;

public class FreezyIntegrationOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public bool Enabled { get; set; } = true;
}

public class FreezyIntegrationService : IFreezyIntegrationService
{
    private readonly HttpClient _httpClient;
    private readonly FreezyIntegrationOptions _options;
    private readonly ILogger<FreezyIntegrationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public bool IsEnabled => _options.Enabled;

    public FreezyIntegrationService(
        HttpClient httpClient,
        IOptions<FreezyIntegrationOptions> options,
        ILogger<FreezyIntegrationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (_options.Enabled)
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }
    }

    public async Task<IEnumerable<FrozenItemDto>> GetFrozenItemsAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return [];

        try
        {
            var response = await _httpClient.GetAsync("api/items", cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var items = JsonSerializer.Deserialize<List<FreezyItemResponse>>(content, JsonOptions);

            return items?.Select(MapToDto) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch frozen items from Freezy at {BaseUrl}", _options.BaseUrl);
            return [];
        }
    }

    public async Task<FrozenItemDto?> GetFrozenItemByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return null;

        try
        {
            var response = await _httpClient.GetAsync($"api/items/{id}", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var item = JsonSerializer.Deserialize<FreezyItemResponse>(content, JsonOptions);

            return item == null ? null : MapToDto(item);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch frozen item {Id} from Freezy", id);
            return null;
        }
    }

    private static FrozenItemDto MapToDto(FreezyItemResponse item) =>
        new(item.Id, item.Name, item.Quantity, item.Unit ?? "pieces", item.ExpirationDate);

    // Mirrors Freezy's item response shape — only the fields Dishhive needs
    private record FreezyItemResponse(
        Guid Id,
        string Name,
        int Quantity,
        string? Unit,
        DateTime? ExpirationDate
    );
}
