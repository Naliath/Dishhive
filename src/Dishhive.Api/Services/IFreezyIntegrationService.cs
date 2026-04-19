using Dishhive.Api.Models.DTOs;

namespace Dishhive.Api.Services;

/// <summary>
/// Reads frozen items from Freezy (FreezerInventory) via its HTTP API.
/// </summary>
public interface IFreezyIntegrationService
{
    bool IsEnabled { get; }
    Task<IEnumerable<FrozenItemDto>> GetFrozenItemsAsync(CancellationToken cancellationToken = default);
    Task<FrozenItemDto?> GetFrozenItemByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
