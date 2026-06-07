# Feature: Measurement Preferences

## Goal
Let the user pick their preferred measurement system (metric or imperial). Default to metric. Preserve original source values for imported recipes so manual correction stays possible.

## Scope
- Settings entry for the default measurement system.
- A unit-conversion service used when displaying recipes whose source unit doesn't match the user's preference.
- Always preserve `OriginalQuantity` and `OriginalUnit` on `RecipeIngredient`.

## Out of scope (v1)
- Per-recipe override of the system.
- Locale-specific cup sizes (US vs UK). v1 uses US imperial conventions.

## Domain model
- `UserSetting` table (`Key` PK, `Value`) — same shape as Freezy's. Setting key: `defaults.measurement_system` with values `metric` or `imperial`.
- `RecipeIngredient.OriginalQuantity`, `RecipeIngredient.OriginalUnit` (already part of the recipe-store feature).

## Architecture
```csharp
public interface IUnitConversionService
{
    bool TryConvert(decimal qty, string fromUnit, string toUnit, out decimal converted);
    (decimal qty, string unit) ConvertForSystem(decimal qty, string unit, MeasurementSystem target);
}

public enum MeasurementSystem { Metric, Imperial }
```

Conversion table covers common cooking units: g↔oz, kg↔lb, ml↔fl oz, l↔cup, °C↔°F. Unknown units are returned unchanged. Display layer shows both values when conversion happened (`120 g (≈ 4.2 oz)`).

## Endpoints
- `GET /api/settings/measurement-system`
- `PUT /api/settings/measurement-system`

## Frontend requirements
- `pages/settings/` — radio group: Metric / Imperial.
- Recipe display reads the current setting and asks the conversion service for converted display values; original is shown alongside if different.

## Risks
- Cooking conversions are imprecise (ounces are not always volume vs weight). v1 explicitly does not auto-convert ambiguous units (`cup` → `g` requires ingredient density). When ambiguous, the original value is shown unchanged with a small "?" tooltip.

## Phased plan
1. `MeasurementSystem` enum + setting key constants.
2. `IUnitConversionService` with safe conversions only.
3. Settings endpoint + UI.
4. Wire conversion into recipe display.

## Implementation checklist
- [x] `MeasurementSystem` enum
- [x] `IUnitConversionService` + impl with safe conversion table
- [x] Settings endpoint
- [x] Settings UI
- [ ] Recipe display wired to convert + show original
