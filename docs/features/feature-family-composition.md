# Feature: Family Composition

> **Feature ID**: FAM-001
> **Status**: Planned
> **Priority**: High
> **Depends on**: Infrastructure setup
> **Last Updated**: 2026-06-07

## 1. Feature Goal

Provide the ability to define and manage household members, their dietary preferences, allergies, intolerances, and favorite dishes. This forms the foundation for personalized meal planning.

## 2. Scope

### In Scope (v1)
- Add/edit/delete household members
- Configure personal preferences per member
- Track allergies and intolerances
- Track dietary constraints (vegetarian, vegan, gluten-free, etc.)
- Mark favorite dishes per family member
- Add temporary guests for specific weeks

### Out of Scope (v1)
- User authentication per family member
- Photo profiles
- Calorie/nutrition tracking
- Integration with health apps

## 3. User Stories / Use Cases

| ID | Story | Priority |
|----|-------|----------|
| FAM-US-001 | As a user, I want to add family members so the system knows who to plan meals for | Must |
| FAM-US-002 | As a user, I want to specify allergies for each member so the planner avoids dangerous ingredients | Must |
| FAM-US-003 | As a user, I want to set dietary preferences so meals match what each person eats | Must |
| FAM-US-004 | As a user, I want to mark favorite dishes so the planner can suggest them | Should |
| FAM-US-005 | As a user, I want to add temporary guests for a specific week | Should |
| FAM-US-006 | As a user, I want to remove a family member who no longer lives with us | Could |
| FAM-US-007 | As a user, I want to see a summary of all family member restrictions at a glance | Could |

## 4. Domain Model

```
FamilyMember
├── Id: UUID
├── Name: string
├── DisplayName: string (optional, for nicknames)
├── IsActive: bool
├── IsGuest: bool (temporary attendee)
├── CreatedAt: DateTime
├── UpdatedAt: DateTime
├── Allergies: Collection<Allergy>
├── DietaryConstraints: Collection<DietaryConstraint>
├── FavoriteDishes: Collection<FavoriteDish>
└── Preferences: FamilyMemberPreference

Allergy
├── Id: UUID
├── FamilyMemberId: UUID (FK)
├── IngredientName: string
├── Severity: enum (Mild, Moderate, Severe)
└── Notes: string?

DietaryConstraint
├── Id: UUID
├── FamilyMemberId: UUID (FK)
├── ConstraintType: enum (Vegetarian, Vegan, GlutenFree, DairyFree, NutFree, Halal, Kosher, Other)
├── Description: string?
└── IsActive: bool

FavoriteDish
├── Id: UUID
├── FamilyMemberId: UUID (FK)
├── RecipeId: UUID (FK to Recipe)
├── Rating: int (1-5)
└── TimesEnjoyed: int

FamilyMemberPreference
├── Id: UUID
├── FamilyMemberId: UUID (FK)
├── Key: string
├── Value: string
└── Category: string (e.g., "cuisine", "cooking_time")
```

## 5. Backend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| FAM-BE-001 | CRUD API for FamilyMember | Must |
| FAM-BE-002 | CRUD API for Allergy | Must |
| FAM-BE-003 | CRUD API for DietaryConstraint | Must |
| FAM-BE-004 | CRUD API for FavoriteDish | Should |
| FAM-BE-005 | GET all active family members with their constraints | Must |
| FAM-BE-006 | Aggregate endpoint: get all allergies/constraints for current household | Must |
| FAM-BE-007 | Soft delete for family members | Should |
| FAM-BE-008 | Validation: no duplicate active family members with same name | Should |

### API Endpoints

