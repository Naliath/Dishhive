# Feature: Recipe Import / Scraping

**Created:** April 19, 2026  
**Status:** ✅ Implemented

---

## Feature Goal

Allow recipes to be imported from external websites. The initial supported source is **Dagelijkse Kost** (VRT, Belgian cooking show). The architecture must be pluggable so new sources can be added without major refactoring.

---

## Research: Dagelijkse Kost (dagelijksekost.vrt.be)

### Source Information

| Property | Value |
|----------|-------|
| URL | https://dagelijksekost.vrt.be/ |
| Language | Dutch (Flemish Belgian) |
| Content Type | Recipe website (TV show tie-in) |
| Operator | VRT (Vlaamse Radio- en Televisieomroeporganisatie) |

### API Research Findings

**Formal public API:** ❌ None found

VRT Dagelijkse Kost does not publish a formal public REST API for recipe data. The website is a standard server-rendered web application.

**Structured data investigation:**

Many modern recipe websites embed structured data using `schema.org/Recipe` in JSON-LD format within `<script type="application/ld+json">` tags. This is the standard approach for SEO and enables parsers to extract recipe data reliably.

Dagelijkse Kost uses standard HTML structure. A `schema.org/Recipe` JSON-LD block is present on recipe pages, providing a clean, structured extraction path without brittle HTML scraping.

**Chosen extraction approach:** Parse `schema.org/Recipe` JSON-LD from the page's `<script type="application/ld+json">` tags.

**Rationale:**
- JSON-LD structured data is far more stable than CSS class-based HTML scraping
- `schema.org/Recipe` is a standard spec — field names are predictable
- Still requires HTTP fetching (no API), but parsing is deterministic

**Fallback:** If JSON-LD is not present or incomplete, fall back to HTML parsing for key fields.

### schema.org/Recipe Fields Used

| schema.org field | Dishhive field | Notes |
|-----------------|----------------|-------|
| `name` | `Title` | Required |
| `description` | `Description` | May be absent |
| `recipeIngredient` | `Ingredients` | Array of strings |
| `recipeInstructions` | `Steps` | Array of HowToStep objects or strings |
| `recipeYield` | `Servings` | Often "4 personen" — parse number |
| `image` | `PictureUrl` | May be array; take first |
| `video.embedUrl` or `video.url` | `VideoUrl` | Optional |
| `url` or page URL | `SourceUrl` | Always stored |
| `author.name` | Stored in raw data | Traceability |

**Ingredient parsing note:** `recipeIngredient` values are strings like `"250 g bloem"`. Phase 1 stores them as-is in `Name` without quantity parsing. Phase 2 can add unit/quantity extraction.

### Test Fixture

A saved HTML snapshot of a known Dagelijkse Kost recipe page is used as a test fixture to ensure deterministic tests without network dependencies.

**Fixture file:** `src/Dishhive.Api.Tests/Fixtures/dagelijksekost-sample.html`

The fixture is verified against a known recipe and must include:
- A JSON-LD script block with `@type: "Recipe"`
- At least one ingredient
- At least one preparation step
- A serving count
- An image URL
- Source URL

---

## Scope

### In Scope
- Import from Dagelijkse Kost via JSON-LD extraction
- Pluggable provider architecture (new sources via `IRecipeSourceProvider`)
- Extracted recipe model covering all required fields
- Automated test using HTML fixture
- Storage of `SourceRawData` (original JSON-LD) for traceability

### Out of Scope (for this phase)
- Browser extension or bookmarklet
- Bulk import
- Authentication against source sites
- Ingredient quantity/unit parsing (stored as strings)
- Video downloading

---

## User Stories

1. **As a meal planner**, I want to paste a Dagelijkse Kost URL and have the recipe imported automatically.
2. **As a meal planner**, I want to review and confirm the imported recipe before saving.
3. **As a developer**, I want to add a new recipe source by implementing one interface without touching existing code.

---

## Domain Model Considerations

See `05-recipe-store.md` for the `Recipe` entity. Import produces a `Recipe` with:
- `SourceUrl` = the input URL
- `SourceName` = the provider name (e.g., "DagelijkseKost")
- `SourceRawData` = raw JSON-LD string for traceability
- `OriginalQuantity` / `OriginalUnit` on each ingredient = source values (Phase 2)

---

## Architecture: Source Provider Pattern

```csharp
/// <summary>
/// Implemented by each recipe import source.
/// </summary>
public interface IRecipeSourceProvider
{
    /// <summary>Display name of this source (e.g., "DagelijkseKost").</summary>
    string SourceName { get; }

    /// <summary>Returns true if this provider can handle the given URL.</summary>
    bool CanHandle(string url);

    /// <summary>Fetches and parses a recipe from the given URL.</summary>
    Task<ImportedRecipeDto?> ImportFromUrlAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Orchestrates import by selecting the correct provider for a URL.
/// </summary>
public interface IRecipeImportService
{
    Task<ImportedRecipeDto?> ImportAsync(string url, CancellationToken cancellationToken = default);
}
```

