# Feature: Recipe Import / Scraping

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [recipe-store.md](recipe-store.md), [measurement-preferences.md](measurement-preferences.md),
[recipe-import-export.md](recipe-import-export.md) (file-based library exchange reusing this extractor)

## Feature Goal

Import recipes into the recipe store from external sources — first source:
**Dagelijkse Kost** (`https://dagelijksekost.vrt.be/`) — through a pluggable source-provider
architecture so additional sources can be added later without refactoring.

## Research: does Dagelijkse Kost have a formal API?

**Investigated June 10, 2026. Conclusion: no formal/public API exists. JSON-LD extraction is
the sanctioned approach.**

Findings:

1. **No documented API.** VRT publishes no public API for Dagelijkse Kost. The site is a
   Next.js application backed by Firebase (recipe images are served from
   `storage.googleapis.com/dagelijkse-kost-prod-*` with Firebase service-account signed URLs).
   Its internal data endpoints (Next.js RSC payloads) are undocumented, obfuscated, and
   unstable across deployments — not suitable as an integration contract.
2. **Every recipe page embeds schema.org `Recipe` JSON-LD** (`<script type="application/ld+json">`),
   verified on a live recipe page. Available fields: `name`, `description`, `image`,
   `thumbnailUrl`, `recipeYield` (servings), `recipeIngredient[]` (e.g. `"200 gram suiker"`),
   `recipeInstructions[]` (`HowToStep` objects), `prepTime`/`cookTime`/`totalTime` (ISO-8601
   durations), `recipeCategory`, `keywords[]`, `author`, `inLanguage`, `datePublished`.
   `video` (schema.org `VideoObject`) is optional and absent on some pages; mapped when present.
