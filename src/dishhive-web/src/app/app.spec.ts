import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { describe, it, expect, beforeEach } from 'vitest';
import { App } from './app';
import { OnboardingService } from './services/onboarding.service';
import { PwaService } from './services/pwa.service';
import { SettingsService } from './services/settings.service';
import { ThemeService } from './services/theme.service';
import { LanguageService } from './services/language.service';

@Component({ template: '' })
class TestPage {}

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([
          { path: '', component: TestPage },
          { path: 'recipes/:id', component: TestPage },
          { path: 'settings/ai-stats', component: TestPage }
        ]),
        {
          provide: SettingsService,
          useValue: {
            loadPreferences: () => of({ preferredLanguage: 'en' })
          }
        },
        { provide: OnboardingService, useValue: { start: () => of({ shouldShow: false }) } },
        { provide: PwaService, useValue: {} },
        { provide: ThemeService, useValue: {} },
        { provide: LanguageService, useValue: { use: () => {}, t: (key: string) => key } }
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

  it('should expose the active parent navigation item for a nested route', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);

    fixture.detectChanges();
    await router.navigateByUrl('/recipes/42');
    await fixture.whenStable();
    fixture.detectChanges();

    const recipeLink = fixture.nativeElement.querySelector('a[href="/recipes"]');
    const weekPlannerLink = fixture.nativeElement.querySelector('a[href="/"]');

    expect(recipeLink.getAttribute('aria-current')).toBe('page');
    expect(weekPlannerLink.hasAttribute('aria-current')).toBe(false);
  });
});
