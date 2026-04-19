# Feature: Week Planner

**Created:** April 19, 2026  
**Status:** ✅ Implemented

---

## Feature Goal

Allow the household to plan meals for each day of the week, supporting both concrete recipe assignments and vague planning instructions. The planner tracks which family members attend each meal and provides extension points for future AI-assisted planning.

---

## Scope

### In Scope
- Assigning dishes to specific days of the week (dinner focus, with optional breakfast/lunch)
- Vague meal instructions (free-text: "something with fish", "leftovers", "something quick")
- Easy selection of known recipes from the recipe store
- Selecting which family members are present for each meal
- Guest inclusion in meal attendance
- Extension point for future AI-assisted suggestions

### Out of Scope (for this phase)
- Automatic AI-assisted planning (extension point only)
- Multi-week planning (one week at a time)
- Calendar sync (future feature)
- Nutritional balance tracking (future feature)

---

## User Stories

1. **As a meal planner**, I want to assign a recipe to each day of the week so I know what to cook.
2. **As a meal planner**, I want to enter vague instructions like "something with fish" when I haven't decided yet.
3. **As a meal planner**, I want to quickly pick a recipe from my stored collection without typing.
4. **As a meal planner**, I want to specify which family members are eating each meal so the shopping list is accurate.
5. **As a meal planner**, I want to copy last week's plan as a starting point.
6. **As a household member**, I want to see the week's plan at a glance on my phone.
7. **As a future AI user** (extension point), I want the planner to suggest meals based on family preferences and available frozen items.

---

## Domain Model Considerations

```
WeekPlan
  - Id: Guid
  - WeekStartDate: DateOnly   (Monday of the week)
  - CreatedAt: DateTime
  - UpdatedAt: DateTime

PlannedMeal
  - Id: Guid
  - WeekPlanId: Guid (FK → WeekPlan)
  - DayOfWeek: enum (Monday..Sunday)
  - MealType: enum (Breakfast, Lunch, Dinner, Snack) — default Dinner
  - RecipeId: Guid? (FK → Recipe, nullable)
  - VagueInstruction: string?  (e.g., "something with fish")
  - IsFromFreezer: bool        (flag: use a frozen item from Freezy)
  - FreezerItemId: Guid?       (external reference to Freezy item, not a FK)
  - Notes: string?
  - AttendeeIds: Guid[]        (serialized as JSON column — FamilyMember IDs)
  - CreatedAt: DateTime
  - UpdatedAt: DateTime
```

### Note on Attendee Storage
`AttendeeIds` is stored as a JSON array column for simplicity. This avoids a join table for a list that is always loaded with the meal. EF Core supports JSON columns on PostgreSQL.

---

## Backend Requirements

- `WeekPlannerController` with endpoints:
  - `GET /api/weekplanner/{weekStartDate}` — get plan for a week (ISO date: YYYY-MM-DD)
  - `POST /api/weekplanner` — create a week plan
  - `PUT /api/weekplanner/{id}/meals` — upsert all meals for a week
  - `POST /api/weekplanner/{id}/meals` — add a single meal
  - `PUT /api/weekplanner/{id}/meals/{mealId}` — update a meal
  - `DELETE /api/weekplanner/{id}/meals/{mealId}` — remove a meal
  - `POST /api/weekplanner/{id}/copy-from/{sourceWeekPlanId}` — copy a previous plan

### AI Extension Point

```csharp
// Extension seam — not implemented yet
public interface IWeekPlanSuggestionProvider
{
    Task<IEnumerable<MealSuggestionDto>> SuggestAsync(WeekPlanSuggestionInputDto input);
}

public record WeekPlanSuggestionInputDto(
    IEnumerable<FamilyMemberDto> Family,
    IEnumerable<PastMealSummaryDto> RecentHistory,
    IEnumerable<FrozenItemDto> AvailableFrozenItems,
    string? FreeTextHint
);
```

The controller has a stubbed `POST /api/weekplanner/{id}/suggest` endpoint that returns 501 Not Implemented until an `IWeekPlanSuggestionProvider` is registered.

---

## Frontend Requirements

- Week planner page at `/` (default home route)
- 7-column grid layout (Mon–Sun) responsive to mobile (vertical scroll on small screens)
- Each day card shows: meal name/instruction, recipe thumbnail if available, attendee avatars
- Tap/click a day slot to open a meal assignment dialog:
  - Search/select a recipe
  - OR type a vague instruction
  - OR pick "from freezer" (Freezy integration)
  - Select attendees
- Week navigation (previous/next week buttons)
- "Copy from last week" shortcut button
- Placeholder "Suggest" button (disabled, tooltip: "AI planning coming soon")

---

## Integration Requirements

- Recipe picker in the meal dialog pulls from the Recipe Store
- "From freezer" slot integrates with Freezy (see `04-freezy-integration.md`)
- Attendees picked from `FamilyService`

---

## Risks / Unknowns

- JSON column for `AttendeeIds` on PostgreSQL is well supported by EF Core 8+; verify with EF Core 10.
- `WeekStartDate` must always be normalized to Monday — enforce in service layer.
- Vague instruction and recipe are mutually exclusive — enforce in DTO validation.
- The 7-column week grid may be difficult to render well on phones — consider a vertical daily view as an alternative for mobile.

---

## Phased Implementation Plan

### Phase 1 — Core Planner CRUD
- [ ] `WeekPlan` and `PlannedMeal` entities + EF config
- [ ] `WeekPlannerController` with get/create/update endpoints
- [ ] `WeekPlannerDtos.cs`
- [ ] Angular week planner page (basic grid)
- [ ] `WeekPlannerService` in Angular

### Phase 2 — Recipe Picker & Attendees
- [ ] Recipe picker dialog in planner
- [ ] Attendee selector in planner dialog
- [ ] Attendee display on day cards

### Phase 3 — Freezer Integration in Planner
- [ ] "From freezer" option in meal dialog (Freezy integration)

### Phase 4 — AI Extension Point
- [ ] `IWeekPlanSuggestionProvider` interface
- [ ] Stub 501 endpoint
- [ ] Disabled "Suggest" button in UI

---

## Implementation Checklist

### Backend
- [x] Feature plan created
- [x] `WeekPlan` entity created
- [x] `PlannedMeal` entity created
- [x] EF Core configuration added
- [x] `WeekPlannerController` created
- [x] `WeekPlannerDtos.cs` created
- [x] `IWeekPlanSuggestionProvider` interface created + `StubWeekPlanSuggestionProvider`
- [x] `/suggest` endpoint (501 stub, ready for AI provider)
- [x] Integration tests for week planner (8 tests)

### Frontend
- [x] `week-plan.model.ts` created
- [x] `WeekPlannerService` created
- [x] Week planner page component created
- [x] Meal assignment dialog (`PlanMealDialog`) created
- [x] Attendee selection per meal
