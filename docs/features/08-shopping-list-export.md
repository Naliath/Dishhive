# Feature: Shopping List Export

**Created:** April 19, 2026  
**Status:** 🚧 In Progress (Phase 1 done, endpoint on WeekPlannerController)

---

## Feature Goal

Generate a shopping list from the planned week menu by aggregating ingredients from linked recipes, accounting for selected attendees and available frozen items.

---

## Scope

### In Scope
- Generate shopping list from a week plan
- Aggregate ingredients from all linked recipes in the week
- Scale quantities by attendee count vs recipe servings
- Exclude items flagged as "from freezer" (no purchase needed)
- Group ingredients by category (produce, dairy, meat, etc.) — optional Phase 2
- Display/export as plain text list

### Out of Scope (for this phase)
- Integration with external shopping apps
- Barcode scanning from shopping list
- Price tracking
- PDF export (future feature)

---

## User Stories

1. **As a meal planner**, I want to generate a shopping list from this week's plan.
2. **As a shopper**, I want the list to show all ingredients I need to buy.
3. **As a shopper**, I want to check off items as I shop.
4. **As a meal planner**, I want quantities scaled to the number of people eating each meal.

---

## Domain Model Considerations

Shopping lists are ephemeral — they are generated on demand from the week plan and not stored permanently (unless the user explicitly saves one).

A `ShoppingList` entity may be added in Phase 2 for saved/shared lists.

### Generation Algorithm

```
For each PlannedMeal in WeekPlan:
  If IsFromFreezer → skip (no shopping needed)
  If RecipeId is set:
    Load recipe ingredients
    Scale quantities: scaleFactor = attendeeCount / recipe.Servings
    Add scaled ingredients to aggregate list
  If VagueInstruction:
    Add as a reminder item (no quantity)

Aggregate: merge duplicate ingredients by name + unit
Sort: by ingredient name (Phase 1), by category (Phase 2)
```

---

## Backend Requirements

- `ShoppingListController` with endpoints:
  - `GET /api/shopping-list/{weekPlanId}` — generate list from week plan
  - Returns `ShoppingListDto` with aggregated ingredients

```csharp
public record ShoppingListDto(
    Guid WeekPlanId,
    DateOnly WeekStartDate,
    IEnumerable<ShoppingListItemDto> Items
);

public record ShoppingListItemDto(
    string Name,
    decimal? TotalQuantity,
    string? Unit,
    IEnumerable<string> UsedInMeals,  // which meals need this ingredient
    bool IsVagueReminder              // true for free-text vague instructions
);
```

---

## Frontend Requirements

- Shopping list page (accessible from week planner)
- Checklist UI (tap to check off an item)
- Share button (Web Share API for plain text copy/share)
- "Regenerate" button to refresh after plan changes

---

## Risks / Unknowns

- Ingredient deduplication: "1 ui" and "2 uien" (Dutch: 1 onion / 2 onions) won't merge automatically without NLP. Phase 1 deduplicates on exact string match only.
- Scaling: if attendeeCount is 0 (all meals are "from freezer"), produce an empty list.
- Vague instructions ("something quick") don't have ingredients — show as reminder items.

---

## Phased Implementation Plan

### Phase 1 — Basic Generation
- [ ] `ShoppingListController` with generation endpoint
- [ ] Ingredient aggregation and scaling logic
- [ ] Integration tests for shopping list generation
- [ ] Angular shopping list page

### Phase 2 — Categories & Export
- [ ] Ingredient category classification
- [ ] Grouped display by category
- [ ] Web Share API export

---

## Implementation Checklist

### Backend
- [x] Feature plan created
- [x] Shopping list endpoint created (`GET /api/weekplanner/{date}/shopping-list` on `WeekPlannerController`)
- [x] Ingredient aggregation logic (groups by ingredient name, sums quantities)
- [ ] Standalone `ShoppingListController` (future refactor)
- [x] Integration tests for shopping list (5 tests)

### Frontend
- [x] Shopping list page created
- [x] Checklist items with check-off state
- [x] Share/export button (Web Share API)
