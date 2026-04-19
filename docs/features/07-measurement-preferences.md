# Feature: Measurement Preferences

**Created:** April 19, 2026  
**Status:** ✅ Implemented

---

## Feature Goal

Allow users to choose their preferred measurement system (metric or imperial). Dishhive normalizes all stored quantities to metric but preserves original source values for traceability and manual correction.

---

## Scope

### In Scope
- Settings page toggle: metric / imperial
- Default: metric
- Display quantities in preferred system when viewing recipes
- Store normalized (metric) + original (source) values per ingredient
- Conversion utilities for display

### Out of Scope (for this phase)
- Automatic unit parsing from free-text ingredient strings (that's Phase 2 of recipe import)
- Custom unit definitions
- Per-ingredient override of measurement system

---

## User Stories

1. **As a user in a metric country**, I want recipes to display quantities in metric by default.
2. **As a user in an imperial country**, I want to switch to imperial and see quantities converted.
3. **As a meal planner**, I want to correct an ingredient's unit if the import was wrong, without losing the original.

---

## Domain Model Considerations

Measurement preference is a user setting stored in `UserSetting`:

```
UserSetting
  - Id: Guid
  - Key: string (e.g., "MeasurementSystem")
  - Value: string (e.g., "metric" | "imperial")
  - UpdatedAt: DateTime
```

Or as a dedicated typed setting:

```csharp
public enum MeasurementSystem { Metric, Imperial }
```

The `RecipeIngredient` entity stores both:
- `Quantity` + `Unit` = normalized metric value
- `OriginalQuantity` + `OriginalUnit` = source value (preserved from import)

---

## Backend Requirements

- `SettingsController` with endpoints:
  - `GET /api/settings` — get all settings
  - `PUT /api/settings/measurement-system` — update measurement preference
- Conversion service: `IMeasurementConversionService`
  - `ConvertToImperial(decimal quantity, string unit) → (decimal, string)`
  - `ConvertToMetric(decimal quantity, string unit) → (decimal, string)`

---

## Frontend Requirements

- Settings page at `/settings`
- Toggle/select for metric vs imperial
- Recipe ingredient display respects preference
- Stored in `localStorage` for immediate effect without API round-trip

---

## Common Conversions

| From (Metric) | To (Imperial) |
|--------------|---------------|
| grams (g) | ounces (oz) — 1g = 0.035274oz |
| kilograms (kg) | pounds (lb) — 1kg = 2.20462lb |
| milliliters (ml) | fluid ounces (fl oz) — 1ml = 0.033814fl oz |
| liters (l) | cups — 1l = 4.22675 cups |
| centimeters (cm) | inches (in) — 1cm = 0.393701in |

---

## Risks / Unknowns

- Ingredient strings from Dagelijkse Kost are already metric — conversion is most relevant for users who prefer imperial.
- Not all units have clean metric/imperial equivalents (e.g., "pinch", "handful") — leave these as-is.

---

## Phased Implementation Plan

### Phase 1 — Settings Storage
- [ ] `UserSetting` entity (or `AppSetting`)
- [ ] Settings controller
- [ ] Default to metric

### Phase 2 — Conversion Display
- [ ] Conversion service implementation
- [ ] Apply conversion in recipe ingredient display

---

## Implementation Checklist

### Backend
- [x] Feature plan created
- [x] `UserSetting` entity created
- [x] `SettingsController` created (`GET/PUT /api/settings/{key}`)
- [x] `IMeasurementConversionService` interface + `MeasurementConversionService` created
- [x] Registered as singleton in `Program.cs`
- [x] Recipe detail endpoint accepts `?units=imperial` and converts quantities

### Frontend
- [x] Settings page created
- [x] Measurement system toggle (metric/imperial)
- [x] `SettingsService` persists setting to backend
- [x] `MeasurementConversionService` unit tests (19 tests)
