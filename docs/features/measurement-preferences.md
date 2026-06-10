# Feature: Measurement Preferences

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [recipe-store.md](recipe-store.md), [recipe-import.md](recipe-import.md),
[shopping-list-export.md](shopping-list-export.md)

## Feature Goal

A settings-page capability to choose the household's default measurement system — **metric
(default)** or **imperial** — applied consistently to recipe display, import normalization,
and shopping lists, while always preserving original source values for manual correction.

## Scope

**In scope**
- Setting stored server-side (key-value `UserSetting`, Freezy's `SettingsController` pattern)
- Settings page with metric/imperial toggle (metric default)
- Normalization model on `RecipeIngredient` (normalized + original values)
- Conversion helpers used at import time and display time

**Out of scope**
- Per-recipe unit overrides
- Temperature conversion in instruction text (free text stays verbatim)
- Locale/translation of unit names beyond a simple display map

## The chosen model (documented decision)

Each `RecipeIngredient` stores **two layers** (see recipe-store.md for full schema):

| Layer | Fields | Meaning |
|-------|--------|---------|
| Normalized | `Quantity`, `Unit` | Canonical **metric** values used by Dishhive everywhere (math, aggregation, scaling, shopping lists) |
| Original | `OriginalText`, `OriginalQuantity`, `OriginalUnit` | Verbatim source line + parsed source values; never modified by conversion |

Rules:

1. **Storage is always metric.** The preference is a *display* concern; the database is
   canonical. Imperial sources are converted to metric at import time.
2. **Originals are immutable.** Conversion or re-normalization never touches the original
   fields, so a human can always verify and correct against the source.
3. **Display converts on the way out.** Imperial preference ⇒ normalized metric values are
   converted in the frontend display layer (single `MeasurementService`); the API always
   serves metric + originals.
4. **Unparseable lines degrade gracefully.** `Quantity`/`Unit` null, `Name` = full line,
   `OriginalText` shown as-is. Shopping lists pass such lines through verbatim.

Canonical units: `g`, `kg`, `ml`, `l`, `piece`, plus pass-through for culinary units that have
no system (`snuifje`/pinch, `blaadje`/leaf, `el`/`tl` spoons kept as-is — converting spoons is
noise, not value).

## User Stories / Use Cases

1. As a household, our default is metric without configuring anything.
2. As a user, I switch to imperial in settings and recipe views/shopping lists show oz/lb/cups.
3. As a user, I see the original source line ("2 cups flour") next to the normalized value
   (240 g) and can correct a bad parse.
4. As the import pipeline, I convert imperial source recipes to metric automatically.

## Domain Model Considerations

```
UserSetting            // identical to Freezy's pattern
├── Key (PK, string, max 100)      // "measurementSystem"
├── Value (string, max 1000)       // "metric" | "imperial"
├── CreatedAt / UpdatedAt
```

Missing row ⇒ metric (default-by-absence, no seeding needed).

## Backend Requirements

- `SettingsController`: `GET /api/settings`, `PUT /api/settings/{key}` (Freezy-compatible shape)
- `UnitConversion` helper (static, tested): imperial→metric for mass/volume at import time
- Import pipeline calls conversion when source units are imperial

## Frontend Requirements

- Page `pages/settings/` — measurement system toggle (radio/segmented, metric preselected)
- `settings.service.ts` with cached signal of current preference
- `MeasurementService` (display conversion + formatting, e.g. 1 decimal, unit symbols)
- Recipe detail + shopping list render through `MeasurementService`

## Integration Requirements

- recipe-import.md: normalization step at import
- shopping-list-export.md: aggregation in metric, display in preferred system

## Risks / Unknowns

- Volume↔mass conversions ("1 cup flour" → grams) are ingredient-dependent — **not** attempted;
  cups convert to ml only. Density tables are a possible future feature.
- Mixed-unit aggregation on shopping lists (g + "bakje") — unparseable units aggregate as
  separate lines.

## Phased Implementation Plan

**Phase 1 — Setting plumbing**
- `UserSetting` entity (part of initial schema), settings endpoints, settings page with toggle

**Phase 2 — Conversion layer**
- `UnitConversion` + import-time normalization + tests

**Phase 3 — Display layer**
- `MeasurementService` + imperial rendering in recipe/shopping views

## Implementation Checklist

- [x] `UserSetting` entity + migration
- [x] `SettingsController` + DTOs + integration tests
- [x] Settings page with metric/imperial toggle (metric default)
- [x] `settings.service.ts`
- [x] `UnitConversion` helper + unit tests (via `IngredientLineParserTests`)
- [x] Import-time normalization wired into import pipeline
- [x] `MeasurementService` display conversion (g→oz, kg→lb, ml→fl oz, l→qt; culinary units pass through)
- [x] Recipe detail/shopping list honor preference (loaded once at app startup)
