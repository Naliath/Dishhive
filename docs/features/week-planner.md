# Feature: Week Planner

## Goal
Plan meals for a week in a way that supports both concrete recipe assignments and vague intents like "something with fish" or "leftovers", and tracks who's eating.

## Scope
- A week plan covering Monday–Sunday, multiple meal slots per day (breakfast/lunch/dinner; configurable later).
- Each `MealSlot` has either:
  - a concrete `RecipeId`, **or**
  - a "vague intent" (free text + optional tag, e.g. `fish`, `quick`, `leftovers`),
  - **or** a reference to a Freezy frozen item (`FrozenItemRef`).
- Attendee selection per slot (subset of family members + guests).
- Multi-week navigation.

## Out of scope (v1)
- AI suggestions (only seam exists — `IMealSuggestionStrategy`).
- Auto-rotation / "don't repeat the same dish in 3 weeks" logic.
- Calendar export (iCal) — captured in [possible-features.md](../../possible-features.md).

## User stories
- *Plan Tuesday's dinner as "Spaghetti bolognese"* (recipe pick).
- *Plan Wednesday as "something with fish"* (vague intent).
- *Plan Sunday's lunch as a Freezy frozen meal* ("Lasagna from October").
- *Mark Friday: only 3 of 4 family members present + 2 guests*.

## Domain model
```
WeekPlan
  Id (Guid)
  WeekStart (date, the Monday)
  Notes (string?)
  CreatedAt / UpdatedAt

MealSlot
  Id (Guid)
  WeekPlanId (FK)
  DayOfWeek (enum)
  MealType (enum: Breakfast, Lunch, Dinner, Snack)
  RecipeId (Guid?, FK — when a recipe is chosen)
  VagueIntent (string?, e.g. "something with fish")
  IntentTag (enum?: Fish, Quick, Leftovers, Vegetarian, Other)
  FrozenItemRef (string?, opaque Freezy item id)
  Notes (string?)

MealSlotAttendee
  Id (Guid)
  MealSlotId (FK)
  FamilyMemberId (Guid?)   ← exactly one of these two is set
  GuestId (Guid?)
```

## Backend requirements
- Controllers: `WeekPlansController` (CRUD on plans) + nested route for slots.
- Endpoints:
  - `GET /api/week-plans?weekStart=YYYY-MM-DD`
  - `POST /api/week-plans`
  - `PUT /api/week-plans/{id}`
  - `PUT /api/week-plans/{id}/slots/{slotId}` (assign recipe / set intent / clear)
  - `PUT /api/week-plans/{id}/slots/{slotId}/attendees`
- `IMealSuggestionStrategy` interface with default no-op implementation.

## Frontend requirements
- `pages/week-planner/` showing a 7×N grid (days × meal types).
- Slot picker dialog with three tabs: **Recipe**, **Intent**, **From Freezy**.
- Attendee chips per slot.
- Material `mat-tabs`, `mat-card`, `mat-chip-listbox`, `mat-autocomplete` for recipe search.

## Integration requirements
- Pulls Freezy frozen items via `IFreezyClient` for the "From Freezy" tab.
- Reads `FamilyMember`s and `Guest`s for attendee selection.
- Provides input to shopping-list generation.

## Risks / unknowns
- "Vague intent" semantics: in v1 we just store the text/tag and surface it. AI suggestions are not implemented.
- Time zone / week-start configuration deferred (week starts Monday).

## Phased plan
1. Entities + migrations.
2. CRUD endpoints.
3. Angular grid + read-only display.
4. Slot edit dialog: Recipe + Intent.
5. Attendees editor.
6. Freezy tab (depends on freezy-integration feature).
7. AI strategy seam.

## Implementation checklist
- [x] Entities (`WeekPlan`, `MealSlot`, `MealSlotAttendee`, enums)
- [x] DbContext mappings
- [x] DTOs
- [x] `WeekPlansController`
- [x] `IMealSuggestionStrategy` seam
- [x] Angular `WeekPlannerPage`
- [x] Slot edit dialog (Recipe / Intent)
- [x] Attendees editor
- [ ] Freezy tab in slot dialog
