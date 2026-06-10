# Feature: Recipe Store

> **Feature ID**: RCP-001
> **Status**: Planned
> **Priority**: High
> **Depends on**: Infrastructure
> **Last Updated**: 2026-06-07

## 1. Feature Goal

Provide a centralized recipe repository within Dishhive where users can store, organize, search, and manage recipes that feed into meal planning, shopping list generation, and other downstream features.

## 2. Scope

### In Scope (v1)
- Create, read, update, and delete recipes
- Recipe ingredients with quantities and units
- Step-by-step preparation instructions
- Recipe metadata (prep time, cook time, servings, difficulty)
- Recipe categories and tags
- Recipe images
- Link recipes to planned meals
- Basic search and filtering
- Recipe import from supported sources (see Recipe Import feature)

### Out of Scope (v1)
- Recipe versioning
- Collaborative recipe editing
- Recipe sharing with external users
- Nutritional analysis engine
- AI-generated recipes

## 3. User Stories / Use Cases

| ID | Story | Priority |
|----|-------|----------|
| RCP-US-001 | As a user, I want to add a new recipe manually | Must |
| RCP-US-002 | As a user, I want to view a recipe with all details | Must |
| RCP-US-003 | As a user, I want to edit an existing recipe | Must |
| RCP-US-004 | As a user, I want to delete a recipe | Must |
| RCP-US-005 | As a user, I want to search recipes by name or ingredient | Must |
| RCP-US-006 | As a user, I want to filter recipes by category or tag | Should |
| RCP-US-007 | As a user, I want to upload a photo for a recipe | Should |
| RCP-US-008 | As a user, I want to import a recipe from a URL | Should |
| RCP-US-009 | As a user, I want to duplicate a recipe | Could |
| RCP-US-010 | As a user, I want to see how often a recipe was used | Could |

## 4. Domain Model

```
Recipe
├── Id: UUID
├── Title: string
├── Description: string?
├── PrepTimeMinutes: int?
├── CookTimeMinutes: int?
├── TotalTimeMinutes: int?
├── Servings: int
├── Difficulty: enum (Easy, Medium, Hard)?
├── Instructions: string (rich text / steps)
├── ImageUrl: string?
├── VideoUrl: string?
├── SourceUrl: string?
├── SourceName: string?
├── DateCreated: DateTime
├── DateModified: DateTime?
├── IsArchived: bool
├── Ingredients: List<RecipeIngredient>
├── Categories: List<RecipeCategory>
├── Tags: List<RecipeTag>
└── ImportMetadata: JSON? (source-specific data)

RecipeIngredient
├── Id: UUID
├── RecipeId: UUID (FK)
├── Name: string
├── Quantity: decimal?
├── Unit: string? (normalized to metric)
├── OriginalQuantity: decimal?
├── OriginalUnit: string?
├── Notes: string?
├── DisplayOrder: int
├── IsOptional: bool
└── Category: string? (produce, dairy, pantry, etc.)

RecipeCategory
├── Id: UUID
├── Name: string
├── Description: string?
├── Slug: string
├── ParentCategoryId: UUID? (FK, self-referencing)
└── Icon: string?

RecipeTag
├── Id: UUID
├── Name: string
├── Slug: string
└── Color: string?

RecipeTagMapping
├── RecipeId: UUID (FK)
└── TagId: UUID (FK)
```

### Measurement Normalization Strategy

Recipes may originate from various sources using different measurement systems. The model supports:

1. **Normalized storage**: Primary quantities stored in metric (g, ml, cm)
2. **Original preservation**: Source quantities and units preserved in `OriginalQuantity`/`OriginalUnit`
3. **Conversion table**: Built-in unit conversion service handles common conversions
4. **Manual override**: Users can edit any value if auto-conversion is incorrect

### Conversion Service Design

```
IUnitConversionService
├── ConvertToMetric(quantity, unit) -> (decimal, string)
├── ConvertToImperial(quantity, unit) -> (decimal, string)
├── GetSupportedUnits() -> List<UnitDefinition>
└── RegisterCustomConversion(from, to, factor)
```

## 5. Backend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| RCP-BE-001 | Full CRUD for recipes | Must |
| RCP-BE-002 | Full CRUD for recipe ingredients | Must |
| RCP-BE-003 | Recipe search by title/description/ingredient | Must |
| RCP-BE-004 | Recipe filtering by category/tag | Should |
| RCP-BE-005 | Recipe category management | Should |
| RCP-BE-006 | Recipe tag management | Should |
| RCP-BE-007 | Image upload and storage | Should |
| RCP-BE-008 | Unit conversion service | Must |
| RCP-BE-009 | Recipe import interface (pluggable) | Must |
| RCP-BE-010 | Pagination and sorting | Should |

