import { signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { MeasurementSystem } from '../models/user-setting.model';
import { LanguageService } from './language.service';
import { MeasurementService } from './measurement.service';
import { SettingsService } from './settings.service';

describe('MeasurementService', () => {
  function createService(system = MeasurementSystem.Metric) {
    const language = new LanguageService({
      get: (url: string) => of({ measurementUnit: url.includes('/nl.json') ? {
        tablespoon: { other: 'el' }, teaspoon: { other: 'tl' },
        leaf: { one: 'blaadje', other: 'blaadjes' }
      } : {
        tablespoon: { other: 'tbsp' }, teaspoon: { other: 'tsp' },
        leaf: { one: 'leaf', other: 'leaves' }
      } })
    } as unknown as HttpClient);
    const settings = { measurementSystem: signal(system) } as unknown as SettingsService;
    return { service: new MeasurementService(settings, language), language };
  }

  it.each([MeasurementSystem.Metric, MeasurementSystem.Imperial])(
    'follows language changes for culinary units in measurement system %s', system => {
      const { service, language } = createService(system);

      expect(service.format(2, 'el')).toBe('2 tbsp');
      expect(service.format(0.5, 'tl')).toBe('0.5 tsp');
      expect(service.format(1, 'blaadje')).toBe('1 leaf');
      expect(service.format(3, 'blaadje')).toBe('3 leaves');

      language.use('nl');
      expect(service.format(2, 'el')).toBe('2 el');
      expect(service.format(0.5, 'tl')).toBe('0.5 tl');
      expect(service.format(1, 'blaadje')).toBe('1 blaadje');
      expect(service.format(3, 'blaadje')).toBe('3 blaadjes');

      language.use('en');
      expect(service.format(2, 'el')).toBe('2 tbsp');
    }
  );

  it('preserves unknown units, omitted quantities, zero, and countable pieces', () => {
    const { service } = createService();
    expect(service.format(2, 'heaped scoop')).toBe('2 heaped scoop');
    expect(service.format(null, 'el')).toBe('');
    expect(service.format(undefined, 'tl')).toBe('');
    expect(service.format(0, 'tl')).toBe('0 tsp');
    expect(service.format(2, 'piece')).toBe('2');
    expect(service.format(2, null)).toBe('2');
  });

  it('continues to convert metric quantities for imperial display', () => {
    const { service } = createService(MeasurementSystem.Imperial);
    expect(service.format(28.35, 'g')).toBe('1 oz');
    expect(service.format(1, 'kg')).toBe('2.2 lb');
    expect(service.format(29.57, 'ml')).toBe('1 fl oz');
    expect(service.format(1, 'l')).toBe('1.06 qt');
  });
});
