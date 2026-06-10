# Feature: Week Planner

> **Feature ID**: WKP-001
> **Status**: Planned
> **Priority**: Critical
> **Depends on**: Family Composition, Recipe Store
> **Last Updated**: 2026-06-07

## 1. Feature Goal

Provide a weekly meal planning interface where users can assign dishes or vague meal instructions to specific days and meals, select which family members are present, and prepare for future AI-assisted planning.

## 2. Scope

### In Scope (v1)
- Weekly calendar view (Monday–Sunday)
- Assign specific recipes to day + meal slot (breakfast, lunch, dinner, snack)
- Enter vague instructions ("something with fish", "leftovers", "quick meal")
- Select present family members and guests per meal
- Browse and pick from recipe store
- Mark meals as "use frozen meal from Freezy"
- Save/load week plans
- Archive past weeks

### Out of Scope (v1)
- AI-assisted auto-planning (extension points only)
- Multi-week planning
- Calendar sync (Google Calendar, etc.)
- Meal notification reminders

## 3. User Stories / Use Cases

| ID | Story | Priority |
|----|-------|----------|
| WKP-US-001 | As a user, I want to see a weekly calendar so I can plan all meals at once | Must |
| WKP-US-002 | As a user, I want to assign a recipe to a specific day and meal slot | Must |
| WKP-US-003 | As a user, I want to write vague instructions like "something with fish" | Must |
| WKP-US-004 | As a user, I want to select which family members eat each meal | Must |
| WKP-US-005 | As a user, I want to browse recipes and pick one quickly | Must |
| WKP-US-006 | As a user, I want to mark a meal as "leftovers" | Should |
| WKP-US-007 | As a user, I want to include a frozen meal from Freezy | Should |
| WKP-US-008 | As a user, I want to save and revisit past week plans | Could |

## 4. Domain Model

```
WeekPlan
├── Id: UUID
├── WeekStart: Date (Monday)
├── WeekEnd: Date (Sunday)
├── CreatedBy: string
├── CreatedAt: DateTime
├── UpdatedAt: DateTime
├── Status: enum (Draft, Active, Archived)
└── Meals: Collection<PlannedMeal>

PlannedMeal
├── Id: UUID
├── WeekPlanId: UUID (FK)
├── DayOfWeek: enum (Monday..Sunday)
├── MealType: enum (Breakfast, Lunch, Dinner, Snack)
├── RecipeId: UUID? (FK to Recipe, nullable for vague instructions)
├── VagueInstruction: string? (e.g., "something with fish")
├── IsLeftovers: bool
├── LeftoverSourceMealId: UUID? (FK to PlannedMeal, references previous meal)
├── FrozenMealId: UUID? (FK to Freezy integration model)
├── ServingCount: int
├── Notes: string?
├── PlannedAttendees: Collection<MealAttendee>
└── PlanningHints: Collection<PlanningHint>

MealAttendee
├── Id: UUID
├── PlannedMealId: UUID (FK)
├── FamilyMemberId: UUID (FK)
└── IsPresent: bool

PlanningHint
├── Id: UUID
├── PlannedMealId: UUID (FK)
├── HintType: enum (Ingredient, Cuisine, CookingTime, Dietary, Budget)
├── HintValue: string
└── Priority: enum (Required, Preferred, Optional)

// Extension point for AI planning
AiPlanningRequest
├── Id: UUID
├── WeekPlanId: UUID (FK)
├── PlannedMealId: UUID (FK)
├── Prompt: string
├── Constraints: string (JSON)
├── Status: enum (Pending, Processing, Completed, Failed)
├── SuggestedRecipeId: UUID?
├── CreatedAt: DateTime
└── CompletedAt: DateTime?
```

## 5. Backend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| WKP-BE-001 | CRUD API for WeekPlan | Must |
| WKP-BE-002 | CRUD API for PlannedMeal | Must |
| WKP-BE-003 | GET week plan with all meals expanded | Must |
| WKP-BE-004 | Validate meal constraints against family member dietary needs | Must |
| WKP-BE-005 | Archive week plan endpoint | Should |
| WKP-BE-006 | GET past week plans | Should |
| WKP-BE-007 | PlanningHint validation and storage | Should |
| WKP-BE-008 | AiPlanningRequest model and endpoints (stub) | Could (v1 stub only) |

### API Endpoints

