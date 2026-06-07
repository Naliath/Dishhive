# Feature: Freezy Integration

## Goal
Surface frozen leftovers / frozen meals from Freezy inside Dishhive's planner without duplicating Freezy's inventory.

## Scope
- Read-only fetch of Freezy items (mainly for items flagged as "meal" / leftover).
- Reference selection inside a meal slot ("planned: Lasagna from Freezy").
- Clear, swappable boundary so that the integration can later evolve (push-back to Freezy, shared schema, message bus, etc.).

## Out of scope (v1)
- Marking items as consumed in Freezy when the meal happens (planned but deferred — see Risks).
- Two-way sync.
- Auth between apps (both run locally without auth).

## User stories
- *In the planner, choose "From Freezy" and pick "Lasagna (added Oct 12)"*.
- *Disable Freezy integration entirely from settings if Freezy isn't running*.

## Architecture
```
        Dishhive.Api
            │
            ▼
   IFreezyClient  ◄── tests substitute a stub
        ▲
        │ default impl
        │
   FreezyHttpClient (typed HttpClient)
        │
        ▼
   GET http://localhost:5000/api/items
```

`IFreezyClient` exposes a Dishhive-shaped contract:
```csharp
public interface IFreezyClient
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FrozenItemReference>> GetFrozenItemsAsync(CancellationToken ct = default);
}

public record FrozenItemReference(
    string FreezyItemId,
    string Name,
    int Quantity,
    string Unit,
    DateTime? ExpirationDate,
    string? LabelIcon);
```

The Freezy response shape never leaks past `FreezyHttpClient`. If Freezy is unreachable, `IsAvailableAsync` returns `false` and `GetFrozenItemsAsync` returns an empty list — the UI shows "Freezy not connected".

## Configuration
- `Dishhive:Freezy:Enabled` (bool, default `true`)
- `Dishhive:Freezy:BaseUrl` (default `http://localhost:5000`)

## Backend requirements
- Service: `Services/FreezyIntegration/IFreezyClient.cs`, `FreezyHttpClient.cs`, `FrozenItemReference.cs`.
- Controller: `FreezyIntegrationController` exposing `GET /api/freezy/items`, `GET /api/freezy/status`.
- Registered as a typed `HttpClient` so timeouts/policies are centralized.

## Frontend requirements
- Service: `freezy-integration.service.ts`.
- Used by the week-planner slot dialog "From Freezy" tab.
- Settings page can toggle the integration flag (writes to `UserSetting`s for runtime override of `Dishhive:Freezy:Enabled`).

## Risks / unknowns
- **Push-back to Freezy** ("mark as consumed") would require modifying Freezy. Out of scope for v1; recorded as a future enhancement.
- Freezy's `FreezerItem` shape may change — adapter contains the coupling.
- No auth means anyone on `localhost` reaches both apps; acceptable for self-hosted single-household v1.

## Phased plan
1. `IFreezyClient` + HTTP impl + tests with stubbed handler.
2. `FreezyIntegrationController`.
3. Angular service.
4. "From Freezy" tab in planner slot dialog.
5. Settings toggle.

## Implementation checklist
- [x] `IFreezyClient` + `FrozenItemReference`
- [x] `FreezyHttpClient` typed client
- [ ] Unit test with `MockHttpMessageHandler`
- [x] `FreezyIntegrationController`
- [ ] Angular `FreezyIntegrationService`
- [x] Settings toggle
- [ ] Planner integration