3. **Prior art:** the widely used [recipe-scrapers](https://github.com/hhursev/recipe-scrapers)
   project supports dagelijksekost.vrt.be via the same structured-data extraction (added in
   release 15.4.0) — confirming there is no better machine interface.
4. Recipe URLs follow `https://dagelijksekost.vrt.be/gerechten/{slug}`.
5. **JSON-LD truncates the instruction list** (verified June 2026): `recipeInstructions`
   only contains the first two steps. The complete list lives in the page's Next.js flight
   payload (`self.__next_f.push` script chunks) as an `"instructions"` map keyed by step
   index. The provider decodes that payload for the full steps and falls back to the
   JSON-LD steps when the payload structure changes.

**Decision:** import = fetch a single user-supplied recipe URL on demand, extract the
schema.org `Recipe` JSON-LD, map to Dishhive's model, and store the raw JSON-LD for
traceability. JSON-LD is intended for machine consumption (search engines), making this the
most stable and respectful extraction surface. No crawling, no bulk scraping.

## Scope

**In scope**
- Paste-a-URL import flow (single recipe, on demand)
- Pluggable provider architecture (`IRecipeSourceProvider`)
- Generic schema.org JSON-LD extractor as the shared engine
- Dagelijkse Kost provider built on the generic extractor
- Imported model: title, description, ingredients, steps, servings, picture, video link,
  source link, raw source data
- Ingredient line parsing (quantity/unit/name) with original text preserved
- Automated tests against a stored HTML fixture (no network in tests)

**Out of scope**
- Bulk import / crawling
- Browser-extension or share-target import
- Image downloading/re-hosting

## Architecture: pluggable source providers

```
POST /api/recipes/import { url }
        │
        ▼
RecipeImportService
        │  selects the FIRST provider whose CanHandle(url) matches
        ▼
IRecipeSourceProvider                       // one per source, registration order matters
├── DagelijkseKostProvider                  // key "dagelijkse-kost" (dedicated, wins)
│       └── uses SchemaOrgRecipeExtractor   // shared JSON-LD engine
└── RecipeScrapersFallbackProvider          // key "recipe-scrapers", registered LAST
        └── RecipeScrapersClient → scraper sidecar container (Python recipe-scrapers,
            src/dishhive-scraper; any http(s) site when RecipeScrapers:BaseUrl is set)

provider returns ImportedRecipe (provider-agnostic)
        │
        ▼
IngredientLineParser (quantity/unit/name, locale-aware: "0,5" decimal comma)
        │
        ▼
mapped to Recipe + RecipeIngredient + RecipeStep, SourceRawData = raw JSON-LD
```

Sites with a dedicated provider never reach the fallback; the sidecar only handles
sites Dishhive has no own implementation for (see
[RECIPE_SCRAPERS_ADOPTION_PLAN.md](../plans/RECIPE_SCRAPERS_ADOPTION_PLAN.md)).
The sidecar's status, installed recipe-scrapers version, update check (PyPI), and
one-click package update live in the settings page integrations widget
(`GET /api/integrations/status`, `GET/POST /api/integrations/scraper/...`).

```csharp
public interface IRecipeSourceProvider
{
    string Key { get; }                        // "dagelijkse-kost"
    bool CanHandle(Uri url);
    Task<ImportedRecipe> ExtractAsync(string html, Uri sourceUrl, CancellationToken ct);
}
```

- Providers are pure extractors: HTML in, `ImportedRecipe` out. HTTP fetching lives in
  `RecipeImportService` (single `HttpClient`, configured User-Agent) so providers stay
  trivially unit-testable against fixtures.
- Adding a source = one new provider class + DI registration + fixture test. No changes to
  the import service, controller, or frontend.

### ImportedRecipe model

| Field | Source (Dagelijkse Kost) |
|-------|--------------------------|
| Title | JSON-LD `name` |
| Description | JSON-LD `description` |
| Ingredients (raw lines) | JSON-LD `recipeIngredient[]` |
| Steps | Next.js payload `instructions` map (complete); JSON-LD `recipeInstructions[].text` as fallback (truncated to 2 on this site) |
| Servings | JSON-LD `recipeYield` |
| ImageUrl | JSON-LD `image`; the image bytes are downloaded at import time and stored locally (see recipe-store.md) |
| VideoUrl | JSON-LD `video.contentUrl` when present, else null |
| SourceUrl | canonical `@id` / requested URL |
| Prep/Cook/Total time | ISO-8601 durations parsed to minutes |
| Category / Keywords | `recipeCategory` / `keywords` |
| RawData | the verbatim JSON-LD `Recipe` object |

## User Stories / Use Cases

1. As a planner, I paste a Dagelijkse Kost URL and get a fully populated recipe to review/save.
2. As a planner, I correct any mis-parsed ingredient before/after saving (original text shown).
3. As a developer, I add a new recipe site by writing one provider class and one fixture test.
4. As a planner, re-importing the same URL updates the existing recipe instead of duplicating it.

## Backend Requirements

- `POST /api/recipes/import` `{ url }` → extracted recipe (saved; returns recipe DTO).
  Duplicate `SourceUrl` ⇒ update existing
- `RecipeImportService` (fetch + provider selection + persistence), HtmlAgilityPack or regex-free
  JSON-LD extraction (prefer parsing only the `ld+json` script blocks with `System.Text.Json`)
- `IngredientLineParser`: handles `"200 gram suiker"`, decimal commas (`"0,5 citroenen"`),
  unit-less lines (`"Cointreau"`); unparseable ⇒ quantity/unit null, name = full line
- Errors: 400 invalid/unsupported URL, 422 page fetched but no recipe data found

## Frontend Requirements

- Import entry point on the recipes page ("Import from URL" button + dialog)
- Post-import review: navigate to the saved recipe detail with an "imported — check values" hint

## Integration Requirements

- Produces `Recipe` rows per recipe-store.md model
- Normalization honors measurement preferences (metric default; originals preserved)

## Testing (required)

Automated provider test against a **stored HTML fixture** of a real Dagelijkse Kost page
(`Fixtures/dagelijkse-kost-recipe.html`) validating: title, description, ingredients (count +
known lines), steps, serving count, picture URL, video link mapping, and source link.
Plus `IngredientLineParser` unit tests. Tests are offline — no network dependency.

## Risks / Unknowns

- Site redesign could drop or change JSON-LD → fixture tests detect drift; raw data allows
  re-parsing; provider isolation limits blast radius.
- The Next.js payload structure (used for the full step list) is less stable than JSON-LD —
  mitigated by the JSON-LD fallback and the fixture test asserting all 11 steps.
- ~~Signed image URLs may expire eventually~~ Resolved: image bytes are downloaded at import
  time and stored locally; the source URL only remains as fallback/traceability.
- Ingredient lines are Dutch and sometimes unit-less or vague ("0,5 bakje rode bessen") —
  parser is best-effort by design; original text always preserved.

## Phased Implementation Plan

**Phase 1 — Extraction engine + provider + tests** (can precede UI)
- `SchemaOrgRecipeExtractor`, `DagelijkseKostProvider`, `IngredientLineParser`, fixture tests

**Phase 2 — Import endpoint**
- `RecipeImportService`, controller action, duplicate handling

**Phase 3 — Import UI**
- Dialog + review flow

## Implementation Checklist

- [x] HTML fixture of a real Dagelijkse Kost recipe page stored in test project
- [x] `IRecipeSourceProvider` + `ImportedRecipe` contracts
- [x] `SchemaOrgRecipeExtractor` (JSON-LD parsing)
- [x] `DagelijkseKostProvider`
- [x] `IngredientLineParser` (decimal comma, units, unit-less)
- [x] Automated fixture test: title/description/ingredients/steps/servings/picture/video/source
- [x] `IngredientLineParser` unit tests
- [x] `RecipeImportService` + `POST /api/recipes/import` + duplicate-URL update
- [x] Full step extraction from Next.js payload (JSON-LD is truncated to 2 steps) + fixture test
- [x] Local image download at import (tolerant of failures; original URL kept) + tests
- [x] Import pipeline tests with mocked HTTP (`RecipeImportServiceTests`)
- [x] Import endpoint integration test (full HTTP pipeline, mocked outbound fetch:
      created recipe, local image serving, unsupported source, unreachable page)
- [x] Import form on recipes page (URL input + navigate to imported recipe)
- [x] recipe-scrapers sidecar container (`src/dishhive-scraper`, FastAPI wrapper) as
      fallback provider for sites without a dedicated implementation
- [x] Scraper integration status + package version check/update in settings widget
