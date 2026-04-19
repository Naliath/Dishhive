# Feature: Family Composition

**Created:** April 19, 2026  
**Status:** ✅ Implemented

---

## Feature Goal

Allow a household to model its family composition: who lives there, their food preferences, allergies, constraints, and favorite dishes. This information feeds directly into week planning, shopping list generation, and statistics.

---

## Scope

### In Scope
- Household members (permanent residents)
- Guest support (temporary attendees)
- Personal food preferences per member
- Allergies and intolerances per member
- Dietary constraints (vegetarian, vegan, halal, gluten-free, etc.)
- Favorite dishes per member
- Member presence per planned meal

### Out of Scope (for this phase)
- Authentication or user accounts (no login system)
- Sharing household data between devices (single-device assumption initially)
- Age/nutritional profile tracking

---

## User Stories

1. **As a household manager**, I want to add each family member by name so I can personalize meal planning.
2. **As a household manager**, I want to record allergies and intolerances per member so the planner can flag incompatible recipes.
3. **As a household manager**, I want to mark favorite dishes per member so the planner can prioritize them.
4. **As a household manager**, I want to add guests for a specific period so meals can be planned accordingly.
5. **As a meal planner**, I want to select which members are present for a meal so the shopping list reflects the right quantities.

---

## Domain Model Considerations

```
FamilyMember
  - Id: Guid
  - Name: string
  - IsGuest: bool
  - GuestFrom: DateTime?
  - GuestUntil: DateTime?
  - CreatedAt: DateTime
  - UpdatedAt: DateTime

MemberPreference
  - Id: Guid
  - FamilyMemberId: Guid (FK → FamilyMember)
  - PreferenceType: enum (Allergy, Intolerance, DietaryConstraint, Dislike, Preference)
  - Value: string  (e.g., "peanuts", "gluten", "vegetarian", "fish")
  - Notes: string?
  - CreatedAt: DateTime

FavoriteDish
  - Id: Guid
  - FamilyMemberId: Guid (FK → FamilyMember)
  - RecipeId: Guid? (FK → Recipe, nullable for free-text dishes)
  - DishName: string  (free text for dishes without a stored recipe)
  - CreatedAt: DateTime
```

---

## Backend Requirements

- `FamilyController` with endpoints:
  - `GET /api/family` — list all members (including active guests)
  - `GET /api/family/{id}` — get member with preferences and favorites
  - `POST /api/family` — create member
  - `PUT /api/family/{id}` — update member
  - `DELETE /api/family/{id}` — delete member
  - `GET /api/family/{id}/preferences` — list preferences
  - `POST /api/family/{id}/preferences` — add preference
  - `DELETE /api/family/{id}/preferences/{prefId}` — remove preference
  - `GET /api/family/{id}/favorites` — list favorites
  - `POST /api/family/{id}/favorites` — add favorite
  - `DELETE /api/family/{id}/favorites/{favId}` — remove favorite

---

## Frontend Requirements

- Family management page at `/family`
- Member list with preference badges (allergy indicators, diet icons)
- Add/edit member form
- Preference management per member (tags/chips UI)
- Guest toggle with date range picker
- Favorites list per member with recipe link if available

---

## Integration Requirements

- Family member presence is referenced from `PlannedMeal.AttendeeIds[]`
- Allergy/intolerance data must be accessible during recipe import to flag warnings

---

## Risks / Unknowns

- Preference model must be flexible enough to cover allergies, intolerances, dietary constraints, and dislikes without separate tables for each — using the `PreferenceType` enum is the chosen approach.
- Guest date ranges need validation (GuestFrom < GuestUntil).

---

## Phased Implementation Plan

### Phase 1 — Core Member CRUD
- [ ] `FamilyMember` entity and EF config
- [ ] `FamilyController` CRUD endpoints
- [ ] `FamilyMemberDtos.cs`
- [ ] Integration tests for CRUD
- [ ] Angular family page (list + add/edit form)
- [ ] `FamilyService` in Angular

### Phase 2 — Preferences & Favorites
- [ ] `MemberPreference` entity
- [ ] `FavoriteDish` entity
- [ ] Preference endpoints
- [ ] Favorite endpoints
- [ ] Angular preference chips UI
- [ ] Angular favorites list

### Phase 3 — Guest Support
- [ ] Guest flag and date range on `FamilyMember`
- [ ] Guest filtering in list endpoint (active/inactive)
- [ ] Angular guest toggle + date range picker

---

## Implementation Checklist

### Backend
- [x] Feature plan created
- [x] `FamilyMember` entity created
- [x] `MemberPreference` entity created
- [x] `FavoriteDish` entity created
- [x] EF Core configuration added to `DishhiveDbContext`
- [x] `FamilyController` created (CRUD)
- [x] `FamilyMemberDtos.cs` created
- [x] Integration tests for family CRUD (14 tests)

### Frontend
- [x] `family-member.model.ts` created
- [x] `FamilyService` created
- [x] Family page component created
- [x] Member add/edit form (`AddMemberDialog`) created
- [x] Member detail page with preference chip UI created
- [x] Favorites list per member created
