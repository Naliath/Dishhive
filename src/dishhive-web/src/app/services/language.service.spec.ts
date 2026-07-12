import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { LanguageService } from './language.service';

describe('LanguageService automatic collection names', () => {
  const http = {
    get: () => of({
      'Top rated': 'Best beoordeeld',
      'Quick (max 30 min)': 'Snel (max. 30 min)',
      'Recently added': 'Recent toegevoegd',
      '{name} favorites': 'Favorieten van {name}'
    })
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
});
