# Feature: Demo Mode

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [recipe-import.md](recipe-import.md), [family-composition.md](family-composition.md)

## Feature Goal

Let someone evaluate Dishhive with one `docker-compose up`: an empty database is seeded
with a realistic household and a filled recipe store so every page has something to show.

## Behavior

- Controlled by the `Demo:Enabled` configuration flag (`Demo__Enabled` env var).
  **Default `true` in `docker-compose.yml`**, `false` in `appsettings.json` for local dev.
- `DemoDataSeeder` (a `BackgroundService`) runs after startup/migrations. It restores
  the embedded recipe backup through the regular file importer without website or AI calls.
- Seeds only when the database holds **no recipes and no family members**, and writes a
  `demo.dataSeeded` user-setting marker so demo data is never re-created after the user
  deletes it. Turning the flag off later changes nothing — existing data is left alone.

## Seeded Data

**Recipes** — 220 recipes with parsed ingredients, full instructions, locally stored
images and current AI-detected dietary facts, restored from the embedded
`Services/Demo/demo-seed.zip`. The file is also an ordinary recipe-library backup that
can be imported from Settings. It contains standard schema.org `recipes.json` with
embedded images, facts and cookbook memberships.

Demo photos use WebP quality 75 with a maximum edge of 1000 pixels. This profile is
applied when generating the demo archive; ordinary recipe imports retain their
1600-pixel, quality-82 settings. The compact archive is approximately 14.4 MB.

| Source | Recipes | Selection |
|--------|---------|-----------|
| [Dagelijkse Kost](https://dagelijksekost.vrt.be) | 60 | Belgian classics, original household favourites, vegetables and Belgian desserts |
| [RecipeTin Eats](https://www.recipetineats.com) | 40 | Asian cooking, vegetarian dishes and desserts |
| [BBC Good Food](https://www.bbcgoodfood.com) | 40 | Vegetarian meals, desserts, fish and seafood |
| [Budget Bytes](https://www.budgetbytes.com) | 40 | Everyday meals, vegetarian cooking and soups |
| [Love and Lemons](https://www.loveandlemons.com) | 40 | Plant-based dinners, noodles, curries and soups |

The exact selection and thematic cookbooks are recorded in `Services/Demo/demo-recipes.json`.
Source URLs remain on every exported recipe. AI assessments remain `aiDetected`, never
implicitly human-confirmed. Existing households are not modified when this seed changes.

### Regenerating the archive

Run from the repository root with Docker Desktop, the scraper sidecar and the configured
local application running:

```powershell
./scripts/generate-demo-seed.ps1
```

The script copies the application's AI configuration through environment variables, creates
an isolated PostgreSQL container/database and invokes `--generate-demo-seed`. Generation
uses the real guarded URL importer, image processor, durable enrichment jobs and central AI
execution gate. Four consumers drain the jobs while the gate retains its configured model
concurrency and task settings. This is a live model operation using the configured provider.

The generator requires at least 200 unique recipes from five sources. It exports only when
every selected recipe has ingredients, instructions, a local image and current AI facts,
and refuses duplicate titles that would be skipped on restore. It writes the seed atomically
and records source/category counts, model outcomes and size measurements in
`demo-seed.report.json`. A failed run retains its disposable database for resuming; a successful
run removes the container. Household databases are never used. The generator build omits its
own seed resource so it can bootstrap a missing archive; normal app builds embed the ZIP.

**Household** — the crew of the Rocinante (The Expanse):

| Member | Dietary needs | Favorites |
|--------|--------------|-----------|
| James Holden | Shellfish allergy | Pasta half-en-half, creamy chicken, spaghetti bolognese |
| Naomi Nagata | **Vegetarian** | Sweet-and-sour cauliflower, courgette-feta burger, scamorza potato dumplings, vegetable curry |
| Alex Kamal | Lactose intolerant | Lasagne verde, drunken noodles, chili con carne |
| Amos Burton | — (eats anything) | Loaded fries, lamb shank, cheeseburger and fries |

Favorites link to the imported recipes where possible (denormalized dish name as usual)
plus a free-text favorite per member.

**Past meals** — two weeks of past dinners (one per day, rotating through the seeded
recipes, all four crew members attending), most marked eaten with deterministic 3–5 star
ratings and one skipped day. This fills the history, statistics and meal-feedback pages
and gives AI week suggestions real data on a fresh database.

## Implementation Checklist

- [x] `Demo:Enabled` flag (appsettings default false, docker-compose default true)
- [x] Curated selection (220 URLs from 5 sources), 4 members, favourites and dietary needs
- [x] `DemoDataSeeder` background service with empty-database guard + seeded marker
- [x] Embedded compressed library backup (`demo-seed.zip`) and resumable import/enrichment/export generator
- [x] Past dinners with eaten marks and ratings (history/statistics/AI demo data)
- [x] Consistency tests on the dataset (`DemoDataTests`)
- [x] Offline seed restore, archive completeness, current facts/images and idempotence tests