### Registration

```csharp
// Program.cs
builder.Services.AddScoped<IRecipeSourceProvider, DagelijksekostSourceProvider>();
// Add more providers here as new sources are supported:
// builder.Services.AddScoped<IRecipeSourceProvider, AnotherSiteSourceProvider>();

builder.Services.AddScoped<IRecipeImportService, RecipeImportService>();
```

`RecipeImportService` iterates registered `IRecipeSourceProvider` instances, calls `CanHandle(url)`, and delegates to the matching provider.

---

## Imported Recipe DTO

```csharp
public record ImportedRecipeDto(
    string Title,
    string? Description,
    IEnumerable<ImportedIngredientDto> Ingredients,
    IEnumerable<string> Steps,
    int? Servings,
    string? PictureUrl,
    string? VideoUrl,
    string SourceUrl,
    string SourceName,
    string? SourceRawData
);

public record ImportedIngredientDto(
    string RawText,          // Original string from source
    string Name,             // Parsed name (same as RawText in Phase 1)
    decimal? OriginalQuantity,
    string? OriginalUnit
);
```

---

## Backend Requirements

- `DagelijksekostSourceProvider` implementing `IRecipeSourceProvider`
  - `CanHandle`: returns true for `dagelijksekost.vrt.be` URLs
  - `ImportFromUrlAsync`: fetches page, extracts JSON-LD, maps to `ImportedRecipeDto`
- `RecipeImportService` orchestrating providers
- `POST /api/recipes/import` — accepts `{ url: string }`, returns `ImportedRecipeDto` (preview before save)
- `POST /api/recipes/import/save` — accepts `ImportedRecipeDto`, saves as `Recipe`

---

## Frontend Requirements

- Import dialog: URL input → loading state → recipe preview → save/discard
- Preview shows: title, description, ingredients, steps, servings, image
- Error state for unsupported URLs or parse failures

---

## Automated Test Requirements

**File:** `src/Dishhive.Api.Tests/Services/RecipeImportServiceTests.cs`

Minimum test cases:
- `ImportAsync_WithDagelijksekostFixture_ExtractsTitle` 
- `ImportAsync_WithDagelijksekostFixture_ExtractsDescription`
- `ImportAsync_WithDagelijksekostFixture_ExtractsIngredients`
- `ImportAsync_WithDagelijksekostFixture_ExtractsSteps`
- `ImportAsync_WithDagelijksekostFixture_ExtractsServings`
- `ImportAsync_WithDagelijksekostFixture_ExtractsPictureUrl`
- `ImportAsync_WithDagelijksekostFixture_ExtractsSourceLink`
- `ImportAsync_WithUnsupportedUrl_ReturnsNull`
- `ImportAsync_WithHttpError_ReturnsNull`
- `ImportAsync_WithMalformedHtml_ReturnsNull`

---

## Risks / Unknowns

- VRT may change the page structure or remove JSON-LD — tests with fixtures detect this before deployment.
- Some recipes may have incomplete JSON-LD — test with real pages and handle gracefully.
- Copyright: importing recipe content for personal household use is reasonable use; the app should not re-publish scraped content publicly.
- If VRT adds bot protection (Cloudflare, etc.), scraping may fail in production.

---

## Phased Implementation Plan

### Phase 1 — Dagelijkse Kost Import (JSON-LD)
- [x] Research VRT Dagelijkse Kost API → no formal API found, JSON-LD extraction chosen
- [x] Architecture design (provider pattern documented)
- [ ] `IRecipeSourceProvider` interface created
- [ ] `IRecipeImportService` interface created
- [ ] `RecipeImportService` implementation
- [ ] `DagelijksekostSourceProvider` implementation
- [ ] HTML fixture created (`dagelijksekost-sample.html`)
- [ ] Automated tests for import extraction
- [ ] Import endpoint in `RecipesController`

### Phase 2 — Additional Sources
- [ ] Define next source provider
- [ ] Add provider without modifying existing code

---

## Implementation Checklist

### Backend
- [x] Feature plan created
- [x] API research documented
- [x] Architecture decision documented
- [x] `IRecipeSourceProvider` interface created
- [x] `IRecipeImportService` interface created
- [x] `RecipeImportService` implementation created
- [x] `DagelijksekostSourceProvider` implementation created
- [x] HTML fixture created
- [x] Automated tests created and passing
- [x] Import endpoint fully wired to recipe save (`POST /api/recipes/import`)

### Frontend
- [x] Recipe import dialog (`ImportRecipeDialog`) created
- [x] Import URL input + loading state + error handling
- [x] Recipe preview before save
