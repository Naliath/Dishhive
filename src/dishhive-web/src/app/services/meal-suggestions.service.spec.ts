import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { MealSuggestionsService } from './meal-suggestions.service';

describe('MealSuggestionsService', () => {
  let service: MealSuggestionsService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(MealSuggestionsService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('sends the selected dates as an authoritative part of the request', () => {
    service.suggestWeek(
      '2026-07-13',
      ['2026-07-15', '2026-07-17'],
      '  two quick meals  '
    ).subscribe();

    const request = httpTesting.expectOne('/api/plannedmeals/suggestions');
    expect(request.request.body).toEqual({
      weekStart: '2026-07-13',
      attendeeIds: [],
      selectedDates: ['2026-07-15', '2026-07-17'],
      instructions: 'two quick meals'
    });
    request.flush({ batchId: 'batch', enabled: true, suggestions: [] });
  });
});
