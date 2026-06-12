# Feature: Recipe Library Import / Export

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [recipe-store.md](recipe-store.md), [recipe-import.md](recipe-import.md)

## Feature Goal

Back up the recipe library to a single file and restore or merge it elsewhere — moving to
a new instance, sharing recipes with another household, or migrating from/to other recipe
managers. Initiated from the settings page.

## Format research (June 2026)

| Candidate | Verdict |
|---|---|
| **schema.org Recipe JSON** | ✅ Chosen. The machine-readable format every recipe site embeds and the lingua franca of Mealie, Tandoor and Nextcloud Cookbook. Dishhive's URL import pipeline already parses it (`SchemaOrgRecipeExtractor`), so import shares one battle-tested code path. |
| Mealie/Tandoor native zips | ❌ Tool-specific, undocumented, versioned — a moving target. Both tools accept schema.org JSON anyway. |
| Paprika `.paprikarecipes` | ❌ Proprietary gzip format; only relevant for Paprika users. |
| RecipeML / MasterCook MX2 | ❌ Legacy XML, effectively dead. |

## Design

**Export — `GET /api/recipes/export`** downloads `dishhive-recipes-YYYY-MM-DD.json`:

```json
{
  "@context": "https://schema.org",
  "@graph": [
    {
      "@type": "Recipe",
      "name": "…", "description": "…", "recipeYield": 4,
      "prepTime": "PT20M", "cookTime": "PT45M",
      "recipeCategory": "…", "keywords": "…", "url": "…source…",
      "image": "data:image/jpeg;base64,…",
      "recipeIngredient": ["1 kg rundsvlees", "…"],
      "recipeInstructions": [{ "@type": "HowToStep", "text": "…" }],
      "dateCreated": "…", "dateModified": "…",
      "dishhive:tags": ["winter", "klassieker"]
    }
  ]
}
```

- **Self-contained**: locally stored images are embedded as data URIs (other tools accept
  or ignore them; Dishhive restores them to local bytes on import).
- Ingredient lines use the verbatim `OriginalText` (the canonical interchange form);
  composed from quantity/unit/name when a manual recipe has none.
- Organization tags travel in a `dishhive:tags` extension property other tools ignore.

**Import — `POST /api/recipes/import/file`** (multipart) accepts any schema.org Recipe
JSON: a single object, an array, or an `@graph` document — including Dishhive's own export
and files saved from other recipe managers. Per recipe:

| Situation | Outcome |
|---|---|
| Source URL matches an existing recipe | **Updated** (consistent with re-importing a URL) |
| Title already in the library (case-insensitive) | **Skipped** — local edits win |
| Otherwise | **Created** (`SourceProvider = "file-import"`) |

- Ingredient lines re-parse through `IngredientLineParser` (same as URL import).
- Data-URI images become locally stored bytes; remote image URLs are downloaded
  best-effort (failure never fails the import).
- Response reports created/updated/skipped counts with per-recipe skip reasons.
- Errors: 400 not JSON / no file, 422 JSON without any Recipe objects.

**Frontend** — settings page "Recipe library backup" card: an export download button and
an import button (hidden file input). The result snackbar summarizes counts; skipped
recipes are listed underneath with their reasons.

## Risks / Notes

- Title-based dedupe means renamed recipes import as new ones — acceptable; deletion is
  manual and visible.
- schema.org has no structured quantity/unit per ingredient, so re-import re-parses the
  lines; manual corrections to parsed values (not the original text) don't round-trip.
- Export holds the whole serialized library in memory (images ×1.33 base64) — fine at
  household scale.

## Implementation Checklist

- [x] `SchemaOrgRecipeExtractor.FindRecipeNodes` / `MapRecipeNode` (multi-recipe, no page URL)
- [x] `RecipeExchangeService`: schema.org export (data-URI images, `dishhive:tags`)
- [x] `RecipeExchangeService`: file import (dedupe by URL/title, tag sync, image decode/download)
- [x] `GET /api/recipes/export` + `POST /api/recipes/import/file` + result DTO
- [x] Integration tests: export shape, own-export round-trip, create/update/skip paths,
      data-URI image, invalid JSON, no-recipe JSON
- [x] Settings page backup card (export download, import picker, skipped list)
