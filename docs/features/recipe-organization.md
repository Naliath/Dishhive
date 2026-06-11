# Feature: Recipe Organization — Tags, Categories, Cookbooks

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [recipe-store.md](recipe-store.md), [dietary-tags.md](dietary-tags.md)

## Feature Goal

Keep a growing recipe library navigable: filterable tags and categories, plus curated
"cookbooks" — named, saved filters ("Italiaans", "Snelle doordeweekse dagen") that
re-apply with one tap.

## Model

- **`RecipeTag`** — `Name` (max 50, unique case-insensitive) + `RecipeTagAssignment`
  join table (recipe ↔ tag, composite key, cascade). Same implicit lifecycle as
  [dietary tags](dietary-tags.md): created when first assigned, removed when the last
  recipe drops them; `GET /api/recipetags` feeds autocomplete and the filter.
- **`Recipe.Category`** stays the free string the import pipeline writes
  (e.g. "Hoofdgerecht"); `GET /api/recipes/categories` lists the distinct values in use.
- **`Cookbook`** — `Name` (unique) + the saved filter: `SearchTerm?`, `Category?`,
  `Tags` (names, stored as `text[]` — deliberately *not* FKs, so a cookbook never keeps
  an unused tag alive; a vanished tag simply matches nothing).

## API

- `GET /api/recipes?search=&category=&tags=a,b` — filters combine; a recipe must carry
  **all** requested tags. List and detail DTOs carry `tags: string[]`; create/update
  accept the same list (max 20, names ≤50 chars, deduplicated case-insensitively).
- `GET /api/recipes/categories`, `GET /api/recipetags` — filter sources.
- `GET/POST/PUT/DELETE /api/cookbooks` — plain CRUD. Validation: unique name
  (case-insensitive) and at least one filter criterion. The cookbook itself never
  queries recipes; applying one happens client-side through the normal recipes filter.

## Frontend

- **Recipes page**: filter bar (search + category select + tag multi-select + clear);
  cookbook row with one chip per cookbook (tap to apply, tap again to clear, × deletes)
  and an inline "Save filter as cookbook…" input that appears when a manual filter is
  active. Manually changing the filter deselects the active cookbook. Cards show up to
  three tag badges.
- **Recipe form**: tag chip input (Enter/comma adds, autocomplete from the pool).
- **Recipe detail**: tag chips under the meta row.

## Risks / Notes

- Tags are user-curated; import does **not** auto-convert its `Keywords` field into
  tags (kept separate to avoid tag-pool spam from scraped keyword lists).
- Tag filtering is AND-based; OR semantics can be added later if cookbooks need them.

## Implementation Checklist

- [x] `RecipeTag` + `RecipeTagAssignment` + `Cookbook` entities, DbContext config
- [x] `AddRecipeOrganization` migration (additive)
- [x] Recipe DTOs/endpoints with tag sync + orphan cleanup; category/tag filters
- [x] `GET /api/recipes/categories`, `GET /api/recipetags`, cookbooks CRUD
- [x] Integration tests (tag lifecycle, filters, cookbook CRUD + validation)
- [x] Recipe form tag chips, detail/card tag display
- [x] Filter bar + cookbook row on the recipes page
