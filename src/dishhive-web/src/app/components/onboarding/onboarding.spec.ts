import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FamilyMembersService } from '../../services/family-members.service';
import { OnboardingService } from '../../services/onboarding.service';
import { SettingsService } from '../../services/settings.service';
import { OnboardingComponent } from './onboarding';

describe('OnboardingComponent', () => {
  const complete = vi.fn(() => of({ shouldShow: false }));
  const setMeasurementSystem = vi.fn(() => of({}));
  const setFirstDayOfWeek = vi.fn(() => of({}));
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
    createMember.mockClear();

    await TestBed.configureTestingModule({
      imports: [OnboardingComponent],
      providers: [
        {
          provide: SettingsService,
          useValue: {
            loadMeasurementSystem: () => of('metric'),
            loadFirstDayOfWeek: () => of('monday'),
            setMeasurementSystem,
            setFirstDayOfWeek
          }
        },
        {
          provide: FamilyMembersService,
          useValue: { getMembers: () => of([]), createMember }
        },
        { provide: OnboardingService, useValue: { complete } },
        { provide: MatSnackBar, useValue: { open: vi.fn() } }
      ]
    }).compileComponents();
  });

  it('starts with Monday and metric defaults', () => {
    const component = TestBed.createComponent(OnboardingComponent).componentInstance;

    expect(component.preferencesForm.getRawValue()).toEqual({
      firstDayOfWeek: 'monday',
      measurementSystem: 'metric'
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
      firstDayOfWeek: 'sunday',
      measurementSystem: 'imperial'
    });

    component.savePreferences(stepper as never);

    expect(setMeasurementSystem).toHaveBeenCalledWith('imperial');
    expect(setFirstDayOfWeek).toHaveBeenCalledWith('sunday');
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
