# Feature: Recipe Store

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [recipe-import.md](recipe-import.md), [week-planner.md](week-planner.md),
[shopping-list-export.md](shopping-list-export.md), [measurement-preferences.md](measurement-preferences.md)

## Feature Goal

A household recipe library: recipes with ingredients, instructions, and the metadata the
planner and shopping list need (servings, times, category, source). Recipes can be entered
manually or imported (see recipe-import.md).

## Scope

**In scope**
- Recipe CRUD (manual entry and edit of imported recipes)
- Ingredients with quantity/unit (normalized + original values, see measurement-preferences.md)
- Ordered preparation steps
- Metadata: servings, prep/cook/total time, category, keywords, locally stored image with
  optional source URL reference, video URL,
  source URL/provider, raw source payload (traceability)
- Search by title/keyword (planner autocomplete)

**Out of scope**
- Ingredient master data / pantry matching (future)
- Recipe scaling UI (model supports it: quantities + servings; UI later)
- Ratings, comments, cooking mode (see possible-features.md)

## User Stories / Use Cases

1. As a planner, I add my own recipe with ingredients and steps.
2. As a planner, I edit an imported recipe (fix a parsed quantity, rename, re-portion).
3. As a planner, I search recipes by title or keyword when filling a planner slot.
4. As a planner, I open a recipe from the week plan to cook from it.
5. As the shopping list, I read structured ingredients for all planned recipes.
6. As a recipe editor, I choose a local image or take a photo without first hosting it online.
7. As a recipe editor, I can provide an image URL while Dishhive downloads and thereafter
   serves its own local copy.

## Domain Model Considerations

```
Recipe
├── Id (Guid)
├── Title (string, required, max 300)
├── Description (string?, max 2000)
├── Servings (int, default 4)            // intended number of people
├── PrepTimeMinutes / CookTimeMinutes / TotalTimeMinutes (int?)
├── Category (string?, max 100)          // e.g. "Dessert"
├── Keywords (string?, max 500)          // comma-separated
├── ImageUrl (string?, max 1000)         // original source URL, kept for traceability
├── ImageData (bytea?)                   // image bytes downloaded at import time
├── ImageContentType (string?, max 100)  // MIME type of ImageData
├── VideoUrl (string?, max 1000)
├── SourceUrl (string?, max 1000)        // null for manual recipes
├── SourceProvider (string?, max 100)    // provider key, e.g. "dagelijkse-kost"
├── SourceRawData (jsonb?)               // original extracted payload for traceability
├── CreatedAt / UpdatedAt
├── Ingredients (ICollection<RecipeIngredient>)
└── Steps (ICollection<RecipeStep>)

RecipeIngredient
├── Id (Guid)
├── RecipeId (FK, cascade)
├── SortOrder (int)
├── Name (string, required, max 200)     // "blauwe bosbes en frambozen"
├── Quantity (decimal?)                  // normalized value (metric by default)
├── Unit (string?, max 50)               // normalized unit ("g", "ml", "piece", …)
├── OriginalText (string, max 300)       // verbatim source line — always preserved
├── OriginalQuantity (decimal?)          // parsed source value before conversion
├── OriginalUnit (string?, max 50)       // source unit before conversion

RecipeStep
├── Id (Guid)
├── RecipeId (FK, cascade)
├── StepNumber (int)
├── Instruction (string, required, max 2000)
```

- `OriginalText` is the safety net: parsing/conversion is best-effort, the verbatim line is
  always shown and editable (decision recorded in measurement-preferences.md).
- `SourceRawData` stored as PostgreSQL `jsonb` — enables re-parsing after extractor improvements
  without re-fetching.
- **Images are always rendered from local storage**: URL imports and manually entered image
  URLs are downloaded into `ImageData`; the original remote URL remains in `ImageUrl` only
  for traceability. DTO `imageUrl` values are either `GET /api/recipes/{id}/image` or null,
  never a remote host. A download failure remains non-fatal for recipe imports, but leaves the
  recipe without a displayed image until the source is retried or replaced.
- Local files and browser camera captures use `PUT /api/recipes/{id}/image`. They clear
  `ImageUrl`, because the file/device—not a remote URL—is now the source. Removing an image
  clears both the bytes and source reference.
- Every incoming image is validated, auto-oriented, reduced to fit within 1600 × 1600, and
  encoded as quality-82 WebP. Sources are capped at 15 MB and stored output at 5 MB. This
  keeps good recipe-card/detail quality while bounding database usage. Bytes-in-database plus
  a dedicated endpoint avoids Base64 in normal JSON responses. Video remains a URL.

