import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FamilyMembersService } from '../../services/family-members.service';
import { FreezerService } from '../../services/freezer.service';
import { LanguageService } from '../../services/language.service';
import { MealSuggestionsService } from '../../services/meal-suggestions.service';
import { PlannedMealsService } from '../../services/planned-meals.service';
import { SettingsService } from '../../services/settings.service';
import { Course, MealType, PlannedMeal } from '../../models/planned-meal.model';
import { WeekPlannerPage } from './week-planner.page';

describe('WeekPlannerPage latest reads', () => {
  const requests: Subject<PlannedMeal[]>[] = [];

  beforeEach(async () => {
    requests.length = 0;
    await TestBed.configureTestingModule({
      imports: [WeekPlannerPage],
      providers: [
        provideRouter([]),
        { provide: PlannedMealsService, useValue: {
          getMeals: vi.fn(() => {
            const request = new Subject<PlannedMeal[]>();
            requests.push(request);
            return request;
          })
        } },
        { provide: FamilyMembersService, useValue: { getMembers: () => of([]) } },
        { provide: FreezerService, useValue: { getSuggestions: () => of({ enabled: false, items: [] }) } },
        { provide: MealSuggestionsService, useValue: { getStatus: () => of({ enabled: true }) } },
        { provide: SettingsService, useValue: { startOfWeek: () => new Date(2099, 0, 5) } },
        { provide: LanguageService, useValue: { language: signal('en'), t: (key: string) => key } }
      ]
    }).compileComponents();
  });

  it('does not let an older week response replace the active week', () => {
    const fixture = TestBed.createComponent(WeekPlannerPage);
    fixture.detectChanges();
    fixture.componentInstance.nextWeek();

    const newer = meal('newer', '2099-01-12');
    const older = meal('older', '2099-01-05');
    requests[1].next([newer]);
    requests[0].next([older]);

    expect(fixture.componentInstance.meals()).toEqual([newer]);
  });

  function meal(id: string, date: string): PlannedMeal {
    return {
      id, date, mealType: MealType.Dinner, course: Course.Main,
      dishName: id, freezyItemQuantity: 0, eaten: null,
      attendeeIds: [], ratings: []
    };
  }
});
