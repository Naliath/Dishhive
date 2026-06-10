# Feature: Shopping List Export

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [week-planner.md](week-planner.md), [recipe-store.md](recipe-store.md),
[measurement-preferences.md](measurement-preferences.md), [freezy-integration.md](freezy-integration.md)

## Feature Goal

Generate a shopping list from the planned week menu, using recipe ingredients and planned
meals as input, so groceries match the plan.

## Scope

**In scope**
- Generate a list for a date range (default: the viewed week)
- Input: planned meals → linked recipes → structured ingredients
- Aggregation of identical ingredients across recipes (same name + compatible unit)
- Scaling by attendance vs. recipe servings (attendee count / recipe servings)
- Meals without a recipe (named dish / vague instruction) listed as "needs deciding" reminders
- Freezer-sourced meals excluded (already in the freezer)
- Export: copy-to-clipboard as plain text (checklist persistence is a later phase)

**Out of scope (initially)**
- Persisted/checkable shopping list state across devices
- Pantry stock subtraction
- Supermarket category sorting, store integrations (see possible-features.md)

## User Stories / Use Cases

1. As a planner, I generate the shopping list for next week with one click.
2. As a planner, quantities reflect how many people attend each meal.
3. As a planner, two recipes both using onions yield one aggregated onion line.
4. As a planner, the frozen-lasagna Monday adds nothing to the list.
5. As a planner, "something with fish — Thursday" appears as a reminder, not as ingredients.
6. As a shopper, I copy the list as text into my notes/messaging app.

## Domain Model Considerations

- **No new persisted entities initially** — the list is computed on demand:
  `PlannedMeal(range) ⨝ Recipe ⨝ RecipeIngredient`, scaled and aggregated into DTOs.
- Scaling: `factor = attendeeCount / recipe.Servings` (factor 1 when no attendees recorded).
- Aggregation key: case-insensitive ingredient name + canonical unit. Unparseable quantities
  pass through as separate verbatim lines (per measurement-preferences.md rules).
- A future persisted `ShoppingList`/`ShoppingListItem` (check-off state, manual extra items)
  is anticipated but deliberately deferred — compute-on-demand keeps the model honest until
  real usage demands persistence.

## Backend Requirements

- `GET /api/shoppinglist?from=YYYY-MM-DD&to=YYYY-MM-DD` →
  `{ items: [ { name, quantity?, unit?, sourceRecipes: [titles] } ], reminders: [ { date, text } ] }`
- Aggregation/scaling service `ShoppingListService` with unit tests (the math is the feature)
- Always metric in the API; display conversion is frontend (measurement-preferences.md)

## Frontend Requirements

- Page `pages/shopping-list/` — date-range (defaults to planner's current week), grouped list,
  reminder section, copy-as-text button
- Entry point from the week planner ("Shopping list for this week")
- `shopping-list.service.ts`

## Integration Requirements

- Reads planner + recipe data only via its own endpoint
- Honors measurement preference for display
- Skips meals with `FreezyItemRef` set

## Risks / Unknowns

- Name-based aggregation fragments on spelling ("ui" vs "uien") — accepted initially;
  ingredient canonicalization is future work.
- Scaling assumes linear ingredient scaling — fine for shopping accuracy.

## Phased Implementation Plan

**Phase 1 — Generation endpoint** (after planner phase 1 + recipe store phase 1)
- `ShoppingListService` + endpoint + unit tests for scaling/aggregation/skip rules

**Phase 2 — UI + export**
- Shopping list page, planner entry point, copy-as-text

**Phase 3 — Persistence (future)**
- Checkable persisted list + manual items, if usage demands it

## Implementation Checklist

- [x] `ShoppingListService` (scaling, aggregation, freezer-skip, reminders)
- [x] Unit tests for aggregation/scaling/skip rules (7 tests)
- [x] `GET /api/shoppinglist` endpoint + DTOs
- [x] Shopping list page with date range (query params or current week default)
- [x] Copy-as-text export
- [x] Planner entry point ("Shopping list" button with the viewed week)