## Backend Requirements

- `RecipesController`: `GET /api/recipes?search=`, `GET /api/recipes/{id}`, `POST`, `PUT`, `DELETE`
- Recipe images: `GET/PUT/DELETE /api/recipes/{id}/image`; create/update downloads a newly
  supplied remote image URL before committing and retains it as `imageSourceUrl` in detail DTOs
- `GET /api/recipes/ingredients` — distinct ingredient names in use (case variants
  collapsed), feeding the form autocomplete so spelling variants ("ei"/"eieren") converge
- Ingredients/steps replaced wholesale on update (simple, atomic; no per-line endpoints)
- DTOs: `RecipeDtos.cs` (list DTO is slim — id/title/image/servings/times — detail DTO is full)
- Indexes: `Title`, `SourceUrl` (unique when not null — prevents duplicate imports)

## Frontend Requirements

- Page `pages/recipes/` — searchable card grid (image, title, time, servings) with
  rating (★) and favorite-count (♥) badges from history/favorites; category/tag filter
  bar and cookbook chips (see [recipe-organization.md](recipe-organization.md)). Rating and
  favorite indicators sit over the image so the title area stays uncluttered. Recipes without
  an image use the Dishhive logo placeholder so every card keeps the same image area.
  Search is debounced (300 ms) and results keep the previous grid until they arrive;
  newly matching cards fade in instead of the page flashing a spinner
- Page `pages/recipe-detail/` — full view incl. original ingredient text toggle and
  a history widget (last planned, planned/eaten counts, rating button → shared rating
  dialog, see meal-feedback.md). Favorites show as heart chips only for members who
  favorited the dish, plus a quiet "mark as favorite" menu for the rest
- Page `pages/recipe-form/` — manual create/edit (dynamic ingredient + step rows);
  ingredient names autocomplete from the library's known ingredients; the form displays the
  current image, shows a prominent skeleton/empty state, and offers file, camera and a
  button-opened URL editor. A newly confirmed URL is previewed before save; the retained source
  is available from the image's info affordance, while removal is a quiet overlay action.
- `recipes.service.ts`, models

## Integration Requirements

- Planner: recipe search/autocomplete + title denormalization into `PlannedMeal.DishName`
- Shopping list: structured ingredient read
- Import: providers produce an `ImportedRecipe` that maps onto this model (recipe-import.md)

## Risks / Unknowns

- Ingredient parsing quality varies by source — mitigated by `OriginalText` + manual edit.
- Wholesale ingredient replacement on update loses per-line identity — acceptable (no
  external references to ingredient rows).
- Existing remote-only records are intentionally not rendered remotely. Editing and saving their
  retained source URL retries localization; a future maintenance job may bulk-retry them if needed.

## Phased Implementation Plan

**Phase 1 — Model + API**
- Entities, migration, CRUD controller, search, tests

**Phase 2 — Library UI**
- Recipe list + detail pages

**Phase 3 — Manual editing**
- Create/edit form with dynamic ingredients/steps

**Phase 4 — Local image ownership**
- Normalize URL imports and local uploads into bounded local WebP images
- Add file/camera/URL controls and an edit-view preview/placeholder
- Keep remote URLs as references only; never render them directly

## Implementation Checklist

- [x] `Recipe`/`RecipeIngredient`/`RecipeStep` entities + migration
- [x] `RecipesController` CRUD + search + DTOs (deletes detach planner references client-side
      so the denormalized dish name survives, identical across EF providers)
- [x] Integration tests for recipe CRUD (create, search, update wholesale, delete + history survival)
- [x] `recipes.service.ts` + models
- [x] Recipe list page with search
- [x] Recipe detail page (incl. original ingredient text toggle, edit, delete)
- [x] Recipe create/edit form (dynamic ingredient + step rows)
- [x] Central image validation, auto-orientation, resize (1600 px), and WebP encoding
- [x] New/changed image URLs download locally while retaining the original URL as a reference
- [x] Local file and camera upload endpoint; replacement clears the remote URL reference
- [x] Local-only image DTO policy plus remove-image endpoint
- [x] Edit-view image preview, skeleton placeholder, file/camera controls, confirmable URL editor,
      source info tooltip, and unobtrusive remove action
- [x] Image processor and controller/import integration tests
