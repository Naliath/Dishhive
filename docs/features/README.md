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

Future ideas live in [`../../possible-features.md`](../../possible-features.md).
The infrastructure foundation is planned in
[`../plans/INFRASTRUCTURE_SETUP_PLAN.md`](../plans/INFRASTRUCTURE_SETUP_PLAN.md).
