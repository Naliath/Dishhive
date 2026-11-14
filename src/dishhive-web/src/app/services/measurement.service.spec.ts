import { signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { MeasurementSystem } from '../models/user-setting.model';
import { LanguageService } from './language.service';
import { MeasurementService } from './measurement.service';
import { SettingsService } from './settings.service';
import { CupConversionDto } from '../api/generated/dishhive-api.client';

describe('MeasurementService', () => {
  function createService(system = MeasurementSystem.Metric) {
    const language = new LanguageService({
      get: (url: string) => of({ measurementUnit: url.includes('/nl.json') ? {
        cup: { one: 'cup', other: 'cups' }, tablespoon: { other: 'el' }, teaspoon: { other: 'tl' },
        leaf: { one: 'blaadje', other: 'blaadjes' }
      } : {
        cup: { one: 'cup', other: 'cups' }, tablespoon: { other: 'tbsp' }, teaspoon: { other: 'tsp' },
        leaf: { one: 'leaf', other: 'leaves' }
      } })
    } as unknown as HttpClient);
    const cupConversions = signal<CupConversionDto[]>([
      { ingredient: 'Flour', aliases: ['bloem'], quantity: 120, unit: 'g' },
      { ingredient: 'Water', aliases: [], quantity: 236.5882365, unit: 'ml' }
    ]);
    const settings = { measurementSystem: signal(system), cupConversions } as unknown as SettingsService;
    return { service: new MeasurementService(settings, language), language, cupConversions };
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

  it('uses only configured exact ingredient names and follows edits immediately', () => {
    const { service, cupConversions } = createService();
    expect(service.format(0.5, 'cup', ' FLOUR ')).toBe('60 g');
    expect(service.format(0.25, 'cups', 'bloem')).toBe('30 g');
    expect(service.format(1, 'cup', 'water')).toBe('237 ml');
    expect(service.format(1, 'cup', 'frozen corn')).toBe('1 cup');
    expect(service.format(2, 'cup', 'almond flour')).toBe('2 cups');
    cupConversions.set([{ ingredient: 'Flour', aliases: [], quantity: 125, unit: 'g' }]);
    expect(service.format(2, 'cup', 'Flour')).toBe('250 g');
    cupConversions.set([]);
    expect(service.format(2, 'cup', 'Flour')).toBe('2 cups');
  });

  it('converts configured metric ingredients back to cups with imperial selected', () => {
    const { service } = createService(MeasurementSystem.Imperial);
    expect(service.format(60, 'g', 'flour')).toBe('0.5 cups');
    expect(service.format(0.12, 'kg', 'bloem')).toBe('1 cup');
    expect(service.format(0.2365882365, 'l', 'water')).toBe('1 cup');
    expect(service.format(1, 'cup', 'frozen corn')).toBe('1 cup');
    expect(service.format(1, 'g', 'flour')).toBe('0.0083 cups');
  });
});
