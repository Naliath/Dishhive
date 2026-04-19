# Feature: Freezy Integration

**Created:** April 19, 2026  
**Status:** ✅ Implemented

---

## Feature Goal

Allow Dishhive to include frozen leftovers and frozen meals from Freezy (FreezerInventory) in week meal planning. The integration must be clean, isolated, and evolvable without tight coupling between the two applications.

---

## Integration Boundary

```
┌──────────────────────────────────┐       HTTP API        ┌──────────────────────────────┐
│           Dishhive               │ ─────────────────────▶ │           Freezy             │
│                                  │                        │  (FreezerInventory.Api)      │
│  IFreezyIntegrationService       │  GET /api/items        │  ItemsController             │
│  FreezyIntegrationService        │◀─────────────────────  │  → returns FreezerItem list  │
│                                  │                        │                              │
└──────────────────────────────────┘                        └──────────────────────────────┘
```

**Rules:**
- Dishhive **never** connects directly to Freezy's PostgreSQL database.
- All communication goes through Freezy's HTTP API.
- The integration can be **disabled** via configuration (`FreezyIntegration:Enabled = false`).
- When disabled, the "from freezer" option in the week planner is hidden.

---

## Scope

### In Scope
- Querying Freezy's frozen item list from within Dishhive
- Selecting a frozen item for a planned meal
- Displaying frozen item name and quantity in the week planner
- Integration configuration (base URL, enable/disable)

### Out of Scope (for this phase)
- Writing back to Freezy (no consumption marking from Dishhive)
- Real-time sync or webhooks
- Dishhive modifying Freezy items

---

## User Stories

1. **As a meal planner**, I want to see what's available in the freezer when planning meals.
2. **As a meal planner**, I want to assign a frozen item from Freezy to a day's meal.
3. **As a household manager**, I want to disable the Freezy integration if Freezy is not running.

---

## Domain Model Considerations

`PlannedMeal` already has:
- `IsFromFreezer: bool`
- `FreezerItemId: Guid?` — external reference (not a DB foreign key)

No additional Dishhive entities are needed for Phase 1.

A `FrozenItemDto` in Dishhive mirrors the relevant fields from Freezy's API response:

```csharp
public record FrozenItemDto(
    Guid Id,
    string Name,
    int Quantity,
    string Unit,
    DateTime? ExpirationDate
);
```

---

## Backend Requirements

### Integration Service Interface

```csharp
public interface IFreezyIntegrationService
{
    bool IsEnabled { get; }
    Task<IEnumerable<FrozenItemDto>> GetFrozenItemsAsync();
    Task<FrozenItemDto?> GetFrozenItemByIdAsync(Guid id);
}
```

### Configuration

`appsettings.json`:
```json
{
  "FreezyIntegration": {
    "BaseUrl": "http://localhost:5000",
    "Enabled": true
  }
}
```

### API Endpoint

- `GET /api/freezer/items` — proxies to Freezy, returns `FrozenItemDto[]`
  - Returns `[]` when integration is disabled

---

## Frontend Requirements

- "From freezer" toggle in the meal assignment dialog (week planner)
- Frozen item picker (dropdown or search list) populated from `/api/freezer/items`
- Display frozen item name on day card when a meal uses a frozen item
- Graceful empty state when Freezy is not available

---

## Risks / Unknowns

- **Freezy API schema changes**: If Freezy changes its item response structure, the `FreezyIntegrationService` needs to be updated. This is isolated to one file.
- **Network unavailability**: When Freezy is not running, the frozen item list should return empty gracefully (don't error out the week planner).
- **Authentication**: Freezy currently has no authentication. If auth is added to Freezy in the future, the integration service will need to pass credentials.

---

## Freezy API Endpoint Used

| Dishhive Operation | Freezy Endpoint |
|--------------------|----------------|
| List frozen items  | `GET /api/items` |
| Get single item    | `GET /api/items/{id}` |

These are standard Freezy endpoints and should remain stable.

---

## Phased Implementation Plan

### Phase 1 — Read Integration
- [ ] `IFreezyIntegrationService` interface
- [ ] `FreezyIntegrationService` implementation
- [ ] Configuration binding (`FreezyIntegrationOptions`)
- [ ] `GET /api/freezer/items` endpoint in Dishhive
- [ ] Integration tests (mock Freezy HTTP responses)

### Phase 2 — Week Planner UI Integration
- [ ] "From freezer" option in meal dialog
- [ ] Frozen item picker component
- [ ] Display frozen item on day card

---

## Implementation Checklist

### Backend
- [x] Feature plan created
- [x] Integration boundary documented
- [x] `IFreezyIntegrationService` interface created
- [x] `FreezyIntegrationService` implementation created (graceful fallback when disabled/unreachable)
- [x] `FreezerController` proxy endpoints created (`/items`, `/items/{id}`, `/status`)
- [x] Integration tests (3 graceful-degradation tests)

### Frontend
- [x] "From freezer" option in meal plan dialog
- [x] Frozen item displayed in day card when planned
- [ ] Standalone frozen item picker component (future)
