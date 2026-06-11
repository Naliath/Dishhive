# Feature: Family Composition

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [week-planner.md](week-planner.md), [past-dishes-and-statistics.md](past-dishes-and-statistics.md)

## Feature Goal

Model the household so meal planning can respect who is eating: household members, their
preferences, allergies/intolerances, dietary constraints, favorite dishes, and temporary guests.

## Scope

**In scope**
- CRUD for household members
- Per-member allergies/intolerances and dietary constraints (e.g. vegetarian, no pork)
- Per-member free-text preference notes (likes/dislikes)
- Favorite dishes per member (link to a recipe, or free-text dish name)
- Guests: members flagged as temporary attendees, selectable per meal

**Out of scope**
- User accounts / authentication (members are data, not logins)
- Nutritional profiles, calorie targets
- Automatic constraint enforcement in the planner (the planner *shows* constraints; hard
  filtering is a future AI-planning concern)

## User Stories / Use Cases

1. As a planner, I add the people in my household once, so every week plan knows who eats.
2. As a planner, I record that a member is allergic to nuts so I'm warned when planning.
3. As a planner, I record dietary constraints (vegetarian, halal, no fish) per member.
4. As a planner, I mark favorite dishes per member so the planner can suggest crowd-pleasers.
5. As a planner, I add "Grandma" as a guest and include her only on Sunday's dinner.
6. As a planner, I deactivate a member who moved out without losing meal history.

## Domain Model Considerations

```
FamilyMember
├── Id (Guid)
├── Name (string, required, max 100)
├── IsGuest (bool, default false)        // temporary attendee vs. household member
├── DietaryTags (n:m via FamilyMemberDietaryTags)  // structured allergy/diet tags, see dietary-tags.md
├── PreferenceNotes (string?, max 1000)  // likes/dislikes free text
├── IsActive (bool, default true)        // soft delete; preserves meal history
├── CreatedAt / UpdatedAt

FamilyMemberFavorite
├── Id (Guid)
├── FamilyMemberId (FK, cascade)
├── RecipeId (Guid?, FK set-null)        // favorite known recipe…
├── DishName (string?, max 200)          // …or free-text dish; at least one required
```

- Allergies/constraints started as free text and were replaced by structured, reusable
  tags in June 2026 — see [dietary-tags.md](dietary-tags.md) for model and migration.
- Soft delete (`IsActive`) instead of hard delete so attendance history and statistics survive.
- Guests are full `FamilyMember` rows with `IsGuest = true`; identical capabilities, different
  default selection behavior in the planner.

## Backend Requirements

- `FamilyMembersController`: `GET/POST /api/familymembers`, `GET/PUT/DELETE /api/familymembers/{id}`
  (DELETE = soft delete when history exists, hard delete otherwise)
- Favorites managed as sub-resource: `GET/POST /api/familymembers/{id}/favorites`,
  `DELETE /api/familymembers/{id}/favorites/{favoriteId}`
- DTOs: `FamilyMemberDtos.cs` (Create/Update/Read), validation via DataAnnotations
- EF configuration with indexes on `IsActive`, `IsGuest`

## Frontend Requirements

- Page `pages/family/` — member list (cards, Material), add/edit form, guest badge,
  allergy/constraint chips
- Service `family-members.service.ts` using the generated API client
- Favorites editing inside the member detail/edit view (recipe autocomplete + free text)

## Integration Requirements

- Week planner reads members/guests for attendance selection and shows allergy warnings
- Statistics aggregates favorites and attendance per member
- No external integrations

## Risks / Unknowns

- Tags aren't matched against recipe ingredients (recipes carry no allergen data) —
  the planner shows warnings rather than enforcing rules; the AI suggestions treat
  allergy tags as hard constraints in the prompt.
- Favorite "dish" vs "recipe" duality (text vs link) must stay consistent with the planner's
  same duality.

## Phased Implementation Plan

**Phase 1 — Core members (vertical slice)**
- Entity + migration, CRUD API, family page with list/add/edit/deactivate

**Phase 2 — Favorites**
- `FamilyMemberFavorite` entity + endpoints, favorites UI in member detail

**Phase 3 — Planner hookup**
- Attendance selection + allergy hints in the week planner (tracked in week-planner.md)

## Implementation Checklist

- [x] `FamilyMember` entity + EF configuration + migration
- [x] `FamilyMembersController` CRUD + DTOs
- [x] Integration tests for member CRUD
- [x] `family-members.service.ts` + model
- [x] Family page: member list
- [x] Family page: add/edit form (allergies, constraints, notes, guest flag)
- [x] Soft-delete handling (deactivate with history)
- [x] `FamilyMemberFavorite` entity + endpoints (recipe link or free text; dish name
      denormalized from recipe title so favorites survive recipe deletion) + tests
- [x] Favorites UI (chips on member cards, add/remove in the edit form; free-text first,
      recipe-linking UI is future polish)
- [x] Allergy warning surface in planner (allergy hints for selected attendees in slot editor)
