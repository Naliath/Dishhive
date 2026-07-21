import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { LanguageService } from '../../services/language.service';
import { IntegrationsService } from '../../services/integrations.service';
import { MealSuggestionsService } from '../../services/meal-suggestions.service';
import { Course, MealType } from '../../models/planned-meal.model';
import {
  SuggestionPlanningDay,
  SuggestionReviewDialog,
  SuggestionReviewDialogData,
  defaultSuggestionDates
} from './suggestion-review-dialog';

function week(startDay = 13): SuggestionPlanningDay[] {
  return Array.from({ length: 7 }, (_, index) => ({
    date: `2026-07-${String(startDay + index).padStart(2, '0')}`,
    hasPlannedDinner: false
  }));
}

describe('defaultSuggestionDates', () => {
  it('starts at today in the current week and skips already planned dinners', () => {
    const days = week();
    days[4].hasPlannedDinner = true;

    expect([...defaultSuggestionDates(days, '2026-07-15')]).toEqual([
      '2026-07-15',
      '2026-07-16',
      '2026-07-18',
      '2026-07-19'
    ]);
  });

  it.each(['2026-07-01', '2026-07-27'])(
    'starts at the first day for a viewed future or past week (today=%s)',
    today => {
      const days = week();
      days[1].hasPlannedDinner = true;

      expect([...defaultSuggestionDates(days, today)]).toEqual([
        '2026-07-13',
        '2026-07-15',
        '2026-07-16',
        '2026-07-17',
        '2026-07-18',
        '2026-07-19'
      ]);
    }
  );
});

describe('SuggestionReviewDialog states', () => {
  const suggestionDates = [
    '2099-01-05', '2099-01-06', '2099-01-07', '2099-01-08',
    '2099-01-09', '2099-01-10', '2099-01-11'
  ];
  const suggestWeek = vi.fn((
    _weekStart: string,
    _selectedDates: string[],
    _instructions?: string
  ) => of({
    batchId: 'batch-1',
    enabled: true,
    suggestions: [{
      id: 'suggestion-1',
      date: suggestionDates[0],
      mealType: MealType.Dinner,
      course: Course.Main,
      attendeeIds: [],
      attendeeNames: [],
      dishName: 'Vegetable curry'
    }]
  }));

  beforeEach(async () => {
    suggestWeek.mockClear();
    const data: SuggestionReviewDialogData = {
      weekStart: suggestionDates[0],
      days: suggestionDates.map(date => ({ date, hasPlannedDinner: false }))
    };
    const translations: Record<string, string> = {
      'weekPlanner.suggestions.chooseDays': 'Choose the days to generate suggestions for.'
    };

    await TestBed.configureTestingModule({
      imports: [SuggestionReviewDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        { provide: MealSuggestionsService, useValue: { suggestWeek } },
        {
          provide: IntegrationsService,
          useValue: {
            getStatus: () => of({
              ai: { reachable: true, modelTestVerdict: 'passed' },
              webSearch: { operational: true }
            })
          }
        },
        {
          provide: LanguageService,
          useValue: {
            language: signal('en'),
            t: (key: string) => translations[key] ?? key,
            plural: (key: string) => key
          }
        }
      ]
    }).compileComponents();
  });

  it('shows a translated picker only before generation and reuses its dates', () => {
    const fixture = TestBed.createComponent(SuggestionReviewDialog);
    const component = fixture.componentInstance;
    fixture.detectChanges();

    const picker = fixture.nativeElement.querySelector('.day-options');
    expect(picker).not.toBeNull();
    expect(fixture.nativeElement.textContent)
      .toContain('Choose the days to generate suggestions for.');

    component.fetch();
    fixture.detectChanges();

    expect(component.phase()).toBe('review');
    expect(fixture.nativeElement.querySelector('.day-options')).toBeNull();

    component.fetch();
    expect(suggestWeek).toHaveBeenCalledTimes(2);
    expect(suggestWeek.mock.calls[1][1]).toEqual(suggestWeek.mock.calls[0][1]);
  });
});
