# Feature: Freezy Integration

> **Feature ID**: FRZ-001
> **Status**: Planned
> **Priority**: High
> **Depends on**: Week Planner, Recipe Store
> **Last Updated**: 2026-06-07

## 1. Feature Goal

Enable Dishhive to reference frozen meals and leftovers from Freezy (the family freezer inventory application) so users can plan meals using items already stored in their freezer.

## 2. Scope

### In Scope (v1)
- Read-only access to Freezy inventory items (frozen meals, leftovers)
- Display available frozen items in meal planning UI
- Assign a frozen item to a meal slot in the week planner
- Mark a frozen item as "used" when assigned to a meal
- Basic status indicators (available, reserved, used)
- Integration health indicator (is Freezy reachable?)

### Out of Scope (v1)
- Writing back to Freezy (no inventory modifications from Dishhive)
- Real-time sync (polling or on-demand fetch only)
- Complex freezer-to-recipe matching algorithms
- Bi-directional sync

## 3. User Stories / Use Cases

| ID | Story | Priority |
|----|-------|----------|
| FRZ-US-001 | As a user, I want to see what frozen meals I have available | Must |
| FRZ-US-002 | As a user, I want to assign a frozen meal to a meal slot | Must |
| FRZ-US-003 | As a user, I want to know if Freezy is reachable | Must |
| FRZ-US-004 | As a user, I want to see frozen items marked as "use soon" | Should |
| FRZ-US-005 | As a user, I want to filter frozen items by category | Should |
| FRZ-US-006 | As a user, I want to see when a frozen item was stored | Could |

## 4. Domain Model

```
FreezyConnection
├── Id: UUID
├── BaseUrl: string
├── ApiKey: string? (encrypted)
├── IsConnected: bool
├── LastSyncAt: DateTime?
├── SyncIntervalMinutes: int
└── Status: enum (Connected, Disconnected, Error)

FreezyInventoryItem (cached mirror)
├── Id: UUID
├── FreezyItemId: string (external ID from Freezy)
├── Name: string
├── Category: string?
├── Description: string?
├── Quantity: int
├── Unit: string?
├── StoredDate: Date?
├── ExpiryDate: Date?
├── UseByDate: Date?
├── Status: enum (Available, Reserved, Used)
├── LastSyncAt: DateTime
├── ThumbnailUrl: string?
└── Metadata: JSON? (freezer-specific extra fields)

FreezyMealAssignment
├── Id: UUID
├── PlannedMealId: UUID (FK to PlannedMeal)
├── FreezyInventoryItemId: UUID (FK)
├── AssignedAt: DateTime
├── UsedQuantity: int
└── Notes: string?
```

### Integration Architecture

```
┌─────────────────────────────────────────────┐
│                 Dishhive                     │
│                                             │
│  Week Planner ──→ FreezyService ──→ Freezy  │
│                           │                 │
│                           ▼                 │
│                   CachedInventory            │
│                   (local mirror)             │
└─────────────────────────────────────────────┘
```

Key design decisions:
- **Read-only access**: Dishhive never writes to Freezy
- **Cached mirror**: Inventory items are cached locally to handle offline Freezy scenarios
- **On-demand sync**: Sync triggered manually or on a configurable interval
- **Graceful degradation**: If Freezy is unreachable, Dishhive continues working with cached data

## 5. Backend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| FRZ-BE-001 | Configurable Freezy connection settings | Must |
| FRZ-BE-002 | HTTP client to Freezy API with retry logic | Must |
| FRZ-BE-003 | Inventory sync service (fetch and cache) | Must |
| FRZ-BE-004 | Read cached inventory items | Must |
| FRZ-BE-005 | Assign frozen item to meal slot | Must |
| FRZ-BE-006 | Connection health check endpoint | Must |
| FRZ-BE-007 | Mark frozen item status as reserved/used | Should |
| FRZ-BE-008 | Sync scheduling (background job) | Should |

### API Endpoints

