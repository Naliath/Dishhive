# Feature: Measurement Preferences

> **Feature ID**: MRP-001
> **Status**: Planned
> **Priority**: Medium
> **Depends on**: Infrastructure, Recipe Store
> **Last Updated**: 2026-06-07

## 1. Feature Goal

Provide users with configurable measurement system preferences (metric/imperial) that apply consistently across recipe display, shopping lists, and planning views. Default to metric.

## 2. Scope

### In Scope (v1)
- Settings page for measurement system preference
- Metric and imperial options
- Metric as default
- Display conversion in recipe views
- Display conversion in shopping list views
- Store normalized values used by Dishhive
- Preserve original source values/units for manual correction

### Out of Scope (v1)
- Custom unit definitions
- Per-recipe unit overrides (beyond what settings provide)
- Automatic unit conversion during import (handled by normalization)
- Volume-to-weight conversions for specific ingredients

## 3. User Stories / Use Cases

| ID | Story | Priority |
|----|-------|----------|
| MRP-US-001 | As a user, I want to set my preferred measurement system | Must |
| MRP-US-002 | As a user, I want recipes to display in my preferred units | Must |
| MRP-US-003 | As a user, I want shopping lists in my preferred units | Must |
| MRP-US-004 | As a user, I want to see original values if conversion seems wrong | Should |
| MRP-US-005 | As a user, I want metric to be the default | Must |

## 4. Domain Model

```
UserPreference / HouseholdSettings
├── MeasurementSystem: enum (Metric, Imperial)
├── DefaultServings: int
├── Language: string
└── ...

Ingredient (stored model)
├── Name: string
├── NormalizedAmount: decimal
├── NormalizedUnit: string (metric canonical)
├── OriginalAmount: decimal?
├── OriginalUnit: string?
├── OriginalText: string? (full original string for traceability)
└── Notes: string?

UnitConversionService
├── Convert(decimal amount, string fromUnit, string toUnit)
├── GetDisplayUnit(string storedUnit, MeasurementSystem preference)
├── GetConversionFactor(string fromUnit, string toUnit)
└── IsConversionSupported(string fromUnit, string toUnit)
```

## 5. Conversion Strategy

### Storage Model

1. **Normalized values**: All ingredients stored in metric canonical units
2. **Original values**: Preserved alongside normalized for traceability and manual correction
3. **Display values**: Converted at render time based on user preference

### Why This Model

| Aspect | Rationale |
|--------|-----------|
| Store normalized | Consistent internal representation; easier aggregation for shopping lists |
| Preserve original | Manual correction possible; audit trail; user trust |
| Convert at display | Single source of truth; settings change propagates everywhere |

### Unit Mapping

| Category | Metric Canonical | Imperial Display |
|----------|-----------------|------------------|
| Weight | grams (g) | pounds (lb) / ounces (oz) |
| Volume (large) | liters (L) | cups / pints |
| Volume (small) | milliliters (ml) | tablespoons / teaspoons |
| Count | pieces | pieces |
| Temperature | Celsius (°C) | Fahrenheit (°F) |

## 6. Backend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| MRP-BE-001 | User preference storage for measurement system | Must |
| MRP-BE-002 | Unit conversion service | Must |
| MRP-BE-003 | Ingredient model with normalized + original fields | Must |
| MRP-BE-004 | API returns values in user's preferred units | Must |
| MRP-BE-005 | Default to metric when no preference set | Must |
| MRP-BE-006 | Unit mapping table/configuration | Must |

## 7. Frontend Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| MRP-FE-001 | Settings page with measurement system toggle | Must |
| MRP-FE-002 | Recipe ingredient display respects preference | Must |
| MRP-FE-003 | Shopping list display respects preference | Must |
| MRP-FE-004 | Show original value on hover/click if different | Should |
| MRP-FE-005 | Edit ingredient with corrected values | Should |

## 8. Risks / Unknowns

| ID | Risk | Mitigation |
|----|------|------------|
| MRP-R001 | Incomplete unit mapping | Start with common units; allow manual entry for unknowns |
| MRP-R002 | Imprecise conversions (e.g., cups vary by region) | Document assumptions; allow manual override |
| MRP-R003 | Non-standard units in source recipes | Store original text; flag for user review |

## 9. Implementation Checklist

### Backend
- [ ] Unit conversion service
- [ ] Unit mapping configuration
- [ ] Ingredient model with dual storage
- [ ] User preference storage
- [ ] API response formatting

### Frontend
- [ ] Settings page component
- [ ] Measurement system toggle
- [ ] Recipe ingredient display
- [ ] Shopping list display
- [ ] Original value tooltip

### Testing
- [ ] Unit test: conversion calculations
- [ ] Unit test: default to metric
- [ ] Integration test: preference persistence
- [ ] Integration test: display respects preference
