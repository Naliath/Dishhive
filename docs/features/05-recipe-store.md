# Feature: Recipe Store

**Created:** April 19, 2026  
**Status:** ✅ Implemented

---

## Feature Goal

Provide a recipe library that stores recipes with ingredients, instructions, and metadata. Recipes can be manually created, imported from external sources, and linked to planned meals and shopping lists.

---

## Scope

### In Scope
- Recipe CRUD (create, read, update, delete)
- Ingredients with quantity and unit
- Preparation steps (ordered)
- Metadata: title, description, servings, tags, source
- Picture (URL or uploaded)
- Video link (optional)
- Source link (for imported recipes)
- Linking recipes to planned meals
- Recipe search and filtering

### Out of Scope (for this phase)
- Recipe rating/review system (handled by Past Dishes & Statistics feature)
- Nutritional information
- Cooking timer integration

---

## User Stories

1. **As a meal planner**, I want to browse my recipe collection so I can choose what to cook.
2. **As a meal planner**, I want to add a recipe manually with ingredients and steps.
3. **As a meal planner**, I want to search recipes by name or tag.
4. **As a meal planner**, I want to filter recipes by dietary constraint (e.g., vegetarian).
5. **As a meal planner**, I want to see how many people a recipe serves so I can scale it.
6. **As a meal planner**, I want to import a recipe from a URL (see feature 06).

---

## Domain Model Considerations

```
Recipe
  - Id: Guid
  - Title: string (required, max 200)
  - Description: string? (max 2000)
  - Servings: int (default 4)
  - PrepTimeMinutes: int?
  - CookTimeMinutes: int?
  - PictureUrl: string? (max 500)
  - VideoUrl: string?
  - SourceUrl: string? (for imported recipes)
  - SourceName: string? (e.g., "Dagelijkse Kost")
  - SourceRawData: string? (JSON — original scraped data for traceability)
  - Tags: string[] (JSON column — e.g., ["vegetarian", "quick"])
  - CreatedAt: DateTime
  - UpdatedAt: DateTime
  - Ingredients: ICollection<RecipeIngredient>
  - Steps: ICollection<RecipeStep>

RecipeIngredient
  - Id: Guid
  - RecipeId: Guid (FK → Recipe)
  - Name: string (required, max 200)
  - Quantity: decimal?
  - Unit: string? (e.g., "g", "tbsp", "cup") — normalized unit
  - OriginalQuantity: decimal? (source value before conversion)
  - OriginalUnit: string?       (source unit before conversion)
  - Notes: string? (e.g., "finely chopped")
  - SortOrder: int

RecipeStep
  - Id: Guid
  - RecipeId: Guid (FK → Recipe)
  - StepNumber: int
  - Instruction: string (required, max 2000)
```

### Note on Measurement Storage

Both normalized and original values are stored per ingredient:
- `Quantity` / `Unit` = Dishhive's normalized value (metric by default)
- `OriginalQuantity` / `OriginalUnit` = source value (preserved for traceability)

This allows manual correction and supports the measurement preferences feature (07).

---

## Backend Requirements

- `RecipesController` with endpoints:
  - `GET /api/recipes` — list (with search and tag filter)
  - `GET /api/recipes/{id}` — get with ingredients and steps
  - `POST /api/recipes` — create
  - `PUT /api/recipes/{id}` — update
  - `DELETE /api/recipes/{id}` — delete
  - `POST /api/recipes/import` — import from URL (delegates to RecipeImportService)

---

## Frontend Requirements

- Recipe list page at `/recipes`
- Recipe detail page at `/recipes/{id}`
- Recipe create/edit form at `/recipes/new` and `/recipes/{id}/edit`
- Search bar at top of list
- Tag filter chips
- Recipe import dialog (URL input → import preview → save)
- Ingredient list with quantity/unit display
- Step-by-step instructions display

---

## Integration Requirements

- Week planner meal assignment uses recipe search
- Shopping list generation reads ingredients from linked recipes
- Past dishes statistics tracks recipes by ID

---

## Risks / Unknowns

- `Tags` stored as JSON array column — works well with PostgreSQL but needs careful querying for filtering.
- `SourceRawData` as a JSON string column provides traceability but can grow large.
- Image uploads vs URL references: Phase 1 supports URL only; Phase 2 can add file upload.

---

## Phased Implementation Plan

### Phase 1 — Core Recipe CRUD
- [ ] `Recipe`, `RecipeIngredient`, `RecipeStep` entities + EF config
- [ ] `RecipesController` with CRUD endpoints
- [ ] `RecipeDtos.cs`
- [ ] Integration tests
- [ ] Angular recipes list page
- [ ] Angular recipe detail page
- [ ] `RecipesService` in Angular

### Phase 2 — Import Integration
- [ ] Import endpoint + RecipeImportService integration (see feature 06)
- [ ] Recipe import dialog in Angular

### Phase 3 — Search & Filtering
- [ ] Full-text search on title/description
- [ ] Tag-based filtering
- [ ] Angular search + filter UI

---

## Implementation Checklist

### Backend
- [x] Feature plan created
- [x] `Recipe` entity created
- [x] `RecipeIngredient` entity created
- [x] `RecipeStep` entity created
- [x] EF Core configuration added to `DishhiveDbContext`
- [x] `RecipesController` created (CRUD)
- [x] `RecipeDtos.cs` created
- [x] Integration tests for recipe CRUD (10 tests)
- [x] Import endpoint wired to RecipeImportService
- [x] `?units=imperial` query param for measurement conversion

### Frontend
- [x] `recipe.model.ts` created
- [x] `RecipesService` created
- [x] Recipe list page created (with search)
- [x] Recipe detail page created (respects measurement setting)
- [x] Recipe import dialog created
- [x] Manual recipe create/edit form (`/recipes/new`, `/recipes/:id/edit`)
- [x] Delete button on recipe detail page
