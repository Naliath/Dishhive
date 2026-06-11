# Feature: Week Planner

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [family-composition.md](family-composition.md), [recipe-store.md](recipe-store.md),
[freezy-integration.md](freezy-integration.md), [shopping-list-export.md](shopping-list-export.md)

## Feature Goal

Plan the family's meals for a week: assign concrete dishes or vague intentions to days, pick
known recipes quickly, select who is present per meal, and leave clean extension points for
future AI-assisted planning.

## Scope

**In scope**
- Week view (Mon–Sun); a day holds any number of dishes (the everyday case is a single
  dinner main, but a lunch plus a dinner with appetizer and dessert is possible)
- Each dish carries a `MealType` (breakfast/lunch/dinner/snack) and a `Course`
  (main/appetizer/side/dessert); both default so the simple case needs no extra input
- Three planning styles per dish, mixable across the week:
  1. **Recipe**: link a known recipe from the recipe store
  2. **Named dish**: free-text dish name without a recipe ("spaghetti bolognese")
  3. **Vague instruction**: intention text ("something with fish", "something quick", "leftovers")
- Attendance: select which family members and guests are present per meal
- Marking a slot as sourced from a Freezy frozen item (see freezy-integration.md)
- Navigation between weeks; copying is future work

**Out of scope (seams only)**
- AI-assisted plan generation — implemented behind the `IMealSuggestionService` seam,
  see [ai-week-planning.md](ai-week-planning.md)
- Recurring meal rules ("pizza every Friday")

## User Stories / Use Cases

1. As a planner, I open next week and assign "spaghetti" (recipe) to Tuesday dinner.
2. As a planner, I write "something with fish" on Thursday and decide the details later.
3. As a planner, I plan "leftovers" on Monday and link a frozen lasagna from Freezy.
4. As a planner, I mark that only 2 of 4 members eat at home on Wednesday.
5. As a planner, I add Grandma (guest) to Sunday dinner.
6. As a planner, I quickly search and pick from my known recipes when filling a slot.
7. (Future) As a planner, I ask the app to propose a week plan that respects constraints.

## Domain Model Considerations

```
PlannedMeal
├── Id (Guid)
├── Date (DateOnly, required)
├── MealType (enum: Breakfast=0, Lunch=1, Dinner=2, Snack=3; default Dinner)
├── Course (enum: Main=0, Appetizer=1, Side=2, Dessert=3; default Main)
├── RecipeId (Guid?, FK set-null)        // planning style 1
├── DishName (string?, max 200)          // planning style 2 (also denormalized from recipe title for history)
├── VagueInstruction (string?, max 500)  // planning style 3
├── FreezyItemRef (string?, max 100)     // Freezy item id when meal comes from the freezer
├── Notes (string?, max 500)
├── CreatedAt / UpdatedAt
└── Attendees (ICollection<PlannedMealAttendee>)

PlannedMealAttendee
├── PlannedMealId (FK, cascade)
├── FamilyMemberId (FK, cascade)         // composite PK
```

- At least one of `RecipeId`, `DishName`, `VagueInstruction` must be set (validated in API).
- `DishName` is **always** filled when a recipe is linked (copy of the recipe title at planning
  time) so history/statistics survive recipe deletion or rename.
- No uniqueness per slot: a day can hold any number of dishes. `Main=0` so rows from
  before the `Course` column existed keep their meaning.
- History is *the same table*: past `PlannedMeal` rows are the dish history
  (see past-dishes-and-statistics.md). No separate history table.

### AI-assistance extension point (implemented)

```csharp
public interface IMealSuggestionService
{
    bool IsEnabled { get; }
    Task<IReadOnlyList<MealSuggestion>> SuggestAsync(MealSuggestionRequest request, CancellationToken ct);
}
```

The seam now has a real provider: when `Ai:Provider` is configured, an LLM-backed
implementation (Microsoft.Extensions.AI, five providers) with a deterministic rules
fallback is registered; otherwise the no-op stays registered and the UI hides the
feature. See [ai-week-planning.md](ai-week-planning.md).

## Backend Requirements

- `PlannedMealsController`:
  - `GET /api/plannedmeals?from=YYYY-MM-DD&to=YYYY-MM-DD` (week fetch)
  - `POST /api/plannedmeals`, `PUT /api/plannedmeals/{id}`, `DELETE /api/plannedmeals/{id}`
- Attendance set in Create/Update DTOs as `familyMemberIds: Guid[]`
- Validation: at-least-one-content rule
- `IMealSuggestionService` seam + no-op implementation registered in DI

## Frontend Requirements

- Page `pages/week-planner/` — 7-day grid, prev/next week navigation, today highlight;
  recipe-linked dishes link through to the recipe detail page
- Slot editor dialog: tabbed or segmented choice (recipe search / dish name / vague text),
  attendee chips (members preselected, guests opt-in), Freezy leftovers picker (later phase);
  the selected recipe has an open-recipe shortcut
- `planned-meals.service.ts`
- Mobile-friendly: day cards stack vertically on small screens (Material, no media queries)

## Integration Requirements

- Recipe store: recipe autocomplete in slot editor
- Family composition: attendee selection, allergy hints
- Freezy: leftover suggestions in slot editor (phase 3, behind `IFreezyClient`)
- Shopping list: consumes planned meals for a date range

## Risks / Unknowns

- Vague instructions are unstructured; AI planning will later need to interpret them.
  Acceptable: they're prompts for humans now, prompts for models later.

## Phased Implementation Plan

**Phase 1 — Backend planning core**
- Entities + migration, CRUD API with validation, attendance

**Phase 2 — Week view UI**
- Week grid, slot editor with three planning styles, attendee selection

**Phase 3 — Connected planning**
- Recipe autocomplete, Freezy leftover picker, allergy hints

**Phase 4 — Seam hardening**
- `IMealSuggestionService` request/response models finalized once phases 1–3 expose real needs

## Implementation Checklist

- [x] `PlannedMeal` + `PlannedMealAttendee` entities + migration
- [x] `PlannedMealsController` (week query, CRUD, validation)
- [x] Integration tests for planning rules (content rule, denormalization, multi-dish days)
- [x] `IMealSuggestionService` seam + no-op registration
- [x] `planned-meals.service.ts` + models
- [x] Week grid page with navigation (prev/next/today, today highlight)
- [x] Slot editor: dish name + vague instruction
- [x] Slot editor: recipe selection (search + pick)
- [x] Slot editor: attendee selection (members preselected, guests opt-in)
- [x] Freezy leftover picker in slot editor
- [x] Allergy hints in slot editor
- [x] Multiple dishes per day (meal + course selects in editor, "Add dish" on day cards,
      labels only shown when deviating from dinner main)
- [x] Phase 4 seam hardening: `IsEnabled` + widened `MealSuggestionRequest`, real providers
      (see ai-week-planning.md); "Suggest week" button when AI is configured
- [x] Eaten checkmark on past day cards (see meal-feedback.md)
