import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SettingsService } from './settings.service';

describe('SettingsService languages', () => {
  let service: SettingsService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    httpTesting = TestBed.inject(HttpTestingController);
    service = TestBed.inject(SettingsService);
  });

  afterEach(() => httpTesting.verify());

  it('loads language choices independently from database-backed preferences', () => {
    httpTesting.expectOne('/api/settings/languages').flush([
      { code: 'en', displayName: 'English' },
      { code: 'nl', displayName: 'Nederlands' }
    ]);

    expect(service.supportedLanguages()).toEqual([
      { code: 'en', displayName: 'English' },
      { code: 'nl', displayName: 'Nederlands' }
    ]);
  });
});
