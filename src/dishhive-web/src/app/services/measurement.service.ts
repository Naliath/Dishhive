import { Injectable } from '@angular/core';
import { SettingsService } from './settings.service';
import { LanguageService } from './language.service';
import { MeasurementSystem } from '../models/user-setting.model';

/**
 * Formats stored (metric) quantities in the household's preferred measurement system.
 * Storage is always metric; this service is the single display-conversion point
 * (see docs/features/measurement-preferences.md). Culinary units keep their quantities
 * in both systems; their display labels follow the selected interface language.
 */
@Injectable({ providedIn: 'root' })
export class MeasurementService {
  /** factor converts metric value → imperial value */
  private static readonly imperialUnits: Record<string, { unit: string; factor: number }> = {
    g: { unit: 'oz', factor: 1 / 28.35 },
    kg: { unit: 'lb', factor: 2.2046 },
    ml: { unit: 'fl oz', factor: 1 / 29.57 },
    l: { unit: 'qt', factor: 1.0567 }
  };

  private static readonly unitLabelKeys: Record<string, string> = {
    el: 'measurementUnit.tablespoon',
    tl: 'measurementUnit.teaspoon',
    snuifje: 'measurementUnit.pinch',
    blaadje: 'measurementUnit.leaf',
    teentje: 'measurementUnit.clove',
    takje: 'measurementUnit.sprig',
    bakje: 'measurementUnit.container'
  };

  constructor(
    private readonly settingsService: SettingsService,
    private readonly language: LanguageService
  ) {}

  /**
   * Formats a stored metric quantity for display, e.g. (500, 'g') → "500 g" or "17.6 oz".
   * Returns an empty string when there is no quantity; bare number for countable pieces.
   */
  format(quantity?: number | null, unit?: string | null): string {
    if (quantity == null) {
      return '';
    }

    let value = quantity;
    let displayUnit = unit ?? '';

    if (this.settingsService.measurementSystem() === MeasurementSystem.Imperial && unit) {
      const imperial = MeasurementService.imperialUnits[unit];
      if (imperial) {
        value = quantity * imperial.factor;
        displayUnit = imperial.unit;
      }
    }

    const rounded = this.round(value);
    if (!displayUnit || displayUnit === 'piece') {
      return `${rounded}`;
    }
    const key = MeasurementService.unitLabelKeys[displayUnit];
    const label = key ? this.language.plural(key, rounded) : displayUnit;
    return `${rounded} ${label === key ? displayUnit : label}`;
  }

  private round(value: number): number {
    // 2 decimals for small values, 1 for medium, whole numbers for large
    if (value >= 100) {
      return Math.round(value);
    }
    if (value >= 10) {
      return Math.round(value * 10) / 10;
    }
    return Math.round(value * 100) / 100;
  }
}
