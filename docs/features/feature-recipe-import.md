# Feature: Recipe Import / Scraping

> **Feature ID**: IMP-001
> **Status**: Planned
> **Priority**: High
> **Depends on**: Recipe Store, Infrastructure
> **Last Updated**: 2026-06-07

## 1. Feature Goal

Enable users to import recipes from external sources into Dishhive, starting with support for `dagelijksekost.vrt.be`. The architecture must be pluggable so additional sources can be added without major refactoring.

## 2. Scope

### In Scope (v1)
- Pluggable source-provider architecture
- Import from `dagelijksekost.vrt.be`
- Recipe data extraction and normalization
- Import preview and confirmation before saving
- Duplicate detection
- Import history tracking
- At least one automated test validating import extraction

### Out of Scope (v1)
- Bulk import from multiple URLs at once
- Scheduled/automated imports
- Recipe update sync from source
- OCR-based recipe import from images
- Support for additional sources beyond Dagelijkse Kost

## 3. User Stories / Use Cases

| ID | Story | Priority |
|----|-------|----------|
| IMP-US-001 | As a user, I want to paste a recipe URL and import it | Must |
| IMP-US-002 | As a user, I want to preview the imported recipe before saving | Must |
| IMP-US-003 | As a user, I want to edit the imported recipe before saving | Must |
| IMP-US-004 | As a user, I want to be warned if a similar recipe already exists | Should |
| IMP-US-005 | As a user, I want to see the source link on the imported recipe | Must |
| IMP-US-006 | As a user, I want to see import history | Could |

## 4. Domain Model

```
RecipeImportJob
├── Id: UUID
├── SourceUrl: string
├── SourceProvider: string
├── Status: enum (Pending, Processing, Completed, Failed)
├── ErrorDetails: string?
├── ImportedRecipeId: UUID? (FK -> Recipe)
├── DateCreated: DateTime
├── DateCompleted: DateTime?
└── RawResponse: JSON? (source-specific raw data for traceability)

ImportedRecipeModel (transient, pre-save)
├── Title: string
├── Description: string?
├── Ingredients: List<ImportedIngredient>
├── PreparationSteps: List<string>
├── Servings: int
├── ImageUrl: string?
├── VideoUrl: string?
├── SourceUrl: string
├── SourceName: string
├── PrepTimeMinutes: int?
├── CookTimeMinutes: int?
└── RawSourceData: JSON?
```

## 5. Research: Dagelijkse Kost API

### Initial Investigation (2026-06-07)

**URL**: `https://dagelijksekost.vrt.be/`

**API Discovery**:
- Attempted to find a formal public API
- Tested `https://dagelijksekost.vrt.be/api/v1/categories` — response pending analysis
- The site is part of VRT (Vlaamse Radio- en Televisieomroep), Belgium's public broadcaster
- Recipes are paired with TV show episodes

### Findings

| Aspect | Finding |
|--------|---------|
| Formal API exists | TBD - investigating |
| API documentation | TBD - investigating |
| Authentication required | TBD - investigating |
| Rate limits | TBD - investigating |
| Terms of service | Must review VRT usage policies |
| Recipe data availability | Recipes visible on site with ingredients, steps, media |

### Approach Decision

Regardless of whether a formal API exists:
1. **If a formal API exists and is appropriate**: Design a `DagelijkseKostApiProvider` that consumes the API
2. **If no formal API exists**: Design a `DagelijkseKostScraperProvider` that extracts data from HTML pages
3. **In both cases**: Use the same pluggable provider interface

### VRT API Notes

The VRT may provide internal APIs used by their frontend. These are:
- Potentially undocumented
- Subject to change without notice
- May have different ToS than public-facing features

**Recommendation**: If using internal APIs, treat them as a scraper (fragile, may break) and document this risk clearly.

## 6. Backend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| IMP-BE-001 | Pluggable source provider interface | Must |
| IMP-BE-002 | Dagelijkse Kost provider implementation | Must |
| IMP-BE-003 | Import job tracking | Must |
| IMP-BE-004 | Recipe data normalization | Must |
| IMP-BE-005 | Duplicate detection | Should |
| IMP-BE-006 | Import preview endpoint | Must |
| IMP-BE-007 | Import commit endpoint | Must |
| IMP-BE-008 | Raw source data preservation | Should |
| IMP-BE-009 | Error handling and retry | Should |
| IMP-BE-010 | Unit tests for extraction | Must |
| IMP-BE-011 | Integration tests for import flow | Must |

### API Endpoints

```
POST   /api/recipes/import/preview
POST   /api/recipes/import/commit/{previewId}
GET    /api/recipes/import/history
GET    /api/recipes/import/sources
GET    /api/recipes/import/status/{jobId}
```

## 7. Pluggable Provider Architecture

```
IRecipeSourceProvider
├── string SourceName { get; }
├── string[] SupportedUrlPatterns { get; }
├── bool CanHandle(string url)
├── Task<ImportedRecipeModel> FetchRecipeAsync(string url, CancellationToken ct)
├── Task<bool> IsAvailableAsync(CancellationToken ct)
└── ProviderMetadata Metadata { get; }

ProviderMetadata
├── string Name
├── string Description
├── string IconUrl?
├── string SourceWebsite
├── bool RequiresAuthentication
├── bool IsOfficialApi
└── string[] SupportedFeatures

// Registration
services.AddRecipeSourceProvider<DagelijkseKostProvider>();
services.AddRecipeSourceProvider<FutureProvider>();

// Provider factory
IRecipeSourceProvider ResolveProviderForUrl(string url);
```

