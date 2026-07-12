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
import {
  AiPromptSettings,
  SupportedLanguage
} from '../models/user-setting.model';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private apiUrl = '/api/settings';

  /** Current measurement system; metric is the default by absence */
  readonly measurementSystem = signal(MeasurementSystem.Metric);

  /** Current first day of the week; Monday is the default by absence */
  readonly firstDayOfWeek = signal(FirstDayOfWeek.Monday);
  readonly preferredLanguage = signal<SupportedLanguage>(this.browserLanguage());
  readonly translateImportedRecipes = signal(false);
  readonly supportedLanguages = signal<ReadonlyArray<{ code: string; displayName: string }>>([]);

  constructor(private http: HttpClient) {
    // Language selection remains available even when loading database-backed
    // preferences fails (for example during initial setup or a database outage).
    this.loadSupportedLanguages().subscribe();
  }

  loadSupportedLanguages(): Observable<SupportedLanguageDto[]> {
    return this.http.get<SupportedLanguageDto[]>(`${this.apiUrl}/languages`).pipe(
      tap(languages => this.supportedLanguages.set(languages)),
      catchError(() => of([]))
    );
  }

  loadPreferences(): Observable<UserPreferencesDto | null> {
    return this.http.get<UserPreferencesDto>(`${this.apiUrl}/preferences`).pipe(
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
    this.measurementSystem.set(preferences.measurementSystem);
    this.firstDayOfWeek.set(preferences.firstDayOfWeek);
    this.preferredLanguage.set(preferences.preferredLanguage);
    this.translateImportedRecipes.set(preferences.translateImportedRecipes);
    this.supportedLanguages.set(preferences.supportedLanguages);
  }

  private updatePreference(change: Partial<UpdateUserPreferencesDto>): Observable<UserPreferencesDto> {
    return this.http.patch<UserPreferencesDto>(`${this.apiUrl}/preferences`,
      new UpdateUserPreferencesDto(change)).pipe(tap(preferences => this.apply(preferences)));
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
    return this.http.get<AiPromptSettings>(`${this.apiUrl}/ai-prompt`).pipe(
      catchError(() => of(null))
    );
  }

  /** Saving a changed prompt also restarts the model capability test server-side */
  setAiPrompt(editablePrompt: string): Observable<AiPromptSettings | null> {
    return this.http.put<AiPromptSettings>(`${this.apiUrl}/ai-prompt`, { editablePrompt }).pipe(
      catchError(() => of(null))
    );
  }

  resetAiPrompt(): Observable<AiPromptSettings | null> {
    return this.http.delete<AiPromptSettings>(`${this.apiUrl}/ai-prompt`).pipe(
      catchError(() => of(null))
    );
  }
}
