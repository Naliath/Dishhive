# Feature: Freezy Integration

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [week-planner.md](week-planner.md), [shopping-list-export.md](shopping-list-export.md)

## Feature Goal

Let the week planner reuse frozen leftovers and frozen meals tracked in Freezy
(`C:\Source\FreezerInventory`), through a clean integration boundary that can evolve without
coupling the two applications.

## Scope

**In scope**
- Read-only consumption of Freezy's existing REST API (`GET /api/items`, `GET /api/items/expiring/{days}`)
- "From the freezer" suggestions in the planner slot editor (prioritizing soon-to-expire items)
- Recording the chosen Freezy item reference on a planned meal (`FreezyItemRef`)
- Graceful degradation: Freezy unconfigured or down ⇒ planner works, freezer panel hidden

**Out of scope**
- Modifying Freezy in any way
- Writing back to Freezy (e.g. auto-consuming an item when the meal is planned/eaten) — future,
  would use Freezy's existing endpoints, still Dishhive→Freezy direction
- Shared database or shared code packages

## The Integration Boundary (architecture decision)

```
┌──────────────── Dishhive.Api ────────────────┐         ┌──── Freezy ────┐
│ Planner / Suggestions                        │         │                │
│        │ uses                                │  HTTP   │  /api/items    │
│  IFreezyClient  ──► FreezyHttpClient ────────┼────────►│  /api/items/   │
│        │                 │                   │         │   expiring/{d} │
│  FrozenItem (Dishhive read model)            │         └────────────────┘
│        ▲ mapped from Freezy DTO (ACL)        │
└──────────────────────────────────────────────┘
```

Rules of the boundary:

1. **Single entry point:** all Freezy access goes through `IFreezyClient`. No other class
   issues HTTP calls to Freezy.
2. **Anti-corruption layer:** Freezy's wire DTOs are mapped to Dishhive's own `FrozenItem`
   read model (`Id`, `Name`, `Quantity`, `Unit`, `ExpirationDate`, `Notes`). Freezy types never
   leak into controllers, entities, or the frontend.
3. **Loose reference:** planned meals store only an opaque `FreezyItemRef` string. If the Freezy
   item disappears, the planned meal remains valid (the denormalized `DishName` still describes it).
4. **Optional dependency:** `Freezy:BaseUrl` config empty ⇒ `IFreezyClient.IsConfigured == false`
   ⇒ API returns an empty/disabled result; UI hides the freezer panel. Timeouts are short (2 s)
   and failures are logged, never thrown to the user.
5. **Versioning strategy:** Freezy's OpenAPI document is the contract. If Freezy's DTOs change,
   only `FreezyHttpClient` mapping changes.

Deployment note: locally Freezy runs on `http://localhost:5000`. From inside the Dishhive
container use `http://host.docker.internal:5000` (Docker Desktop). A shared external Docker
network is the cleaner long-term option for Linux hosts — open question in the infrastructure plan.

## User Stories / Use Cases

1. As a planner, I see frozen meals/leftovers from Freezy when filling a slot, soonest-expiring first.
2. As a planner, I pick "frozen lasagna" and the slot records it came from the freezer.
3. As a planner, shopping list generation skips meals sourced from the freezer.
4. As a household, if Freezy is offline, Dishhive planning is unaffected.

## Domain Model Considerations

- `FrozenItem` is a **read model only** — never persisted in Dishhive's database.
- `PlannedMeal.FreezyItemRef` (string, nullable) is the only persistence touchpoint.

## Backend Requirements

- `IFreezyClient` + `FreezyHttpClient` (typed `HttpClient` via `AddHttpClient`, BaseUrl +
  User-Agent from config, short timeout)
- `GET /api/freezer/suggestions` → frozen items ordered by expiration (empty when unconfigured)
- Unit tests with mocked `HttpMessageHandler` (same pattern as Freezy's `OpenFoodFactsServiceTests`)

## Frontend Requirements

- Freezer suggestion panel in the planner slot editor (name, expiry badge reusing Freezy's
  visual language: error = expired, tertiary = expiring soon)
- Hidden entirely when the suggestions endpoint reports the integration is disabled

## Integration Requirements

- Config: `Freezy__BaseUrl` (compose + appsettings), documented in README
- No Freezy changes required — verified against Freezy's existing `ItemsController` API

## Risks / Unknowns

- Freezy API has no versioning today; a breaking DTO change silently breaks mapping →
  mitigate with tolerant JSON deserialization + a smoke test against Freezy's OpenAPI doc (manual for now).
- No auth on either app today; if Freezy adds auth, `FreezyHttpClient` needs a token strategy.
- `host.docker.internal` doesn't resolve on plain Linux Docker — documented; deployment-time concern.

## Phased Implementation Plan

**Phase 1 — Client + suggestions endpoint**
- `IFreezyClient`, ACL mapping, `/api/freezer/suggestions`, config plumbing, tests

**Phase 2 — Planner UI**
- Freezer panel in slot editor, `FreezyItemRef` recorded on selection

**Phase 3 — Lifecycle (future)**
- Optional write-back ("we ate it") via Freezy's consumption endpoints; needs product decision

## Implementation Checklist

- [x] `IFreezyClient` interface + `FrozenItem` read model
- [x] `FreezyHttpClient` implementation + DI/config registration
- [x] `GET /api/freezer/suggestions` endpoint
- [x] Unit tests (mocked handler: mapping, ordering, down, unconfigured, invalid JSON)
- [x] Freezer suggestion panel in slot editor (hidden when integration disabled)
- [x] `FreezyItemRef` persisted from UI selection
- [x] Shopping list skips freezer-sourced meals (tracked in shopping-list-export.md)
