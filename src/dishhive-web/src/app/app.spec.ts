import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { describe, it, expect, beforeEach } from 'vitest';
import { App } from './app';
import { OnboardingService } from './services/onboarding.service';
import { PwaService } from './services/pwa.service';
import { SettingsService } from './services/settings.service';
import { ThemeService } from './services/theme.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        {
          provide: SettingsService,
          useValue: {
            loadMeasurementSystem: () => of('metric'),
            loadFirstDayOfWeek: () => of('monday')
          }
        },
        { provide: OnboardingService, useValue: { start: () => of({ shouldShow: false }) } },
        { provide: PwaService, useValue: {} },
        { provide: ThemeService, useValue: {} }
      ]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it(`should have the 'Dishhive' title`, () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance.title).toBe('Dishhive');
  });

  it('should open the normal shell when onboarding is not required', () => {
    const fixture = TestBed.createComponent(App);

    fixture.detectChanges();

    expect(fixture.componentInstance.checkingOnboarding()).toBe(false);
    expect(fixture.componentInstance.showOnboarding()).toBe(false);
  });
});
