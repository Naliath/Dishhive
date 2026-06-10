# Feature: Shopping List Export

> **Feature ID**: SLE-001
> **Status**: Planned
> **Priority**: High
> **Depends on**: Recipe Store, Week Planner, Measurement Preferences
> **Last Updated**: 2026-06-07

## 1. Feature Goal

Generate a consolidated shopping list from the planned week menu, aggregating ingredients across all planned meals with support for quantity adjustment based on servings.

## 2. Scope

### In Scope (v1)
- Generate shopping list from planned week menu
- Aggregate identical ingredients across meals
- Adjust quantities based on planned servings vs recipe default servings
- Display in user's preferred measurement system
- Export formats: text, CSV
- Mark items as purchased
- Group items by category (produce, dairy, meat, etc.)

### Out of Scope (v1)
- Integration with online grocery stores
- Price tracking
- Pantry inventory deduction
- Multi-household sharing
- Barcode scanning

## 3. User Stories / Use Cases

| ID | Story | Priority |
|----|-------|----------|
| SLE-US-001 | As a user, I want to generate a shopping list from my planned week menu | Must |
| SLE-US-002 | As a user, I want identical ingredients aggregated | Must |
| SLE-US-003 | As a user, I want quantities adjusted for my planned servings | Must |
| SLE-US-004 | As a user, I want items grouped by category | Should |
| SLE-US-005 | As a user, I want to mark items as purchased | Should |
| SLE-US-006 | As a user, I want to export the list as text/CSV | Must |
| SLE-US-007 | As a user, I want the list in my preferred measurement units | Must |

## 4. Domain Model

```
ShoppingList
├── Id: Guid
├── WeekStart: DateTime
├── WeekEnd: DateTime
├── GeneratedAt: DateTime
├── Items: List<ShoppingListItem>
├── Categories: List<ShoppingListCategory>
└── Notes: string?

ShoppingListItem
├── Id: Guid
├── Name: string
├── Quantity: decimal
├── Unit: string
├── Category: string
├── IsPurchased: bool
├── SourceRecipes: List<RecipeReference>
└── Notes: string?

ShoppingListCategory
├── Name: string (Produce, Dairy, Meat, Bakery, etc.)
├── DisplayOrder: int
└── Items: List<ShoppingListItem>

IngredientCategoryMapping
├── IngredientName: string
├── Category: string
└── Confidence: float (for auto-categorization)
```

## 5. Aggregation Logic

### Quantity Adjustment

```
adjusted_quantity = recipe_quantity * (planned_servings / recipe_default_servings)
```

### Ingredient Matching

Ingredients are considered identical if:
- Name matches (case-insensitive, trimmed)
- Unit matches (after normalization to preferred system)

### Known Challenges

| Challenge | Approach |
|-----------|----------|
| "Salt" vs "Sea salt" vs "Kosher salt" | Configurable synonym mapping |
| "Flour" vs "All-purpose flour" | User can merge manually |
| "Butter" in 2 recipes, different units | Normalize units first, then aggregate |
| User already has item at home | Manual exclusion from list |

## 6. Backend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| SLE-BE-001 | Shopping list generation service | Must |
| SLE-BE-002 | Ingredient aggregation engine | Must |
| SLE-BE-003 | Serving adjustment calculation | Must |
| SLE-BE-004 | Category grouping | Should |
| SLE-BE-005 | Export service (text, CSV) | Must |
| SLE-BE-006 | Shopping list persistence | Should |
| SLE-BE-007 | Ingredient category mapping | Should |

### API Endpoints

```
POST   /api/shopping-lists/generate
GET    /api/shopping-lists
GET    /api/shopping-lists/{id}
PUT    /api/shopping-lists/{id}/items/{itemId}/purchased
DELETE /api/shopping-lists/{id}
GET    /api/shopping-lists/{id}/export?format=csv
GET    /api/shopping-lists/{id}/export?format=text
```

## 7. Frontend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| SLE-FE-001 | Shopping list generation button on week planner | Must |
| SLE-FE-002 | Shopping list view with categories | Must |
| SLE-FE-003 | Purchase toggle per item | Should |
| SLE-FE-004 | Export buttons (CSV, text) | Must |
| SLE-FE-005 | Manual item addition | Should |
| SLE-FE-006 | Manual item removal | Should |
| SLE-FE-007 | Filter by category | Could |
| SLE-FE-008 | Sort options | Could |

## 8. Integration Requirements

| ID | Integration | Description |
|----|-------------|-------------|
| SLE-INT-001 | Week Planner | Source of planned meals and servings |
| SLE-INT-002 | Recipe Store | Source of ingredient data |
| SLE-INT-003 | Measurement Preferences | Unit display formatting |
| SLE-INT-004 | Freezy (future) | Exclude items available as frozen meals |

## 9. Risks / Unknowns

| ID | Risk | Mitigation |
|----|------|------------|
| SLE-R001 | Inaccurate ingredient matching | Allow manual merge/split; improve synonym mapping over time |
| SLE-R002 | Serving size inconsistencies | Clear defaults; allow user override |
| SLE-R003 | Category classification errors | Configurable mappings; manual reassignment |

## 10. Implementation Checklist

### Backend
- [ ] Shopping list generation service
- [ ] Ingredient aggregation engine
- [ ] Serving adjustment logic
- [ ] Category grouping service
- [ ] Export service (CSV, text)
- [ ] Shopping list CRUD endpoints
- [ ] Ingredient category mapping

### Frontend
- [ ] Generate button on week planner
- [ ] Shopping list view
- [ ] Category grouping display
- [ ] Purchase toggle UI
- [ ] Export buttons
- [ ] Manual item management

### Testing
- [ ] Unit test: ingredient aggregation
- [ ] Unit test: serving adjustment calculation
- [ ] Integration test: full generation flow
- [ ] Integration test: export formats
