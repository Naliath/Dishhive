# Feature: Recipe Import

## Goal
Import recipes from external websites — initially `dagelijksekost.vrt.be` — using a pluggable architecture that lets us add more sources without touching existing code.

See [research note](../research/RECIPE_IMPORT_RESEARCH.md) for the investigation that justified scraping rather than using a formal API.

## Scope
- Pluggable recipe-source provider pattern.
- One concrete provider: `DagelijkseKostRecipeProvider`.
- Two-step UX: **preview** (URL → parsed recipe shown to user) → **save** (creates a `Recipe`).
- Preserve the source-specific raw payload for traceability and manual correction.
- Automated test using a captured HTML fixture.

## Architecture

```
RecipeImportController
   ↓
IRecipeImportService
   ↓
RecipeSourceRegistry  ← resolves provider by URL host
   ↓
IRecipeSourceProvider (one per source)
   • DagelijkseKostRecipeProvider
   • <future providers>
```

### Interfaces (sketch)
```csharp
public interface IRecipeSourceProvider
{
    string ProviderKey { get; }                 // "dagelijksekost"
    bool CanHandle(Uri url);
    Task<ImportedRecipe> FetchAsync(Uri url, CancellationToken ct = default);
}

public sealed record ImportedRecipe(
    string Title,
    string? Description,
    int Servings,
    string? ImageUrl,
    string? VideoUrl,
    Uri SourceUrl,
    string ProviderKey,
    string SourceRawPayload,                    // JSON / text dump preserved verbatim
    IReadOnlyList<ImportedIngredient> Ingredients,
    IReadOnlyList<ImportedStep> Steps,
    IReadOnlyList<string> Tags);

public sealed record ImportedIngredient(
    int Order,
    string Name,
    decimal? Quantity,
    string? Unit,
    decimal? OriginalQuantity,
    string? OriginalUnit,
    string? Section,
    string? Note);

public sealed record ImportedStep(int Order, string Text);
```

### Provider implementation strategy (DagelijkseKost)
1. Fetch HTML.
2. **Strategy A (preferred):** look for `<script type="application/ld+json">` blocks containing a schema.org `Recipe`. Map fields directly.
3. **Strategy B (fallback):** parse the DOM for the title/ingredients/instructions sections. Used only when no JSON-LD is present.
4. Always store the chosen raw payload (`SourceRawPayload`) to allow re-import or manual correction.

### Unit conversion
`IUnitConversionService` is invoked when the user's measurement system differs from the recipe's source. Original value+unit are always preserved on the entity.

## Endpoints
- `POST /api/recipe-import/preview` — body `{ url: string }`, returns `ImportedRecipeDto`.
- `POST /api/recipe-import/save` — body is the previewed `ImportedRecipeDto` (possibly user-edited), returns the created recipe id.

## Frontend requirements
- `pages/recipe-import/` with: URL input → "Preview" → editable form → "Save".
- Form mirrors the recipe edit form so user corrections feel familiar.

## Risks / unknowns
- Site layout changes can break Strategy B. Strategy A (JSON-LD) is robust to layout changes.
- Anti-bot / Cloudflare-style challenges are not currently in place but could appear.
- HTML parsing dependency: `HtmlAgilityPack` (well-known, BSD-licensed) is selected.

## Phased plan
1. Provider interface + registry.
2. `DagelijkseKostRecipeProvider` (JSON-LD strategy).
3. DOM fallback strategy.
4. Service + controller.
5. Test with HTML fixture.
6. Angular preview/save UI.
7. Pluggable registration so a future provider is one DI registration.

## Implementation checklist
- [x] `IRecipeSourceProvider` + `ImportedRecipe` shape
- [x] `RecipeSourceRegistry`
- [x] `DagelijkseKostRecipeProvider` — JSON-LD strategy
- [x] `IRecipeImportService` + `RecipeImportService`
- [x] `RecipeImportController` (preview + save)
- [x] Test fixture under `Dishhive.Api.Tests/Fixtures/`
- [x] Provider unit test asserting all required fields
- [ ] DOM fallback strategy (when no JSON-LD)
- [x] Angular import UI
- [x] Save persists `Recipe` aggregate via `RecipesController`