### API Endpoints

```
GET    /api/recipes
GET    /api/recipes/{id}
POST   /api/recipes
PUT    /api/recipes/{id}
DELETE /api/recipes/{id}

GET    /api/recipes/{id}/ingredients
POST   /api/recipes/{id}/ingredients
PUT    /api/recipes/{id}/ingredients/{ingredientId}
DELETE /api/recipes/{id}/ingredients/{ingredientId}

GET    /api/recipes/categories
POST   /api/recipes/categories
GET    /api/recipes/tags
POST   /api/recipes/tags

POST   /api/recipes/import
POST   /api/recipes/import/{sourceProvider}
GET    /api/recipes/import/sources

POST   /api/recipes/{id}/image
DELETE /api/recipes/{id}/image

GET    /api/recipes/search?q={query}
```

## 6. Frontend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| RCP-FE-001 | Recipe list view with search | Must |
| RCP-FE-002 | Recipe detail view | Must |
| RCP-FE-003 | Recipe create/edit form | Must |
| RCP-FE-004 | Ingredient editor | Must |
| RCP-FE-005 | Category/tag filter sidebar | Should |
| RCP-FE-006 | Image upload UI | Should |
| RCP-FE-007 | Recipe import wizard | Should |
| RCP-FE-008 | Recipe card component (for reuse in planner) | Must |

### UI Components

- `RecipeListComponent` — paginated list with search
- `RecipeDetailComponent` — full recipe view
- `RecipeFormComponent` — create/edit form
- `RecipeIngredientEditorComponent` — ingredient management
- `RecipeCardComponent` — compact recipe preview
- `RecipeFilterSidebarComponent` — category/tag filters
- `RecipeImageUploadComponent` — image handling
- `RecipeImportWizardComponent` — import from URL

## 7. Integration Requirements

| ID | Integration | Direction | Notes |
|----|------------|-----------|-------|
| RCP-INT-001 | Week Planner | Outbound | Recipes available for meal slots |
| RCP-INT-002 | Shopping List | Outbound | Ingredients used for list generation |
| RCP-INT-003 | Recipe Import | Inbound | Imported recipes stored here |
| RCP-INT-004 | Freezy Integration | Bidirectional | Frozen meals may reference recipes |
| RCP-INT-005 | Past Dishes | Outbound | Recipe usage tracking |

## 8. Risks / Unknowns

| ID | Risk | Mitigation |
|----|------|------------|
| RCP-R001 | Unit conversion may lose precision for unusual units | Allow manual override; flag uncertain conversions |
| RCP-R002 | Rich text instructions may have security risks | Sanitize HTML input; use allowed tags list |
| RCP-R003 | Large recipe collections may impact search performance | Implement full-text search with proper indexing |
| RCP-R004 | Image storage strategy TBD | Start with local/file storage; plan for cloud later |

## 9. Phased Implementation Plan

### Phase 1 — Core Recipe CRUD
- [ ] Database migration for Recipe, RecipeIngredient
- [ ] Recipe CRUD API endpoints
- [ ] Recipe CRUD frontend forms
- [ ] Basic recipe list and detail views

### Phase 2 — Organization
- [ ] Database migration for categories and tags
- [ ] Category and tag management
- [ ] Filter and search UI
- [ ] Pagination

### Phase 3 — Media & Import
- [ ] Image upload support
- [ ] Recipe import interface
- [ ] Import wizard UI
- [ ] Unit conversion service

### Phase 4 — Polish
- [ ] Full-text search optimization
- [ ] Recipe card component
- [ ] Integration with planner and shopping list
- [ ] Recipe archiving

## 10. Implementation Checklist

### Infrastructure
- [ ] Database migration created and applied
- [ ] Entity models defined
- [ ] DTOs defined
- [ ] Image storage configured

### Backend
- [ ] RecipeController
- [ ] RecipeService
- [ ] RecipeIngredientController
- [ ] RecipeCategoryController
- [ ] RecipeTagController
- [ ] Recipe search service
- [ ] Unit conversion service
- [ ] Recipe import interface
- [ ] Image upload handler
- [ ] Unit tests
- [ ] Integration tests

### Frontend
- [ ] Recipe list component
- [ ] Recipe detail component
- [ ] Recipe form component
- [ ] Ingredient editor
- [ ] Filter sidebar
- [ ] Image upload UI
- [ ] Recipe card component
- [ ] Service/HTTP client

### Testing
- [ ] Unit tests for CRUD operations
- [ ] Unit tests for unit conversion
- [ ] Integration tests for API
- [ ] E2E tests for recipe management
