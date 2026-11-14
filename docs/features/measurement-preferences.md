# Feature: Measurement Preferences

**Status legend:** `[ ]` = new · `[~]` = in progress · `[x]` = done
**Related:** [recipe-store.md](recipe-store.md), [recipe-import.md](recipe-import.md),
[shopping-list-export.md](shopping-list-export.md),
[ingredient-aware conversion research and plan](../plans/INGREDIENT_MEASUREMENT_CONVERSION_PLAN.md)

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
| Structured | `Quantity`, `Unit` | Metric values, count/culinary units, or retained cups; used for math, scaling and shopping lists |
| Original | `OriginalText`, `OriginalQuantity`, `OriginalUnit` | Verbatim source line + parsed source values; never modified by conversion |

Rules:

1. **The preference is a display concern.** Mass units are normalized to metric at import.
   Cups stay cups unless the publisher provides a metric alternative. Ingredient-specific
   cup conversions happen at display time using the editable household list.
2. **Originals are immutable.** Conversion or re-normalization never touches the original
   fields, so a human can always verify and correct against the source.
3. **Display converts on the way out.** Imperial preference ⇒ normalized metric values are
   converted in the frontend display layer (single `MeasurementService`); the API always
   serves structured quantities + originals.
4. **Unparseable lines degrade gracefully.** `Quantity`/`Unit` null, `Name` = full line,
   `OriginalText` shown as-is. Shopping lists pass such lines through verbatim.

Canonical units: `g`, `kg`, `ml`, `l`, `piece`, `cup`, plus pass-through for culinary units that have
no system (`snuifje`/pinch, `blaadje`/leaf, `el`/`tl` spoons keep their quantities). Culinary
unit labels follow the interface language through `measurementUnit` translation messages;
English displays `el` as `tbsp` and `tl` as `tsp` in either measurement system.

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

- Cup conversions use a small, user-editable list of approximate amounts. Unlisted ingredients
  remain in cups; there is no generic cup-to-ml fallback or guessed density.
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
- [x] Editable common-ingredient cup conversions, US customary basis, without new dependencies
- [x] Fraction parsing and safe read projection of recognizable older imports
- [x] Generic extraction of published WP Recipe Maker metric alternatives

## Common-ingredient cup conversions

Settings provides a small initial list with names, exact aliases, a metric amount per US cup,
and a unit (`g` or `ml`). Users can change amounts, add/remove ingredients, or save an empty
list to disable these conversions. The list is validated and stored in the existing
`UserSetting` table under `cupConversions`; no schema migration or new library is needed.
Preferences include the effective list so it is also available from the existing offline cache.

The default list covers flour, sugars, butter, oats, almond flour, and common liquids.
Weight starting points are approximate references from
[King Arthur Baking's chart](https://www.kingarthurbaking.com/learn/ingredient-weight-chart).
Liquid entries use the US customary cup volume (approximately 236.59 ml), not a mass density.
Only a full ingredient name or alias matches, ignoring case and repeated whitespace. For
example, `almond flour` does not match `flour`, and `frozen corn` stays in cups unless the user
adds it. Users can include English and Dutch names in the same entry.

Metric display multiplies cups by the configured amount; Imperial display divides compatible
metric quantities by that amount. Other metric quantities retain the existing oz/lb/fl oz/qt
display. The shared formatter is used by recipes, scaled servings, shopping lists and copied
text. Changing a conversion does not rewrite recipes. Mixed units remain separate shopping
rows instead of being combined through an assumed ingredient equivalence.

`IngredientLineParser` accepts Unicode fractions (`½`, `¼`, etc.), ASCII fractions (`1/2`),
mixed numbers (`1½`, `1 1/2`), and attached metric units (`50g`). Parsed decimal quantities
scale with servings. Ambiguous ranges and package quantities remain verbatim.
The API also projects untouched older fraction lines into structured quantities, and reverses
recognizable old cup-to-240-ml imports using preserved originals. It does not change database
rows or overwrite a quantity that differs from the old conversion. Publisher metric payloads
are excluded from this legacy rule.

The scraper reads published metric alternatives from WP Recipe Maker data without running
JavaScript or branching on a hostname. It requires one unambiguous recipe and alignment with
the extracted ingredient lines; missing alternatives fall back individually. New imports
store published metric amounts while preserving original text/quantity/unit. Existing imports
need a source refresh to recover alternatives that were not previously retained.

The [AI translation experiment](../research/CUP_TRANSLATION_EVALUATION.md) found that the model
can follow a supplied conversion list, but otherwise guesses weights for unlisted foods.
Production translation remains text-only; deterministic formatting applies the editable list
whether translation is enabled or disabled.
