# Dishhive Feature Documents

One living document per functional feature. Each contains the feature goal, scope, user
stories, domain model, backend/frontend/integration requirements, risks, a phased plan, and an
implementation checklist that is updated as work progresses.

**Checklist status convention:** `[ ]` = new · `[~]` = in progress · `[x]` = done

| Document | Feature | Depends on |
|----------|---------|------------|
| [family-composition.md](family-composition.md) | Household members, preferences, allergies, guests | — |
| [recipe-store.md](recipe-store.md) | Recipe library: ingredients, steps, metadata | — |
| [recipe-import.md](recipe-import.md) | Pluggable import (Dagelijkse Kost first) incl. API research | recipe-store |
| [measurement-preferences.md](measurement-preferences.md) | Metric/imperial setting + normalization model | recipe-store |
| [week-planner.md](week-planner.md) | Week menu planning, attendance, AI seam | family-composition, recipe-store |
| [freezy-integration.md](freezy-integration.md) | Frozen leftovers from Freezy in planning | week-planner |
| [past-dishes-and-statistics.md](past-dishes-and-statistics.md) | History, favorites, frequency stats | week-planner |
| [shopping-list-export.md](shopping-list-export.md) | Shopping list from the planned week | week-planner, recipe-store |
| [demo-mode.md](demo-mode.md) | Seed demo recipes + household into an empty database | recipe-import, family-composition |
| [meal-feedback.md](meal-feedback.md) | Mark meals eaten/skipped, per-member ratings | past-dishes-and-statistics |
| [ai-week-planning.md](ai-week-planning.md) | LLM week suggestions (5 providers) + rules fallback | week-planner, meal-feedback, freezy-integration |
| [dietary-tags.md](dietary-tags.md) | Structured allergy/diet tags replacing free text | family-composition |
| [recipe-organization.md](recipe-organization.md) | Recipe tags, category filter, cookbooks (saved filters) | recipe-store |
| [pwa-support.md](pwa-support.md) | Installable app + offline read of plan and shopping list | week-planner, shopping-list-export |
| [recipe-import-export.md](recipe-import-export.md) | Library backup/restore as schema.org Recipe JSON | recipe-store, recipe-import |

Future ideas live in [`../../possible-features.md`](../../possible-features.md).
The infrastructure foundation is planned in
[`../plans/INFRASTRUCTURE_SETUP_PLAN.md`](../plans/INFRASTRUCTURE_SETUP_PLAN.md).
