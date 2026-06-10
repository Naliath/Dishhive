namespace Dishhive.Api.Services.Freezy;

/// <summary>
/// Dishhive's read model of a frozen item in Freezy. Mapped from Freezy's wire DTOs in the
/// anti-corruption layer (<see cref="FreezyHttpClient"/>); Freezy types never leak past it.
/// Not persisted in Dishhive's database.
/// </summary>
public record FrozenItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Quantity { get; init; }
    public string? Unit { get; init; }
    public DateTime? ExpirationDate { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// The single integration boundary with Freezy (see docs/features/freezy-integration.md).
/// Direction is Dishhive → Freezy only, over Freezy's public REST API.
/// The integration is optional: when unconfigured or unreachable, callers receive
/// empty results and the planner degrades gracefully.
/// </summary>
public interface IFreezyClient
{
    /// <summary>Whether a Freezy base URL is configured</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Frozen items available for meal planning, ordered soonest-expiring first.
    /// Returns an empty list when Freezy is unconfigured or unreachable.
    /// </summary>
    Task<IReadOnlyList<FrozenItem>> GetFrozenItemsAsync(CancellationToken cancellationToken = default);
}
