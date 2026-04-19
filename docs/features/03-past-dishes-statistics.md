# Feature: Past Dishes & Statistics

**Created:** April 19, 2026  
**Status:** ✅ Implemented (Phase 2 per-member view deferred)

---

## Feature Goal

Track the history of planned meals and provide statistics to help with planning decisions — e.g., how often a dish was planned, favorites per family member, and rotation suggestions.

---

## Scope

### In Scope
- History of planned meals (persisted automatically from week planner)
- Frequency statistics per dish
- Per-member favorite tracking (implicit from history + explicit favorites)
- "Last time this was cooked" per dish

### Out of Scope (for this phase)
- Nutritional statistics
- Cost tracking
- Ingredient consumption history

---

## User Stories

1. **As a meal planner**, I want to see which dishes were planned most often so I can vary my menu.
2. **As a household member**, I want to mark a meal we had as a favorite so I can find it again.
3. **As a meal planner**, I want to see when a particular dish was last served so I can avoid repeating it too soon.
4. **As a meal planner**, I want to see a family member's top dishes to plan meals they enjoy.

---

## Domain Model Considerations

History is derived from the `WeekPlan` / `PlannedMeal` tables — no separate history entity is needed in Phase 1. Statistics are computed queries over the `PlannedMeal` table.

A separate `DishRating` entity supports explicit favorites/ratings:

```
DishRating
  - Id: Guid
  - FamilyMemberId: Guid (FK → FamilyMember)
  - RecipeId: Guid? (FK → Recipe)
  - DishName: string   (for free-text dishes without a recipe)
  - Rating: int        (1–5 stars, or use enum: Disliked/Neutral/Liked/Loved)
  - CreatedAt: DateTime
  - UpdatedAt: DateTime
```

---

## Backend Requirements

- `StatisticsController` with endpoints:
  - `GET /api/statistics/dishes` — dishes sorted by frequency (with optional date range filter)
  - `GET /api/statistics/dishes/{recipeName}` — detail for one dish (planned count, last planned date)
  - `GET /api/statistics/members/{memberId}/favorites` — top dishes for a member (from history + explicit ratings)
  - `POST /api/statistics/ratings` — add/update a dish rating
  - `GET /api/statistics/ratings` — list all ratings

---

## Frontend Requirements

- Statistics page at `/settings/statistics` or `/history`
- Dish frequency chart (bar chart with recipe names)
- "Last cooked" per dish display
- Per-member favorites view

---

## Risks / Unknowns

- Free-text vague instructions ("something with fish") don't map to a recipe — group them by string match or leave them as-is in stats.
- Statistics queries may become expensive at scale — add pagination early.

---

## Phased Implementation Plan

### Phase 1 — History Queries
- [ ] Statistics endpoint for dish frequency
- [ ] "Last planned" query per dish

### Phase 2 — Ratings & Favorites
- [ ] `DishRating` entity + EF config
- [ ] Ratings endpoint
- [ ] Per-member favorites endpoint

### Phase 3 — UI
- [ ] Statistics page in Angular
- [ ] Frequency chart component
- [ ] Per-member favorites view

---

## Implementation Checklist

### Backend
- [x] Feature plan created
- [x] Statistics endpoints created (`/overview`, `/top-recipes`, `/meal-frequency`, `/recent-weeks`)
- [x] `DishRating` entity created
- [x] Ratings endpoints created (`GET /api/statistics/ratings`, `POST /api/statistics/ratings`, `DELETE /api/statistics/ratings/{id}`)
- [x] EF migration `AddDishRatings` applied
- [x] Integration tests for statistics + ratings (9 tests)

### Frontend
- [x] Statistics page created
- [x] Overview cards (total meals, unique recipes, busiest day)
- [x] Top recipes list
- [x] Meal frequency by day chart (bar display)
- [ ] Per-member favorites view (Phase 2 — future)