```
GET    /api/week-plans
POST   /api/week-plans
GET    /api/week-plans/{id}
PUT    /api/week-plans/{id}
DELETE /api/week-plans/{id}
POST   /api/week-plans/{id}/archive
GET    /api/week-plans/archived

GET    /api/week-plans/{weekPlanId}/meals
POST   /api/week-plans/{weekPlanId}/meals
GET    /api/week-plans/{weekPlanId}/meals/{mealId}
PUT    /api/week-plans/{weekPlanId}/meals/{mealId}
DELETE /api/week-plans/{weekPlanId}/meals/{mealId}

POST   /api/week-plans/{weekPlanId}/meals/{mealId}/suggest
GET    /api/week-plans/{weekPlanId}/meals/{mealId}/hints
POST   /api/week-plans/{weekPlanId}/meals/{mealId}/hints
```

## 6. Frontend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| WKP-FE-001 | Weekly calendar grid view | Must |
| WKP-FE-002 | Drag-and-drop recipe to day/slot | Should |
| WKP-FE-003 | Vague instruction text input per slot | Must |
| WKP-FE-004 | Recipe picker/browser dialog | Must |
| WKP-FE-005 | Attendee selector per meal | Must |
| WKP-FE-006 | Leftovers toggle with source meal picker | Should |
| WKP-FE-007 | Frozen meal picker from Freezy | Should |
| WKP-FE-008 | Week navigation (prev/next/archive) | Should |
| WKP-FE-009 | Planning hint chips per meal slot | Should |

### UI Components

- `WeekCalendarComponent` — main weekly grid
- `MealSlotComponent` — individual day+meal cell
- `RecipePickerDialog` — browse and select recipes
- `AttendeeSelectorComponent` — checkboxes for family members
- `VagueInstructionInputComponent` — text input with hint chips
- `PlanningHintsComponent` — add/remove planning constraints
- `WeekNavigationComponent` — prev/next/archive controls

## 7. Integration Requirements

| ID | Integration | Direction | Notes |
|----|------------|-----------|-------|
| WKP-INT-001 | Family Composition | Inbound | Needs active members and constraints |
| WKP-INT-002 | Recipe Store | Inbound | Browse and assign recipes |
| WKP-INT-003 | Freezy Integration | Inbound | Frozen meals available for selection |
| WKP-INT-004 | Shopping List | Outbound | Planned meals generate shopping list |
| WKP-INT-005 | Past Dishes | Outbound | Archived plans feed history |

## 8. Risks / Unknowns

| ID | Risk | Mitigation |
|----|------|------------|
| WKP-R001 | Vague instruction interpretation is complex | Defer AI interpretation; store hints as metadata for now |
| WKP-R002 | Constraint validation across all family members may be slow | Cache household constraints; validate on save |
| WKP-R003 | Drag-and-drop UX complexity | Use Angular CDK drag-drop; simple implementation first |

## 9. Phased Implementation Plan

### Phase 1 — Basic Week Planning
- [ ] Database migration for WeekPlan, PlannedMeal, MealAttendee
- [ ] Backend CRUD for WeekPlan
- [ ] Backend CRUD for PlannedMeal
- [ ] Frontend weekly calendar grid
- [ ] Assign recipe to meal slot
- [ ] Recipe picker dialog

### Phase 2 — Vague Instructions & Hints
- [ ] Database migration for PlanningHint
- [ ] Vague instruction input
- [ ] Planning hint chips
- [ ] Hint backend support

### Phase 3 — Attendees & Leftovers
- [ ] Attendee selection per meal
- [ ] Leftovers toggle and source selection
- [ ] Constraint validation on save

### Phase 4 — Freezy Integration & Archival
- [ ] Frozen meal picker
- [ ] Week archival
- [ ] Past weeks browsing

### Phase 5 — AI Extension Points
- [ ] AiPlanningRequest model
- [ ] Suggest endpoint stub
- [ ] AI service interface definition

## 10. Implementation Checklist

### Infrastructure
- [ ] Database migration created and applied
- [ ] Entity models defined
- [ ] DTOs defined
- [ ] Validators defined

### Backend
- [ ] WeekPlanController
- [ ] WeekPlanService
- [ ] PlannedMealController
- [ ] PlannedMealService
- [ ] Constraint validation service
- [ ] AI planning interface (stub)
- [ ] Unit tests
- [ ] Integration tests

### Frontend
- [ ] Week calendar grid component
- [ ] Meal slot component
- [ ] Recipe picker dialog
- [ ] Attendee selector
- [ ] Vague instruction input
- [ ] Planning hints component
- [ ] Week navigation
- [ ] Service/HTTP client

### Testing
- [ ] Unit tests for domain logic
- [ ] Integration tests for API
- [ ] E2E tests for planning flow
