# Feature: Structured Dietary Tags

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [family-composition.md](family-composition.md), [dietary-facts.md](dietary-facts.md), [ai-week-planning.md](ai-week-planning.md), [week-planner.md](week-planner.md)

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
  both sides. Since July 2026 the link carries **`ExcludedClasses`** (CSV of
  `IngredientClass` names): the tag stays a shared label, but its machine-checkable
  meaning is **per member** — one member's "Vegetarian" excludes fish, another's
  doesn't. Seeded from built-in Dutch/English presets (`DietaryTagPresets`) when a
  link is created without explicit classes; the migration seeded existing links the
  same way. An empty set = not machine-checkable (prompt-only, like all tags were
  before) — the deliberate fate of certification diets like kosher. See
  [dietary-facts.md](dietary-facts.md) for the recipe side and the matching.
- `FamilyMember.PreferenceNotes` stays free text — likes/dislikes are prose, not tags.

## Tag Lifecycle (implicit management)

Tags have no CRUD of their own; the pool always reflects real usage:

- **Created** on the fly when a member is saved with a tag name that doesn't exist yet
  (per kind, case-insensitive — "noten" reuses "Noten" and keeps its original casing).
- **Removed** automatically when the last member using a tag drops it (orphan cleanup
  on member update/delete).
- `GET /api/dietarytags` exposes the pool for the autocomplete in the family form.

## API

- `FamilyMemberDto` carries `allergyTags` / `dietTags` as **entries**
  `{ name, excludedClasses: string[] | null }` (max 20 each, names max 50 chars,
  deduplicated case-insensitively). In requests, `excludedClasses: null` means "no
  explicit definition": new links are preset-seeded, existing links keep their stored
  set — so a save that only edited something else never wipes a definition. Unknown
  class names → 400. `GET /api/dietarytags/preset?name=&kind=` previews the preset
  for a not-yet-saved tag.
- Consumers: the AI suggestion prompt lists tags per member with their classes
  ("allergies: Noten [excludes: TreeNuts]"), the meal-slot dialog shows attendee tag
  names as planning hints, and the planner matches classes against recipe facts
  (see [dietary-facts.md](dietary-facts.md)).

## Frontend

- Family form: chip inputs (Enter/comma to add) with autocomplete from the shared tag
  pool, separate fields for allergies and diets. Tapping a chip opens the per-member
  definition editor (grouped class checkboxes, preset-previewed before saving; hint
  when a tag is not machine-checkable).
- Member cards: colored tag chips (red-tinted allergy chips with warning icon,
  blue-tinted diet chips); tooltip shows the excluded classes.
- Meal-slot dialog: attendee hint line shows allergy *and* diet tags.

## Implementation Checklist

- [x] `DietaryTag` + `FamilyMemberDietaryTag` entities, DbContext config, unique (Name, Kind)
- [x] `DietaryTag` / `FamilyMemberDietaryTag` schema in the EF migration
- [x] Member DTOs/endpoints on tag lists; find-or-create sync + orphan cleanup
- [x] `GET /api/dietarytags` for autocomplete
- [x] AI prompt + demo data on structured tags
- [x] Family page chip editing with autocomplete; tag chips on member cards
- [x] Integration tests: reuse, kind separation, orphan cleanup, dedupe, validation
