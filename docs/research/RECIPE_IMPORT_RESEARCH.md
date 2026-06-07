# Recipe Import Research — `dagelijksekost.vrt.be`

**Status:** initial investigation, April 2026.

## Question
> Does dagelijksekost.vrt.be expose a formal/public recipe API we can use, or do we need to scrape HTML?

## Findings
- The site is a Next.js application (`/_next/...` assets are visible) with **server-rendered HTML** for individual recipe pages under `https://dagelijksekost.vrt.be/gerechten/<slug>`.
- No public, documented REST or GraphQL API is advertised on the site or its associated developer portals (`vrt.be`, `vrt.be/over-de-vrt`, ...). VRT mobile apps consume internal endpoints under `*.vrt.be` that are not part of any contract we can rely on.
- The HTML page contains:
  - The recipe title in a `<h1>` / `<h2>` heading.
  - Ingredients in a structured ingredients section (per-section grouping such as "Rayon" / "Deelgerechten").
  - Numbered cooking instructions in an instructions section.
  - A serving count near the ingredients header (e.g. "4 personen").
  - A hero image hosted on `cdn.dagelijksekost.tv` (Firebase storage).
- Many recipe pages on the site emit a `schema.org`-style JSON-LD `Recipe` block inside `<script type="application/ld+json">`. Where present, this is the most reliable extraction path: it gives `name`, `description`, `image`, `recipeIngredient[]`, `recipeInstructions[]`, `recipeYield`, `video`, and `author` in a stable shape.
- A small fraction of pages do not include JSON-LD (older or featured pages). A DOM-based fallback is therefore needed.

## Decision
- **No formal public API is available.** We import via the HTML page.
- We adopt a **two-strategy extraction pipeline** within a single `DagelijkseKostRecipeProvider`:
  1. Parse `application/ld+json` Recipe blocks (preferred — schema.org standard, robust to layout changes).
  2. DOM-based fallback for pages without JSON-LD (best-effort title / ingredients / steps).
- The provider preserves the **raw extracted source payload** (the JSON-LD object or a structured DOM dump) on the persisted recipe so users can manually correct mis-imports without re-scraping.
- Architecture: `IRecipeSourceProvider` interface + `RecipeSourceRegistry` selects the right provider for a given URL host. Adding a future source means adding one class, no refactor.

## Compliance & politeness
- Identify ourselves with a descriptive `User-Agent` (`Dishhive/<version> (+local recipe import)`).
- Respect `robots.txt`. The current `dagelijksekost.vrt.be/robots.txt` does not disallow `/gerechten/`.
- Rate-limit imports to 1 request/second per host (only invoked manually via the UI in v1, so this is comfortable).
- We never republish the scraped content beyond the user's local Dishhive instance.

## Test approach
- Capture a representative recipe page as an HTML fixture under `src/Dishhive.Api.Tests/Fixtures/`.
- Run the provider's parser against the fixture without touching the network.
- Assert: title, description, ingredients, steps, serving count, image, video link (where present), source link, raw source payload preserved.

## Future improvements
- Add providers for additional sources (Mealie-style imports, NYT cooking, AllRecipes, etc.) once the abstraction has been used a second time and the seam is validated.
- Consider an out-of-process scraper if anti-bot measures appear.
- If VRT publishes a formal API later, swap the provider implementation while keeping the interface.
