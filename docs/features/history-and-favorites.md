# Feature: History & Favorites

## Goal
Show what was planned in the past, and let family members favorite dishes so the planner becomes more useful over time.

## Scope
- `DishHistoryEntry` is created automatically whenever a `MealSlot` for a date in the past is non-empty.
- Per-family-member `DishFavorite` (recipe-linked or free-text dish name).
- Statistics: how often a dish was planned, last time it was planned, top dishes.

## Out of scope
- Cross-household statistics, sharing, ratings.
- Skipped/cancelled meal tracking (could be a future enhancement).

## User stories
- *Show me the last 30 days of planned dinners*.
- *Top 10 most-planned dishes this year*.
- *Mark "Pizza" as Sam's favorite*.

## Domain model
```
DishHistoryEntry
  Id (Guid)
  Date (date)
  MealType (enum)
  RecipeId (Guid?, FK)
  DishLabel (string)   ← duplicated from recipe name or vague intent at planning time so deletes don't lose history
  PlannedSlotId (Guid?, soft reference)
  CreatedAt

DishFavorite
  Id (Guid)
  FamilyMemberId (FK)
  RecipeId (Guid?, FK — optional, for recipe favorites)
  DishLabel (string)   ← required so free-text favorites work too
```

## Backend requirements
- A background or on-write service that materializes `DishHistoryEntry` rows from `MealSlot`s when "today" passes (cheap implementation: query slots from week plans whose `WeekStart` < today and ensure history rows exist; runs on every `GET /api/history`).
- Endpoints:
  - `GET /api/history?from=...&to=...`
  - `GET /api/statistics/dish-frequency?from=...&to=...`
  - `GET/POST/DELETE /api/family-members/{id}/favorites`

## Frontend requirements
- `pages/history/` with a list and a small stats panel (chart deferred — start with table + counts).
- Favorites managed inline on the family member detail view.

## Integration requirements
- Reads from `WeekPlan`/`MealSlot`.
- Cross-references `Recipe` for display names.

## Risks / unknowns
- If a recipe is renamed or deleted, history must remain readable: that's why we duplicate `DishLabel` on the history entry.

## Phased plan
1. Entities.
2. Read endpoints (history, stats).
3. Materialization logic.
4. Favorites endpoints.
5. UI: history table.
6. UI: top dishes / counts.

## Implementation checklist
- [x] Entities (`DishHistoryEntry`, `DishFavorite`)
- [x] DbContext mappings
- [x] History materialization service
- [x] DTOs
- [x] `HistoryController`
- [x] `StatisticsController.DishFrequency`
- [x] Favorite endpoints on family-member controller
- [x] Angular `HistoryPage`
- [ ] Favorites UI on family member detail
