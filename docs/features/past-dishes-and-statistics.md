# Feature: Past Dishes & Statistics

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [week-planner.md](week-planner.md), [family-composition.md](family-composition.md)

## Feature Goal

Give the household insight into what was actually planned and eaten: a browsable history of
planned dishes, per-member favorites, and statistics such as how often a dish was planned.

## Scope

**In scope**
- History view of past planned meals (the planner's own past rows — no separate event store)
- Dish frequency statistics ("spaghetti: 9× in the last 90 days")
- Last-planned date per dish/recipe ("you haven't had fish in 3 weeks")
- Per-member favorite list (from family-composition favorites) cross-referenced with frequency
- Mark a favorite directly from a past dish

**Out of scope**
- "Was it actually eaten / how was it rated" feedback loop — now its own feature, see
  [meal-feedback.md](meal-feedback.md)
- Nutritional statistics
- Charts beyond simple counts/lists initially (Freezy uses chart.js — can be added when a chart
  earns its place)

## User Stories / Use Cases

1. As a planner, I scroll back through previous weeks to remember what we ate.
2. As a planner, I see the most-planned dishes so I can add variety (or lean into hits).
3. As a planner, I see when a dish was last planned while deciding this week's menu.
4. As a member, my favorites are visible so the planner can balance everyone's preferences.
5. As a planner, I mark "that lasagna from two weeks ago" as Dad's favorite in one click.

## Domain Model Considerations

- **No new tables.** History = `PlannedMeal WHERE Date < today`. `DishName` is denormalized at
  planning time precisely so history survives recipe changes (see week-planner.md).
- Statistics are computed with grouped queries over `PlannedMeal` (`GROUP BY DishName`,
  attendance joins for per-member views). Indexes on `Date` and `DishName` keep this cheap at
  household scale (a household plans ~500 meals/year; no materialized views needed).
- Mirrors Freezy's approach where `ConsumptionEvent` + `StatisticsController` compute aggregates
  server-side and ship small DTOs.

## Backend Requirements

- `StatisticsController`:
  - `GET /api/statistics/dishes?from=&to=` → `[ { dishName, recipeId?, timesPlanned, lastPlanned } ]`
  - `GET /api/statistics/members/{id}` → per-member attendance + favorite-dish frequency
- History uses the existing planner endpoint (`GET /api/plannedmeals?from=&to=`) — no duplicate API
- DTOs in `StatisticsDtos.cs`; `AsNoTracking` grouped queries

## Frontend Requirements

- Page `pages/history/` — past weeks list (reuses week-grid components read-only where possible)
- Page `pages/statistics/` — dish frequency table with search, "last planned" column,
  favorite-toggle per member (chips)
- `statistics.service.ts`

## Integration Requirements

- Week planner: "last planned X days ago" hint when picking a dish/recipe
- Family composition: favorite toggles write through the favorites endpoints

## Risks / Unknowns

- Free-text dish names fragment statistics ("spaghetti" vs "spaghetti bolognese") — mitigate
  with autocomplete-from-history in the planner slot editor; true canonicalization is future work.
- Vague-instruction slots ("something quick") pollute dish stats — excluded from dish frequency
  (counted separately as "unspecified").

## Phased Implementation Plan

**Phase 1 — History view** (after planner phase 2)
- Read-only past weeks browsing

**Phase 2 — Dish statistics**
- Frequency/last-planned endpoint + statistics page

**Phase 3 — Favorites integration**
- Per-member favorite surfaces, one-click favorite from history, planner hints

## Implementation Checklist

- [x] History browsing UI (past days grouped, load-older)
- [x] `GET /api/statistics/dishes` endpoint + tests (incl. unspecified count, date range)
- [x] Statistics page (frequency table with filter, last planned)
- [x] Per-member statistics endpoint (`GET /api/statistics/members/{id}`) + tests
- [x] Favorite toggle from statistics (star per dish row → pick the member)
- [x] "Last planned" hint in planner slot editor (matches dish name/recipe title against history)
