# Feature: Recipe Store

## Goal
Manage the household's collection of recipes — manually entered or imported — with enough metadata for planning and shopping-list generation.

## Scope
- Recipe CRUD.
- Ingredients (with quantity, unit, optional note, optional original-source values for imported recipes).
- Step-by-step instructions.
- Tags (e.g. `fish`, `quick`, `vegetarian`).
- Optional media: hero picture URL, video URL, source URL.
- Servings / "intended number of people".
- Source-specific raw payload kept for imported recipes (JSONB) so users can re-parse / manually correct.

## Out of scope (v1)
- Versioning / change history of a recipe.
- Per-recipe nutrition.
- Recipe sharing / publish.

## Domain model
```
Recipe
  Id (Guid)
  Title (string, required)
  Description (string?)
  Servings (int, required, default 4)
  ImageUrl (string?)
  VideoUrl (string?)
  SourceUrl (string?)
  SourceProviderKey (string?)   ← e.g. "dagelijksekost"
  SourceRawPayload (jsonb, string?) — preserved for imported recipes
  Notes (string?)
  CreatedAt / UpdatedAt

RecipeIngredient
  Id (Guid)
  RecipeId (FK)
  Order (int)
  Name (string, required)
  Quantity (decimal?)
  Unit (string?)              ← canonical Dishhive unit (metric by default)
  OriginalQuantity (decimal?) ← preserved from source
  OriginalUnit (string?)      ← preserved from source
  Note (string?)              ← e.g. "finely chopped"
  Section (string?)           ← e.g. "Sauce", "Garnish"

RecipeStep
  Id (Guid)
  RecipeId (FK)
  Order (int)
  Text (string, required)

RecipeTag
  Id (Guid)
  RecipeId (FK)
  Tag (string)
```

## Backend requirements
- Controller: `RecipesController` with full CRUD.
- Endpoints:
  - `GET /api/recipes?search=...&tag=...`
  - `GET /api/recipes/{id}`
  - `POST/PUT/DELETE /api/recipes`
- DTOs in `RecipeDtos.cs`.
- Free-text search via `ILIKE` in v1 (good enough for personal scale).

## Frontend requirements
- `pages/recipes/` (list with search) + `pages/recipes/<id>` (detail) + `pages/recipes/<id>/edit`.
- Manual recipe form with ingredient + step rows (`mat-list`, `mat-form-field`).

## Integration requirements
- Recipe import (`recipe-import` feature) writes recipes via this module.
- Week planner reads recipes for slot assignment.
- Shopping list reads ingredients.

## Risks / unknowns
- Ingredient text quality for shopping-list generation depends on consistent units. Mitigated by storing both normalized and original, and offering manual correction.

## Phased plan
1. Entities + migrations.
2. Read endpoints.
3. Write endpoints.
4. Angular recipe service.
5. List + detail pages.
6. Edit page.

## Implementation checklist
- [x] Entities (`Recipe`, `RecipeIngredient`, `RecipeStep`, `RecipeTag`)
- [x] DbContext mappings
- [x] DTOs
- [x] `RecipesController`
- [x] Angular `RecipesService`
- [x] Recipe list page
- [x] Recipe detail page
- [x] Recipe edit form
