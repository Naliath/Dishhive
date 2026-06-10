# Feature: Past Dishes & Statistics

> **Feature ID**: PDS-001
> **Status**: Planned
> **Priority**: High
> **Depends on**: Week Planner, Recipe Store, Family Composition
> **Last Updated**: 2026-06-07

## 1. Feature Goal

Track the history of planned and cooked dishes, provide a favorites system per family member, and display statistics about dish usage over time.

## 2. Scope

### In Scope (v1)
- Automatic archiving of completed week plans into dish history
- Per-dish cooking/planning count
- Per-family-member favorites system (mark/unmark)
- Statistics: most planned dishes, least planned, frequency over time
- Filter history by date range, family member, recipe
- "Don't cook again" / "Cook again" indicators

### Out of Scope (v1)
- Nutritional tracking
- Cost tracking per meal
- Photo uploads of cooked dishes
- Rating system beyond favorites

## 3. User Stories / Use Cases

| ID | Story | Priority |
|----|-------|----------|
| PDS-US-001 | As a user, I want to see a history of all past planned meals | Must |
| PDS-US-002 | As a user, I want to mark dishes as favorites per family member | Must |
| PDS-US-003 | As a user, I want to see how often a dish was planned | Must |
| PDS-US-004 | As a user, I want to filter history by date range | Should |
| PDS-US-005 | As a user, I want to filter history by family member | Should |
| PDS-US-006 | As a user, I want to see statistics about meal planning patterns | Could |

## 4. Domain Model

```
DishHistoryEntry
├── Id: UUID
├── WeekPlanId: UUID (FK)
├── PlannedMealId: UUID (FK)
├── RecipeId: UUID? (FK to Recipe)
├── DishName: string
├── PlannedDate: Date
├── ActualDate: Date? (when actually cooked)
├── MealType: enum (Breakfast, Lunch, Dinner, Snack)
├── DayOfWeek: enum
├── WasFavorite: bool? (was it marked favorite after cooking)
├── CookAgain: bool? (would you cook this again)
├── Notes: string?
├── CreatedAt: DateTime
└── UpdatedAt: DateTime

DishFavorite
├── Id: UUID
├── FamilyMemberId: UUID (FK)
├── RecipeId: UUID (FK to Recipe)
├── AddedAt: DateTime
└── Rank: int? (optional ordering within favorites)

DishStatistics
├── Id: UUID
├── RecipeId: UUID (FK)
├── TimesPlanned: int
├── TimesCooked: int?
├── TimesFavorited: int (how many family members favorited)
├── LastPlannedDate: Date?
├── AverageCookAgainRating: float?
├── CalculatedAt: DateTime
└── Period: enum (AllTime, LastMonth, LastQuarter, LastYear)
```

## 5. Backend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| PDS-BE-001 | Auto-create history entries when week plan is archived | Must |
| PDS-BE-002 | CRUD API for DishFavorite | Must |
| PDS-BE-003 | GET dish history with filters | Must |
| PDS-BE-004 | GET dish statistics | Must |
| PDS-BE-005 | PATCH history entry (mark favorite, cook again) | Should |
| PDS-BE-006 | Statistics caching and refresh | Should |

### API Endpoints

```
GET    /api/dish-history
GET    /api/dish-history/{id}
PATCH  /api/dish-history/{id}
GET    /api/dish-history?from={date}&to={date}&memberId={id}&recipeId={id}

POST   /api/dish-favorites
GET    /api/dish-favorites
GET    /api/dish-favorites?memberId={id}
DELETE /api/dish-favorites/{id}

GET    /api/dish-statistics
GET    /api/dish-statistics?period={period}&sortBy={field}
GET    /api/dish-statistics/{recipeId}
```

## 6. Frontend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| PDS-FE-001 | Dish history list/timeline view | Must |
| PDS-FE-002 | Favorites list per family member | Must |
| PDS-FE-003 | Statistics dashboard with charts | Should |
| PDS-FE-004 | Filter controls for history | Should |
| PDS-FE-005 | Favorite toggle on recipe cards | Should |
| PDS-FE-006 | Cook again / don't cook again indicators | Could |

### UI Components

- `DishHistoryComponent` — timeline/list of past meals
- `DishHistoryFiltersComponent` — date range, member, recipe filters
- `DishFavoriteComponent` — favorites management
- `DishStatisticsComponent` — charts and stats dashboard
- `FavoriteToggleButton` — star/heart toggle on recipe cards

## 7. Integration Requirements

| ID | Integration | Direction | Notes |
|----|------------|-----------|-------|
| PDS-INT-001 | Week Planner | Inbound | Archived plans become history |
| PDS-INT-002 | Recipe Store | Inbound | Recipe details for display |
| PDS-INT-003 | Family Composition | Inbound | Family member names and avatars |
| PDS-INT-004 | Week Planner | Outbound | Stats inform future planning |

## 8. Risks / Unknowns

| ID | Risk | Mitigation |
|----|------|------------|
| PDS-R001 | Statistics calculations may be slow on large datasets | Pre-compute and cache statistics; refresh periodically |
| PDS-R002 | History data retention policy unclear | Document retention policy; consider soft-delete with archival |

## 9. Phased Implementation Plan

### Phase 1 — History Tracking
- [ ] Database migration for DishHistoryEntry
- [ ] Auto-archive logic when week plan completes
- [ ] History list endpoint with basic filters
- [ ] Frontend history view

### Phase 2 — Favorites System
- [ ] Database migration for DishFavorite
- [ ] Favorite CRUD endpoints
- [ ] Favorite toggle on recipe cards
- [ ] Favorites list per family member

### Phase 3 — Statistics
- [ ] Database migration for DishStatistics
- [ ] Statistics calculation service
- [ ] Statistics caching strategy
- [ ] Statistics dashboard with charts

## 10. Implementation Checklist

### Infrastructure
- [ ] Database migration created and applied
- [ ] Entity models defined
- [ ] DTOs defined

### Backend
- [ ] DishHistoryController
- [ ] DishHistoryService
- [ ] DishFavoriteController
- [ ] DishFavoriteService
- [ ] DishStatisticsService
- [ ] Auto-archive background job
- [ ] Unit tests
- [ ] Integration tests

### Frontend
- [ ] Dish history component
- [ ] History filters
- [ ] Favorites component
- [ ] Statistics dashboard
- [ ] Favorite toggle button
- [ ] Service/HTTP client

### Testing
- [ ] Unit tests for statistics calculations
- [ ] Integration tests for API
- [ ] E2E tests for history browsing