```
GET    /api/family-members
POST   /api/family-members
GET    /api/family-members/{id}
PUT    /api/family-members/{id}
DELETE /api/family-members/{id}

GET    /api/family-members/{id}/allergies
POST   /api/family-members/{id}/allergies
PUT    /api/family-members/{id}/allergies/{allergyId}
DELETE /api/family-members/{id}/allergies/{allergyId}

GET    /api/family-members/{id}/dietary-constraints
POST   /api/family-members/{id}/dietary-constraints
PUT    /api/family-members/{id}/dietary-constraints/{constraintId}
DELETE /api/family-members/{id}/dietary-constraints/{constraintId}

GET    /api/family-members/{id}/favorite-dishes
POST   /api/family-members/{id}/favorite-dishes
PUT    /api/family-members/{id}/favorite-dishes/{dishId}
DELETE /api/family-members/{id}/favorite-dishes/{dishId}

GET    /api/family-members/active-with-constraints
```

## 6. Frontend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| FAM-FE-001 | Family members list page | Must |
| FAM-FE-002 | Add family member dialog/form | Must |
| FAM-FE-003 | Edit family member dialog/form | Must |
| FAM-FE-004 | Allergy management UI | Must |
| FAM-FE-005 | Dietary constraint selection UI | Must |
| FAM-FE-006 | Favorite dishes picker (linked to Recipe store) | Should |
| FAM-FE-007 | Household summary card/widget | Could |

### UI Components

- `FamilyMemberListComponent` — list all members with status indicators
- `FamilyMemberFormDialog` — add/edit member with inline allergy/constraint sections
- `AllergyChipComponent` — display allergy with severity color coding
- `DietaryConstraintChipComponent` — display dietary constraints
- `HouseholdSummaryComponent` — compact overview of all restrictions

## 7. Integration Requirements

| ID | Integration | Direction | Notes |
|----|------------|-----------|-------|
| FAM-INT-001 | Week Planner | Outbound | Planner needs active members and their constraints |
| FAM-INT-002 | Recipe Store | Outbound | Favorite dishes link to recipes |
| FAM-INT-003 | Shopping List | Outbound | Dietary constraints filter ingredients |

## 8. Risks / Unknowns

| ID | Risk | Mitigation |
|----|------|------------|
| FAM-R001 | Allergy data is safety-critical; data loss could be dangerous | Regular backups, soft deletes, audit logging |
| FAM-R002 | Dietary constraint enforcement across all planned meals | Central constraint service used by planner |
| FAM-R003 | Guest management complexity vs. value | Keep guest model simple; just name + constraints |

## 9. Phased Implementation Plan

### Phase 1 — Core Member Management
- [ ] Database migration for FamilyMember, Allergy, DietaryConstraint
- [ ] Backend CRUD endpoints for FamilyMember
- [ ] Backend CRUD endpoints for Allergy
- [ ] Backend CRUD endpoints for DietaryConstraint
- [ ] Frontend family members list page
- [ ] Frontend add/edit dialog

### Phase 2 — Favorites & Preferences
- [ ] Database migration for FavoriteDish, FamilyMemberPreference
- [ ] Backend CRUD endpoints for FavoriteDish
- [ ] Frontend favorite dishes picker
- [ ] Aggregate endpoint for household constraints

### Phase 3 — Guest Support & Polish
- [ ] Guest/attendee support
- [ ] Household summary widget
- [ ] Soft delete implementation
- [ ] Validation rules

## 10. Implementation Checklist

### Infrastructure
- [ ] Database migration created and applied
- [ ] Entity models defined
- [ ] DTOs defined
- [ ] Validators defined

### Backend
- [ ] FamilyMemberController
- [ ] FamilyMemberService
- [ ] Allergy CRUD endpoints
- [ ] DietaryConstraint CRUD endpoints
- [ ] FavoriteDish CRUD endpoints
- [ ] Aggregate constraints endpoint
- [ ] Unit tests for service layer
- [ ] Integration tests for API endpoints

### Frontend
- [ ] Family members list component
- [ ] Add/edit dialog component
- [ ] Allergy management UI
- [ ] Dietary constraint UI
- [ ] Favorite dishes UI
- [ ] Household summary component
- [ ] Service/HTTP client for family data

### Testing
- [ ] Unit tests for domain logic
- [ ] Integration tests for API
- [ ] E2E tests for critical flows