### Why This Architecture

1. **Open/Closed Principle**: New providers added without modifying existing code
2. **Dependency Injection**: Each provider can have its own dependencies (HTTP client, scraper, API client)
3. **Testability**: Providers can be mocked individually
4. **Configuration**: Providers can be enabled/disabled via configuration
5. **Fallback**: Multiple providers can handle the same URL pattern

## 8. Dagelijkse Kost Provider Design

### Data Extraction Targets

| Field | Source | Notes |
|-------|--------|-------|
| Title | Page title / recipe heading | Required |
| Description | Recipe intro text | Optional |
| Ingredients | Ingredient list | Required |
| Preparation steps | Instructions section | Required |
| Servings | Serving indicator | Required, default to 4 if missing |
| Image | Recipe photo URL | Optional |
| Video | Embedded video URL | Optional, key feature of Dagelijkse Kost |
| Source link | Original recipe URL | Required |
| Prep time | Timing info | Optional |
| Cook time | Timing info | Optional |

### HTML Structure Considerations

The provider must handle:
- Current page structure
- Potential future structure changes
- Different recipe page URL patterns
- Missing or optional fields gracefully

## 9. Frontend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| IMP-FE-001 | URL input form | Must |
| IMP-FE-002 | Import progress indicator | Must |
| IMP-FE-003 | Recipe preview before save | Must |
| IMP-FE-004 | Edit imported recipe before save | Must |
| IMP-FE-005 | Duplicate warning dialog | Should |
| IMP-FE-006 | Import history view | Could |
| IMP-FE-007 | Supported sources indicator | Should |

## 10. Risks / Unknowns

| ID | Risk | Mitigation |
|----|------|------------|
| IMP-R001 | Dagelijkse Kost site structure changes | Abstract extraction; add version detection; monitor breaks |
| IMP-R002 | VRT ToS may restrict scraping | Review ToS; prefer official API if available; document compliance |
| IMP-R003 | Rate limiting from target site | Implement respectful delays; cache responses; respect robots.txt |
| IMP-R004 | Recipe data quality varies | Allow full manual editing after import |
| IMP-R005 | No formal API available | Scraper architecture with graceful degradation |
| IMP-R006 | Video URLs may be time-limited | Store video reference; note that availability may expire |

## 11. Phased Implementation Plan

### Phase 1 — Foundation
- [ ] Define `IRecipeSourceProvider` interface
- [ ] Implement provider registration system
- [ ] Create import job tracking
- [ ] Build preview/commit API flow

### Phase 2 — Dagelijkse Kost Provider
- [ ] Research and document VRT API availability
- [ ] Implement Dagelijkse Kost data extraction
- [ ] Handle all target fields
- [ ] Add error handling and fallbacks
- [ ] Write automated tests with sample data

### Phase 3 — Import UI
- [ ] URL input component
- [ ] Progress indicator
- [ ] Preview and edit flow
- [ ] Duplicate detection UI

### Phase 4 — Polish
- [ ] Import history
- [ ] Supported sources indicator
- [ ] Error messages and recovery
- [ ] Integration tests

## 12. Implementation Checklist

### Research
- [x] Identify target source (dagelijksekost.vrt.be)
- [ ] Determine if formal VRT API exists
- [ ] Review VRT terms of service
- [ ] Analyze HTML structure of recipe pages
- [ ] Document extraction strategy

### Backend
- [~] IRecipeSourceProvider interface (foundation in place via domain model)

### Frontend
- [ ] Import URL input component
- [ ] Import progress component
- [ ] Recipe preview component
- [ ] Duplicate warning dialog
- [ ] Import history component

### Testing
- [ ] Unit test: Dagelijkse Kost extraction with sample HTML
- [ ] Unit test: Provider resolution by URL
- [ ] Unit test: Duplicate detection logic
- [ ] Integration test: Full import flow
- [ ] Integration test: Error handling for unavailable source
- [ ] Test data fixtures for known recipes

## 13. Test Strategy

### Minimum Test Requirements

At least one automated test must validate recipe import/extraction for these expected fields:
- [ ] Title
- [ ] Description
- [ ] Ingredients
- [ ] Preparation steps
- [ ] Serving count
- [ ] Picture URL
- [ ] Video URL
- [ ] Source link

### Test Data

Use a known, stable recipe URL from dagelijksekost.vrt.be as test fixture. Capture and store the expected HTML response to avoid dependency on live site during testing.

### Test Approach

```csharp
// Example test structure
[Fact]
public async Task Import_DagelijkseKost_ExtractsAllExpectedFields()
{
    var provider = new DagelijkseKostProvider(...);
    var result = await provider.FetchRecipeAsync(TestRecipeUrl);
    
    Assert.NotNull(result.Title);
    Assert.NotNull(result.Description);
    Assert.NotEmpty(result.Ingredients);
    Assert.NotEmpty(result.PreparationSteps);
    Assert.True(result.Servings > 0);
    Assert.NotNull(result.ImageUrl);
    Assert.NotNull(result.VideoUrl);
    Assert.NotNull(result.SourceUrl);
}
```
