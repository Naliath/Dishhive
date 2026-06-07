# Feature: Family Composition

## Goal
Track who lives in (or visits) the household so the planner can pick meals that work for everyone present at a given meal.

## Scope
- Permanent household members (`FamilyMember`).
- Guests / temporary attendees that recur but don't live in the household (`Guest`).
- Per-member preferences: allergies, intolerances, dietary constraints, dislikes, favorite dishes.
- Designating attendees per planned meal (cross-feature with the week planner).

## Out of scope (v1)
- Authentication / per-person login (single-household app).
- Sharing / external invitations.
- Per-member calorie or nutrition tracking.

## User stories
- *As the cook*, I list everyone who lives here so the planner knows the default attendees.
- *As the cook*, I record that "Sam is lactose intolerant" so dishes with milk are flagged.
- *As the cook*, I add "Grandma" as a recurring guest so I can mark her present for Sunday lunches.
- *As the cook*, I mark a dish as a favorite of "Sam" so the planner can suggest it more often.

## Domain model considerations
```
FamilyMember
  Id (Guid)
  DisplayName (string, required)
  Notes (string?, freeform)
  CreatedAt / UpdatedAt

Guest
  Id (Guid)
  DisplayName (string, required)
  Notes (string?)
  CreatedAt / UpdatedAt

FamilyMemberPreference
  Id (Guid)
  FamilyMemberId (FK)
  Kind (enum: Allergy, Intolerance, Dietary, Dislike, Favorite)
  Value (string, e.g. "lactose", "vegetarian", "olives")
  RecipeId (Guid?, optional — set when Kind=Favorite and the favorite is a known recipe)
```
- A favorite either refers to a known `Recipe` (FK) or just stores a free-text dish name (when no recipe exists yet).
- `Guest.Preferences` reuses the same `FamilyMemberPreference` shape via a generic owner pattern — but for v1 keep it simple: only `FamilyMember` has preferences. Guests get a freeform `Notes` field.

## Backend requirements
- Controllers: `FamilyMembersController`, `GuestsController`.
- Endpoints:
  - `GET/POST/PUT/DELETE /api/family-members`
  - `GET/POST/PUT/DELETE /api/family-members/{id}/preferences`
  - `GET/POST/PUT/DELETE /api/guests`
- DTOs in `FamilyDtos.cs`.
- EF entities under `Models/Family/`.

## Frontend requirements
- Page: `pages/family/`
- Components: `family-member-list`, `family-member-form`, `preference-list-editor`, `guest-list`.
- Use Material `mat-list`, `mat-chip-set` (for preferences), `mat-form-field`.

## Integration requirements
- Week planner pulls the active `FamilyMember`s as default attendees per meal slot.
- Recipe display warns when a recipe contains an ingredient flagged as an allergy/intolerance for any default attendee.

## Risks / unknowns
- Preference matching against ingredients is a fuzzy problem. v1 does case-insensitive substring matching — good enough to be useful, not perfect.

## Phased plan
1. Entities + migrations.
2. CRUD endpoints + DTOs.
3. Angular page with member list + edit dialog.
4. Preferences editor (chips).
5. Guest list management.
6. Hook into planner attendee picker.
7. Allergy warning surface in recipe view.

## Implementation checklist
- [x] Entities (`FamilyMember`, `Guest`, `FamilyMemberPreference`)
- [x] DbContext mappings
- [x] DTOs
- [x] `FamilyMembersController`
- [x] `GuestsController`
- [x] Angular `FamilyService`
- [x] Angular `FamilyPage` + components
- [x] Preferences editor
- [x] Default-attendee integration with planner
- [ ] Allergy warning on recipe view
