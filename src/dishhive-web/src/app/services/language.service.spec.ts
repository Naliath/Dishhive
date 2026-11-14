import { HttpClient } from '@angular/common/http';
import { of, Subject } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { LanguageService } from './language.service';

describe('LanguageService automatic collection names', () => {
  const http = {
    get: () => of({ collection: {
      topRated: 'Best beoordeeld',
      quickMax30Min: 'Snel (max. 30 min)',
      recentlyAdded: 'Recent toegevoegd',
      nameFavorites: 'Favorieten van {name}'
    } })
  } as unknown as HttpClient;

  it('localizes built-in names from stable automatic collection ids', () => {
    const service = new LanguageService(http);
    service.use('nl');

    expect(service.autoCollectionName({ id: 'auto-top-rated', name: 'Top rated' }))
      .toBe('Best beoordeeld');
    expect(service.autoCollectionName({ id: 'auto-quick', name: 'Quick (max 30 min)' }))
      .toBe('Snel (max. 30 min)');
    expect(service.autoCollectionName({ id: 'auto-recent', name: 'Recently added' }))
      .toBe('Recent toegevoegd');
    expect(service.autoCollectionName({ id: 'auto-fav-member', name: "Naomi's favorites" }))
      .toBe('Favorieten van Naomi');
  });

  it('does not translate a user-created collection name', () => {
    const service = new LanguageService(http);
    service.use('nl');

    expect(service.autoCollectionName({ id: 'manual-id', name: 'Top rated' }))
      .toBe('Top rated');
  });

  it('localizes canonical display labels without altering their stored identifiers', () => {
    const service = new LanguageService({ get: () => of({
      ingredientClass: { Milk: 'Melk', Crustaceans: 'Schaaldieren' },
      mealType: { Dinner: 'Avondeten' }, course: { Appetizer: 'Voorgerecht' }
    }) } as unknown as HttpClient);
    service.use('nl');
    expect(service.classLabels(['Milk', 'Crustaceans'])).toBe('Melk, Schaaldieren');
    expect(service.classLabel('UnrecognizedSourceLabel')).toBe('UnrecognizedSourceLabel');
    expect(service.mealTypeLabel(2)).toBe('Avondeten');
    expect(service.courseLabel(1)).toBe('Voorgerecht');
  });

  it('interpolates parameters and selects locale-aware plural forms', () => {
    const pluralHttp = {
      get: () => of({ test: {
        greeting: 'Hello {name}',
        items: { one: '{count} item', other: '{count} items' }
      } })
    } as unknown as HttpClient;
    const service = new LanguageService(pluralHttp);
    service.use('en');

    expect(service.t('test.greeting', { name: 'Ada' })).toBe('Hello Ada');
    expect(service.plural('test.items', 1)).toBe('1 item');
    expect(service.plural('test.items', 3)).toBe('3 items');
  });

  it('loads English immediately without waiting for user preferences', () => {
    const service = new LanguageService({
      get: () => of({ common: { save: 'Save' } })
    } as unknown as HttpClient);

    expect(service.t('common.save')).toBe('Save');
  });

  it('does not let the initial English request overwrite a later language choice', () => {
    const english = new Subject<object>();
    const dutch = new Subject<object>();
    const http = {
      get: (url: string) => url.includes('/nl.json') ? dutch : english
    } as unknown as HttpClient;
    const service = new LanguageService(http);

    service.use('nl');
    dutch.next({ common: { save: 'Opslaan' } });
    english.next({ common: { save: 'Save' } });

    expect(service.language()).toBe('nl');
    expect(service.t('common.save')).toBe('Opslaan');
  });

  it('uses versioned asset URLs so the service worker can also serve catalogs offline', () => {
    const get = vi.fn((_url: string) => of({ common: { save: 'Save' } }));
    const service = new LanguageService({ get } as unknown as HttpClient);
    service.use('nl');
    expect(get.mock.calls.map(call => call[0])).toEqual(['/i18n/en.json', '/i18n/nl.json']);
  });
});
