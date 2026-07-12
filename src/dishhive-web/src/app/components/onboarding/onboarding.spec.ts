import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FamilyMembersService } from '../../services/family-members.service';
import { OnboardingService } from '../../services/onboarding.service';
import { SettingsService } from '../../services/settings.service';
import { LanguageService } from '../../services/language.service';
import { OnboardingComponent } from './onboarding';
import { FirstDayOfWeek, MeasurementSystem } from '../../api/generated/dishhive-api.client';
import { signal } from '@angular/core';

describe('OnboardingComponent', () => {
  const complete = vi.fn(() => of({ shouldShow: false }));
  const setMeasurementSystem = vi.fn(() => of({}));
  const setFirstDayOfWeek = vi.fn(() => of({}));
  const setPreferredLanguage = vi.fn(() => of({}));
  const setTranslateImportedRecipes = vi.fn(() => of({}));
  const createdMember = {
    id: 'member-1',
    name: 'Ada',
    isGuest: false,
    allergyTags: [],
    dietTags: [],
    isActive: true,
    createdAt: '2026-07-10T00:00:00Z',
    updatedAt: '2026-07-10T00:00:00Z'
  };
  const createMember = vi.fn(() => of(createdMember));

  beforeEach(async () => {
    complete.mockClear();
    setMeasurementSystem.mockClear();
    setFirstDayOfWeek.mockClear();
    setPreferredLanguage.mockClear();
    setTranslateImportedRecipes.mockClear();
    createMember.mockClear();

    await TestBed.configureTestingModule({
      imports: [OnboardingComponent],
      providers: [
        {
          provide: SettingsService,
          useValue: {
            loadPreferences: () => of({
              measurementSystem: MeasurementSystem.Metric,
              firstDayOfWeek: FirstDayOfWeek.Monday,
              preferredLanguage: 'en',
              translateImportedRecipes: false
            }),
            setMeasurementSystem,
            setFirstDayOfWeek,
            setPreferredLanguage,
            setTranslateImportedRecipes,
            supportedLanguages: signal([{ code: 'en', displayName: 'English' }])
          }
        },
        {
          provide: FamilyMembersService,
          useValue: { getMembers: () => of([]), createMember }
        },
        { provide: OnboardingService, useValue: { complete } },
        { provide: LanguageService, useValue: { use: vi.fn(), t: (key: string) => key } },
        { provide: MatSnackBar, useValue: { open: vi.fn() } }
      ]
    }).compileComponents();
  });

  it('starts with Monday and metric defaults', () => {
    const component = TestBed.createComponent(OnboardingComponent).componentInstance;

    expect(component.preferencesForm.getRawValue()).toEqual({
      firstDayOfWeek: FirstDayOfWeek.Monday,
      measurementSystem: MeasurementSystem.Metric,
      preferredLanguage: 'en',
      translateImportedRecipes: false
    });
  });

  it('persists the skipped outcome and closes the wizard', () => {
    const component = TestBed.createComponent(OnboardingComponent).componentInstance;
    const finished = vi.fn();
    component.finished.subscribe(finished);

    component.skip();

    expect(complete).toHaveBeenCalledWith(true);
    expect(finished).toHaveBeenCalledOnce();
  });

  it('saves both preferences before advancing', () => {
    const component = TestBed.createComponent(OnboardingComponent).componentInstance;
    const stepper = { next: vi.fn() };
    component.preferencesForm.setValue({
      firstDayOfWeek: FirstDayOfWeek.Sunday,
      measurementSystem: MeasurementSystem.Imperial,
      preferredLanguage: 'nl',
      translateImportedRecipes: true
    });

    component.savePreferences(stepper as never);

    expect(setMeasurementSystem).toHaveBeenCalledWith(MeasurementSystem.Imperial);
    expect(setFirstDayOfWeek).toHaveBeenCalledWith(FirstDayOfWeek.Sunday);
    expect(setPreferredLanguage).toHaveBeenCalledWith('nl');
    expect(setTranslateImportedRecipes).toHaveBeenCalledWith(true);
    expect(stepper.next).toHaveBeenCalledOnce();
  });

  it('shows the name validation state only after an add attempt', () => {
    const component = TestBed.createComponent(OnboardingComponent).componentInstance;
    const formDirective = { resetForm: vi.fn() };
    component.memberForm.controls.name.markAsTouched();

    expect(component.memberErrorStateMatcher.isErrorState(
      component.memberForm.controls.name,
      null
    )).toBe(false);

    component.addMember(formDirective as never);

    expect(component.memberSubmissionAttempted()).toBe(true);
    expect(component.memberErrorStateMatcher.isErrorState(
      component.memberForm.controls.name,
      null
    )).toBe(true);
    expect(createMember).not.toHaveBeenCalled();
  });

  it('resets submission validation after successfully adding a member', () => {
    const component = TestBed.createComponent(OnboardingComponent).componentInstance;
    const formDirective = { resetForm: vi.fn() };
    component.memberSubmissionAttempted.set(true);
    component.memberForm.setValue({ name: ' Ada ', isGuest: false });

    component.addMember(formDirective as never);

    expect(createMember).toHaveBeenCalledWith({
      name: 'Ada',
      isGuest: false,
      allergyTags: [],
      dietTags: []
    });
    expect(formDirective.resetForm).toHaveBeenCalledWith({ name: '', isGuest: false });
    expect(component.memberSubmissionAttempted()).toBe(false);
  });
});
