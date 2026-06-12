# Plan: Adopting `recipe-scrapers` for Recipe Import

**Status:** implemented (June 12, 2026) — sidecar container + fallback provider +
settings-widget status/update UI are in place; see implementation notes at the bottom
**Related:** [recipe-import.md](../features/recipe-import.md) (current C# pipeline),
[recipe-import-export.md](../features/recipe-import-export.md)

## What the library is

[recipe-scrapers](https://github.com/hhursev/recipe-scrapers) is an MIT-licensed **Python**
library that extracts recipes (title, ingredients, instructions, times, yields, image, …)
from ~500 recipe websites. It layers per-site scraper classes over a generic schema.org
JSON-LD/OpenGraph engine — architecturally the same idea as Dishhive's
`SchemaOrgRecipeExtractor` + per-site `IRecipeSourceProvider`, but with years of community
maintenance and a large fixture-test corpus behind it.

It supports `dagelijksekost.vrt.be` since v15.4.0, including the Next.js flight-payload
parsing for the full instruction list (the same quirk `DagelijkseKostProvider` handles).

## The core impact: a Python runtime in a .NET stack

Dishhive is .NET 10 + Angular, deployed with docker-compose. `recipe-scrapers` cannot be
referenced from C# — adopting it means running Python somewhere:

| Option | Verdict |
|--------|---------|
| **Sidecar HTTP service** (FastAPI + recipe-scrapers in its own container) | The only sane option. Stateless, fits docker-compose, pinnable version, independently testable. |
| Subprocess (`python import.py` per request) | Python runtime inside the API image; slow cold starts; messy error handling. Rejected. |
| Embedding (Python.NET / IronPython) | Fragile, complicates the build, IronPython can't run bs4 reliably. Rejected. |

### What we'd gain

- **~500 sites for free.** Today Dishhive imports from exactly one site. Every new site in
  the current architecture is a hand-written C# provider + fixture test. With the sidecar,
  one fallback provider covers the library's entire supported list, plus its `wild_mode`
  (generic schema.org extraction) for unsupported sites — and coverage grows with `pip install -U`.
- **Community maintenance.** When a site redesigns, upstream usually fixes it before we notice.
- **Architecture fit is clean.** Providers are pure (`HTML in → ImportedRecipe out`) and
  fetching stays in `RecipeImportService`. The sidecar mirrors this exactly:
  `POST /scrape { html, url } → recipe JSON`. A `RecipeScrapersProvider` slots in as the
  lowest-priority provider with zero changes to the import service, controller, or frontend.
  Ingredient parsing (`IngredientLineParser`), image download, and persistence all stay in .NET
  and apply unchanged — the library returns raw ingredient strings just like JSON-LD does.

### What it costs

- **A second runtime to operate.** New container (~150–250 MB image), new healthcheck, new
  thing that can be down. Import must degrade gracefully (clear 503-style error) when the
  sidecar is unavailable.
- **A cross-language contract to test.** Today's tests are pure in-process fixture tests.
  We'd add contract tests: stored HTML fixtures through the real sidecar in CI (compose-based),
  plus mocked-sidecar tests on the .NET side.
- **Dependency/supply-chain surface.** Python deps (bs4, etc.) need the same lockfile
  discipline as npm/NuGet; pin the library version, upgrade deliberately (a scraper "fix"
  upstream can change extracted values and silently alter imports).
- **Quality is uneven per site.** Generic extraction quality varies; the post-import review
  step ("imported — check values") becomes more important, not less.

### Specifically for Dagelijkse Kost: no gain today

`DagelijkseKostProvider` already does everything the upstream scraper does — and one thing
more: it prefers `og:title` over the schema.org `name`, which on many pages is an SEO teaser
sentence ("Jeroen Meus maakt …"). Upstream `title()` still returns the teaser. We've sent
that fix upstream (see PR below); until it's merged and released, replacing our provider
with the library would be a **regression** on titles.

## Recommendation

1. **Keep the native C# pipeline as-is for Dagelijkse Kost.** It's complete, tested, and
   currently better than upstream for this site.
2. **Upstream the title fix** so the ecosystem (and our future fallback) is correct:
   override `title()` in `recipe_scrapers/dagelijksekost.py` to prefer `og:title`
   (the same logic as [dagelijksekost-paprika](https://github.com/tomklaasen/dagelijksekost-paprika)'s
   `import_recipe.py`). — *Done, PR open.*
3. **Adopt the library as a fallback provider** behind the existing architecture — not as
   a replacement for it. *(Done — see Implementation below.)*

## Implementation (June 12, 2026)

**Sidecar — `src/dishhive-scraper/` + `docker/scraper.Dockerfile`**
- FastAPI app, `python:3.12-slim`, non-root user, compose service `scraper`
  (`dishhive-scraper`, host port 5101 for local dev; the app uses `http://scraper:8000`).
- `POST /scrape { html, url }` → recipe JSON (wild mode enabled, so any site with
  schema.org Recipe data works), 422 when the page has no recipe.
- `GET /healthz` → installed package version; `GET /version` → installed vs latest on
  PyPI; `POST /update { version? }` → pip-installs into the `scraper_lib` volume
  (`/data/lib`, ahead of site-packages via PYTHONPATH) and exits, letting the restart
  policy reload the process on the new version. Verified both directions (downgrade
  15.11.0 → 15.10.0 and back to latest).

**.NET — fallback provider behind the existing architecture**
- `RecipeScrapersClient` (typed HttpClient, `RecipeScrapers:BaseUrl`, empty = disabled)
  and `RecipeScrapersFallbackProvider` (key `recipe-scrapers`), registered **after**
  `DagelijkseKostProvider` — `RecipeImportService` picks the first matching provider, so
  dedicated implementations always win and the sidecar only sees the rest.
- Sidecar down ⇒ `RecipeExtractionFailedException` with a clear message (422 to the
  client); dedicated providers keep working regardless.

**Settings widget**
- The integrations card shows the scraper service status, installed version, a
  "Check for updates" action (PyPI via the sidecar), and a one-click update that polls
  until the restarted service is back.

**Tests:** mocked-sidecar provider mapping tests + provider-precedence tests
(`RecipeScrapersFallbackProviderTests`, `RecipeImportServiceTests`); container smoke
test of scrape/version/update/restart.

## Upstream PR

- Fix: `title()` override preferring `og:title` in `recipe_scrapers/dagelijksekost.py`,
  with a second test fixture (a recipe page whose schema name is the teaser sentence).
- [recipe-scrapers#2000](https://github.com/hhursev/recipe-scrapers/pull/2000)
  (fork `Naliath/recipe-scrapers`, branch `fix/dagelijksekost-title`). Until merged and
  released, the dedicated C# provider remains the better extractor for Dagelijkse Kost —
  another reason dedicated providers take precedence.
