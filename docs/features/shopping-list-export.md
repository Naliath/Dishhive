# Feature: Shopping List Export

## Goal
Generate a shopping list from a planned week so the cook walks into the store with a single, deduplicated list.

## Scope
- Generate a `ShoppingList` from a `WeekPlan`:
  - Aggregate ingredients across all meal slots that reference a recipe.
  - Skip slots that reference Freezy frozen items (already in inventory).
  - Optionally include "vague intent" slots as a free-text reminder line.
- Group by section/category if available, otherwise alphabetically.
- Allow manual edits (add/remove/check off items) without re-generating.
- Export as plain text (Markdown) for easy copy-paste.

## Out of scope (v1)
- Pantry inventory subtraction.
- Store-aisle ordering.
- Per-store price tracking.

## Domain model
```
ShoppingList
  Id (Guid)
  WeekPlanId (FK?)              ← optional, supports manual lists
  Title (string)
  CreatedAt / UpdatedAt

ShoppingListItem
  Id (Guid)
  ShoppingListId (FK)
  Order (int)
  Name (string, required)
  Quantity (decimal?)
  Unit (string?)
  Section (string?)
  Checked (bool)
  Note (string?)
```

## Backend requirements
- Controller: `ShoppingListsController`.
- Endpoints:
  - `POST /api/shopping-lists/from-week-plan/{weekPlanId}` — generate a new list.
  - `GET/PUT/DELETE /api/shopping-lists/{id}`
  - `PUT /api/shopping-lists/{id}/items/{itemId}` (toggle check, edit)
  - `GET /api/shopping-lists/{id}/export?format=markdown`
- Generation algorithm:
  1. Load week plan with slots+recipes+ingredients.
  2. For each ingredient, key by `(normalized name, unit)`; sum quantities (skipping when units differ — keep separate lines).
  3. Add free-text vague intents as plain reminder lines.
  4. Skip Freezy slots.

## Frontend requirements
- `pages/shopping-list/` with list view, check-off, edit-in-place.
- "Generate from current week" button on the planner.
- Markdown export via clipboard / download.

## Risks
- Ingredient deduplication on free-text names is imperfect. v1 uses case-insensitive trimmed match; users can manually merge.

## Phased plan
1. Entities + migrations.
2. Generation service.
3. CRUD + generation endpoints.
4. UI: list + check-off.
5. Export.

## Implementation checklist
- [x] Entities (`ShoppingList`, `ShoppingListItem`)
- [x] DbContext mappings
- [x] DTOs
- [x] `ShoppingListsController`
- [x] Generation service
- [x] Angular `ShoppingListsService`
- [x] Shopping list page
- [x] Generate-from-planner button
- [x] Markdown export
