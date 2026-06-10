# Feature: Demo Mode

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [recipe-import.md](recipe-import.md), [family-composition.md](family-composition.md)

## Feature Goal

Let someone evaluate Dishhive with one `docker-compose up`: an empty database is seeded
with a realistic household and a filled recipe store so every page has something to show.

## Behavior

- Controlled by the `Demo:Enabled` configuration flag (`Demo__Enabled` env var).
  **Default `true` in `docker-compose.yml`**, `false` in `appsettings.json` for local dev.
- `DemoDataSeeder` (a `BackgroundService`) runs after startup/migrations so the
  network-bound imports never delay the app or its health check.
- Seeds only when the database holds **no recipes and no family members**, and writes a
  `demo.dataSeeded` user-setting marker so demo data is never re-created after the user
  deletes it. Turning the flag off later changes nothing — existing data is left alone.

## Seeded Data

**Recipes** — 20 recipes scraped live from [Dagelijkse Kost](https://dagelijksekost.vrt.be)
through the regular import pipeline (`IRecipeImportService`), so they get parsed
ingredients, full steps and locally stored images. The set (in
`Services/Demo/DemoData.cs`) spreads across meat, fish, vegetarian dishes, lunches and
desserts. A URL that fails to import (offline, page changed) is logged and skipped; the
demo works with fewer recipes.

**Household** — the crew of the Rocinante (The Expanse):

| Member | Dietary needs | Favorites |
|--------|--------------|-----------|
| James Holden | Shellfish allergy | Pasta half-en-half, creamy chicken, spaghetti bolognese |
| Naomi Nagata | **Vegetarian** | Sweet-and-sour cauliflower, courgette-feta burger, scamorza potato dumplings, vegetable curry |
| Alex Kamal | Lactose intolerant | Lasagne verde, drunken noodles, chili con carne |
| Amos Burton | — (eats anything) | Loaded fries, lamb shank, cheeseburger and fries |

Favorites link to the imported recipes where possible (denormalized dish name as usual)
plus a free-text favorite per member.

## Implementation Checklist

- [x] `Demo:Enabled` flag (appsettings default false, docker-compose default true)
- [x] `DemoData` static dataset (20 URLs, 4 members, favorites, dietary needs)
- [x] `DemoDataSeeder` background service with empty-database guard + seeded marker
- [x] Tolerant import loop (per-URL failures logged, seeding continues)
- [x] Consistency tests on the dataset (`DemoDataTests`)
