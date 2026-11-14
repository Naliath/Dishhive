import { SettingsClient } from '../api/generated/dishhive-api.client';
import { apiResponse } from '../api/api-response';
import { HouseholdClockService } from './household-clock.service';
import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import {
  FirstDayOfWeek,
  MeasurementSystem,
  UpdateUserPreferencesDto,
  UserPreferencesDto,
  SupportedLanguageDto
} from '../api/generated/dishhive-api.client';
import { CupConversionDto } from '../api/generated/dishhive-api.client';
import {
  AiPromptSettings,
  SupportedLanguage
} from '../models/user-setting.model';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly api: SettingsClient;
  private apiUrl = '/api/settings';

  /** Current measurement system; metric is the default by absence */
  readonly measurementSystem = signal(MeasurementSystem.Metric);

  /** Current first day of the week; Monday is the default by absence */
  readonly firstDayOfWeek = signal(FirstDayOfWeek.Monday);
  readonly preferredLanguage = signal<SupportedLanguage>(this.browserLanguage());
  readonly translateImportedRecipes = signal(false);
  readonly cupConversions = signal<ReadonlyArray<CupConversionDto>>([]);
  readonly supportedLanguages = signal<ReadonlyArray<{ code: string; displayName: string }>>([]);

  constructor(http: HttpClient, private readonly clock: HouseholdClockService) {
    this.api = new SettingsClient(http, "");
    // Language selection remains available even when loading database-backed
    // preferences fails (for example during initial setup or a database outage).
    this.loadSupportedLanguages().subscribe();
  }

  loadSupportedLanguages(): Observable<SupportedLanguageDto[]> {
    return this.api.getSupportedLanguages().pipe(
      tap(languages => this.supportedLanguages.set(languages)),
      catchError(() => of([]))
    );
  }

  loadPreferences(): Observable<UserPreferencesDto | null> {
    return this.api.getPreferences().pipe(
      tap(preferences => this.apply(preferences)),
      catchError(() => of(null))
    );
  }

  /** Loads the measurement preference from the backend into the signal */
  loadMeasurementSystem(): Observable<MeasurementSystem> {
    return this.loadPreferences().pipe(map(() => this.measurementSystem()));
  }

  setMeasurementSystem(system: MeasurementSystem): Observable<UserPreferencesDto> {
    return this.updatePreference({ measurementSystem: system });
  }

  setCupConversions(entries: CupConversionDto[]): Observable<UserPreferencesDto> {
    return apiResponse(this.api.setCupConversions(entries)).pipe(tap(preferences => this.apply(preferences)));
  }

  /** Loads the first-day-of-week preference from the backend into the signal */
  loadFirstDayOfWeek(): Observable<FirstDayOfWeek> {
    return this.loadPreferences().pipe(map(() => this.firstDayOfWeek()));
  }

  setFirstDayOfWeek(day: FirstDayOfWeek): Observable<UserPreferencesDto> {
    return this.updatePreference({ firstDayOfWeek: day });
  }

  loadPreferredLanguage(): Observable<SupportedLanguage> {
    return this.loadPreferences().pipe(map(() => this.preferredLanguage()));
  }

  setPreferredLanguage(language: SupportedLanguage): Observable<UserPreferencesDto> {
    return this.updatePreference({ preferredLanguage: language });
  }

  loadTranslateImportedRecipes(): Observable<boolean> {
    return this.loadPreferences().pipe(map(() => this.translateImportedRecipes()));
  }

  setTranslateImportedRecipes(enabled: boolean): Observable<UserPreferencesDto> {
    return this.updatePreference({ translateImportedRecipes: enabled });
  }

  private apply(preferences: UserPreferencesDto): void {
    this.clock.timeZone.set(preferences.householdTimeZone ?? 'Europe/Brussels');
    this.measurementSystem.set(preferences.measurementSystem);
    this.cupConversions.set(preferences.cupConversions ?? []);
    this.firstDayOfWeek.set(preferences.firstDayOfWeek);
    this.preferredLanguage.set(preferences.preferredLanguage);
    this.translateImportedRecipes.set(preferences.translateImportedRecipes);
    this.supportedLanguages.set(preferences.supportedLanguages);
  }

  private updatePreference(change: Partial<UpdateUserPreferencesDto>): Observable<UserPreferencesDto> {
    return apiResponse(this.api.updatePreference(change)).pipe(tap(preferences => this.apply(preferences)));
  }

  private browserLanguage(): SupportedLanguage {
    return typeof navigator !== 'undefined' && navigator.language.toLowerCase().startsWith('nl') ? 'nl' : 'en';
  }

  /** Local midnight of the start of the week containing `date`, per the configured first day */
  startOfWeek(date: Date): Date {
    const start = new Date(date);
    start.setHours(0, 0, 0, 0);
    const firstDay = this.firstDayOfWeek() === FirstDayOfWeek.Sunday ? 0 : 1;
    start.setDate(start.getDate() - ((start.getDay() - firstDay + 7) % 7));
    return start;
  }

  getAiPrompt(): Observable<AiPromptSettings | null> {
    return this.api.getAiPrompt().pipe(
      catchError(() => of(null))
    );
  }

  /** Saving a changed prompt also restarts the model capability test server-side */
  setAiPrompt(editablePrompt: string): Observable<AiPromptSettings | null> {
    return this.api.setAiPrompt({ editablePrompt }).pipe(
      catchError(() => of(null))
    );
  }

  resetAiPrompt(): Observable<AiPromptSettings | null> {
    return this.api.resetAiPrompt().pipe(
      catchError(() => of(null))
    );
  }
}