```
GET    /api/freezy/connection
PUT    /api/freezy/connection
POST   /api/freezy/connection/test
POST   /api/freezy/sync

GET    /api/freezy/inventory
GET    /api/freezy/inventory/{id}
GET    /api/freezy/inventory?status={status}&category={category}

POST   /api/freezy/assignments
GET    /api/freezy/assignments
GET    /api/freezy/assignments?mealId={id}
DELETE /api/freezy/assignments/{id}
```

## 6. Frontend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| FRZ-FE-001 | Frozen item picker in meal planning | Must |
| FRZ-FE-002 | Connection status indicator | Must |
| FRZ-FE-003 | Manual sync button | Should |
| FRZ-FE-004 | Frozen items list with status badges | Should |
| FRZ-FE-005 | "Use soon" warning indicators | Could |

### UI Components

- `FreezyConnectionIndicatorComponent` — status badge
- `FreezyItemPickerComponent` — browse and select frozen items
- `FreezyInventoryListComponent` — full inventory view
- `FreezySyncButtonComponent` — manual sync trigger

## 7. Integration Requirements

| ID | Integration | Direction | Notes |
|----|------------|-----------|-------|
| FRZ-INT-001 | Freezy API | Outbound | Read-only HTTP calls to Freezy |
| FRZ-INT-002 | Week Planner | Inbound | Frozen items available for meal slots |
| FRZ-INT-003 | Recipe Store | Inbound | Frozen items may link to known recipes |

### Freezy API Contract (Expected)

Based on Freezy conventions, the expected API surface:

```
GET /api/inventory              - List all inventory items
GET /api/inventory/{id}         - Get specific item
GET /api/inventory/categories   - Get available categories
GET /api/inventory?status={s}   - Filter by status
GET /api/inventory?category={c} - Filter by category
```

**Assumption**: Freezy exposes a REST API with JSON responses. If Freezy's actual API differs, the `IFreezyApiClient` interface will be adapted accordingly.

## 8. Risks / Unknowns

| ID | Risk | Mitigation |
|----|------|------------|
| FRZ-R001 | Freezy API contract may change | Versioned API client; defensive parsing |
| FRZ-R002 | Freezy may be unavailable during planning | Cached inventory with staleness indicator |
| FRZ-R003 | Freezy may not have a public API yet | Document integration contract; mock for development |
| FRZ-R004 | Duplicate items (same frozen meal as a recipe) | Dedup logic; allow both references |

## 9. Phased Implementation Plan

### Phase 1 — Connection & Health Check
- [ ] Database migration for FreezyConnection
- [ ] Connection settings UI
- [ ] Health check endpoint
- [ ] Connection indicator in UI

### Phase 2 — Inventory Sync
- [ ] Database migration for FreezyInventoryItem
- [ ] HTTP client to Freezy API
- [ ] Sync service with caching
- [ ] Manual sync trigger

### Phase 3 — Meal Assignment
- [ ] Database migration for FreezyMealAssignment
- [ ] Assign frozen item to meal slot
- [ ] Frozen item picker in planner
- [ ] Status management (available/reserved/used)

### Phase 4 — Polish
- [ ] Scheduled background sync
- [ ] "Use soon" indicators
- [ ] Category filtering
- [ ] Error handling and user feedback

## 10. Implementation Checklist

### Infrastructure
- [ ] Database migration created and applied
- [ ] Entity models defined
- [ ] DTOs defined
- [ ] API client interface defined

### Backend
- [ ] FreezyConnectionController
- [ ] FreezyConnectionService
- [ ] IFreezyApiClient interface
- [ ] FreezyApiClient implementation
- [ ] Inventory sync service
- [ ] FreezyAssignmentController
- [ ] FreezyAssignmentService
- [ ] Background sync job
- [ ] Unit tests
- [ ] Integration tests (with mocked Freezy)

### Frontend
- [ ] Connection indicator component
- [ ] Frozen item picker
- [ ] Inventory list component
- [ ] Sync button
- [ ] Service/HTTP client

### Testing
- [ ] Unit tests for sync logic
- [ ] Integration tests with mocked Freezy API
- [ ] E2E tests for frozen meal assignment
