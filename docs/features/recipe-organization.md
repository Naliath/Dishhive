# Feature: Recipe Organization — Tags, Categories, Collections

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [recipe-store.md](recipe-store.md), [dietary-tags.md](dietary-tags.md),
[ai-week-planning.md](ai-week-planning.md) (#[Name] collection references)

## Feature Goal

Keep a growing recipe library navigable: filterable tags and categories, plus
**collections** — named, explicitly curated recipe sets ("Easy Weekday Dishes",
"Comfort Food"). A recipe can be in any number of collections, and collections are
referenceable from planning instructions as `#[Name]`.

> **History (June 2026):** cookbooks were originally *saved filters* (search + category
> + tags). That allowed no real curation — grouping required contorting tags or filter
> names — so they were replaced by explicit-membership collections. The old filter rows
> were dropped in the `AddCollectionMembership` migration (no conversion; decided
> deliberately). Dynamic slicing stays with tags, search and category. The entity keeps
> the `Cookbook` name in code/DB; the UI says "Collections".

## Model

- **`RecipeTag`** — `Name` (max 50, unique case-insensitive) + `RecipeTagAssignment`
  join table (recipe ↔ tag, composite key, cascade). Same implicit lifecycle as
  [dietary tags](dietary-tags.md): created when first assigned, removed when the last
  recipe drops them; `GET /api/recipetags` feeds autocomplete and the filter.
- **`Recipe.Category`** stays the free string the import pipeline writes
  (e.g. "Hoofdgerecht"); `GET /api/recipes/categories` lists the distinct values in use.
- **`Cookbook`** (collection) — `Name` (unique case-insensitive, max 100, **no square
  brackets** — they delimit the `#[Name]` mention syntax) + `CookbookEntry` join table
  (cookbook ↔ recipe, composite key, `AddedAt`). Deleting a recipe removes its
  memberships explicitly (same provider-agnostic delete pattern as tags).
- **Auto collections** — computed, read-only sets that appear alongside manual ones:
  *Top rated* (avg rating ≥ 4, ≥ 2 ratings), *Quick (max 30 min)*, *Recently added*
  (last 30 days), *{Member}'s favorites*. No DB rows (`AutoCollectionProvider` builds
  queries on read); slug ids (`auto-quick`, `auto-fav-{memberId}`). Each can be
  individually enabled/disabled from the settings page (disabled ids stored in the
  `autoCollections.disabled` UserSetting); a disabled one drops out of the cookbooks
  list, the recipe filter and `#[Name]` resolution, but its name stays reserved so
  re-enabling never collides with a manual collection.

## API

- `GET /api/recipes?search=&category=&tags=a,b&cookbookId=` — filters combine; a recipe
  must carry **all** requested tags; `cookbookId` is a manual Guid or an auto slug.
- `GET /api/cookbooks` — manual collections (with `recipeCount`) + *enabled* auto
  collections (`kind: "manual" | "auto"`).
- `GET /api/cookbooks/auto-collections` — all auto collections with `enabled` state and
  counts (settings management); `PUT /api/cookbooks/auto-collections/{id}` `{enabled}`.
- `POST/PUT/DELETE /api/cookbooks/{id}` — manual CRUD (name only; may be empty).
- `GET /api/cookbooks/{id}/recipes` — members (works for auto slugs too).
- `POST /api/cookbooks/{id}/recipes` `{recipeIds[]}` — bulk add, idempotent;
  `DELETE /api/cookbooks/{id}/recipes/{recipeId}` removes one membership.
- `PUT /api/recipes/{id}/cookbooks` `{cookbookIds[]}` — recipe-side full sync; the
  recipe detail DTO carries `cookbookIds`.
- Export/import: memberships travel as a `dishhive:collections` extension property
  (see [recipe-import-export.md](recipe-import-export.md)); import recreates
  collections by name. Auto collections are never exported.

## Frontend

- **Recipes page**: filter bar (search + category + tags + clear) combinable with the
  collections row — one chip per collection (manual `bookmark`, auto `auto_awesome` with
  dashed border; tap to view, tap again to clear, × deletes manual ones) and an inline
  "New collection…" input. Recipe cards get a hover bookmark menu: add to any manual
  collection, or remove from the currently viewed one.
- **Recipe detail**: collection chips next to the tags (tap to remove) plus an
  "Add to collection" menu.
- **Meal slot dialog**: "surprise me" chips — tap a collection to fill the slot with a
  random member, avoiding recipes already planned this week. Works without AI.
- **Recipe form**: tag chip input (Enter/comma adds, autocomplete from the pool).
- **Settings page**: an "Automatic collections" card with a toggle per auto collection
  (and its current member count).

## Risks / Notes

- Tags are user-curated; import does **not** auto-convert its `Keywords` field into
  tags (kept separate to avoid tag-pool spam from scraped keyword lists).
- Tag filtering is AND-based; OR semantics can be added later if needed.
- `#[Name]` references are by name: renaming a collection dangles references in
  existing planning texts (they degrade to plain-text hints; see ai-week-planning.md).

## Implementation Checklist

- [x] `RecipeTag` + `RecipeTagAssignment` entities, DbContext config
- [x] Recipe DTOs/endpoints with tag sync + orphan cleanup; category/tag filters
- [x] `Cookbook` + `CookbookEntry` explicit membership, `AddCollectionMembership`
      migration (drops the old saved-filter rows and columns)
- [x] Collections CRUD + membership endpoints + `cookbookId` recipe filter
- [x] Auto collections (`AutoCollectionProvider`) in list/detail/filter/mentions
- [x] Demo seed collections (Easy Weekday Dishes, Comfort Food, Feestelijk)
- [x] Collections row + card menus on the recipes page; detail chips + menu
- [x] Random "surprise me from collection" in the meal slot dialog
- [x] `dishhive:collections` in export/import
- [x] Integration tests (membership CRUD, sync, cascade, filters, auto collections,
      exchange round-trip) + `DemoDataTests`
