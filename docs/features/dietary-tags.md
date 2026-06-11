# Feature: Structured Dietary Tags

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [family-composition.md](family-composition.md), [ai-week-planning.md](ai-week-planning.md), [week-planner.md](week-planner.md)

## Feature Goal

Replace the free-text `Allergies` / `DietaryConstraints` fields on family members with
reusable, structured tags. Tags make dietary data filterable and consistent across
members ("Lactose" is one tag, not five spelling variants), which the AI week
suggestions and planner hints build on.

## Model

- **`DietaryTag`** — `Name` (max 50) + `Kind` (`Allergy` = hard "must not contain",
  `Diet` = lifestyle constraint like vegetarian/no pork). One tag per name per kind;
  name matching is case-insensitive in the API.
- **`FamilyMemberDietaryTag`** — composite-key join table (member ↔ tag), cascade on
  both sides.
- `FamilyMember.PreferenceNotes` stays free text — likes/dislikes are prose, not tags.

## Tag Lifecycle (implicit management)

Tags have no CRUD of their own; the pool always reflects real usage:

- **Created** on the fly when a member is saved with a tag name that doesn't exist yet
  (per kind, case-insensitive — "noten" reuses "Noten" and keeps its original casing).
- **Removed** automatically when the last member using a tag drops it (orphan cleanup
  on member update/delete).
- `GET /api/dietarytags` exposes the pool for the autocomplete in the family form.

## API

- `FamilyMemberDto` carries `allergyTags: string[]` and `dietTags: string[]`
  (replacing the former `allergies`/`dietaryConstraints` strings); create/update DTOs
  accept the same lists (max 20 tags each, names max 50 chars, deduplicated
  case-insensitively within a request).
- Consumers: the AI suggestion prompt lists tags per member ("allergies: Shellfish;
  constraints: Vegetarian"), the meal-slot dialog shows attendee tags as planning hints.

## Migration of Existing Data

`AddDietaryTags` converts before dropping the old columns: comma-separated free-text
values become individual tags (trimmed, deduplicated case-insensitively across members,
capped at 50 chars) with member links; `Down()` restores the free-text columns by
re-joining tag names. Verified against a seeded old-schema database.

## Frontend

- Family form: chip inputs (Enter/comma to add) with autocomplete from the shared tag
  pool, separate fields for allergies and diets.
- Member cards: colored tag chips (red-tinted allergy chips with warning icon,
  blue-tinted diet chips).
- Meal-slot dialog: attendee hint line shows allergy *and* diet tags.

## Implementation Checklist

- [x] `DietaryTag` + `FamilyMemberDietaryTag` entities, DbContext config, unique (Name, Kind)
- [x] `AddDietaryTags` migration with free-text → tags data conversion (and reverse in Down)
- [x] Member DTOs/endpoints on tag lists; find-or-create sync + orphan cleanup
- [x] `GET /api/dietarytags` for autocomplete
- [x] AI prompt + demo data on structured tags
- [x] Family page chip editing with autocomplete; tag chips on member cards
- [x] Integration tests: reuse, kind separation, orphan cleanup, dedupe, validation
