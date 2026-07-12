import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import {
  AiPromptSettings,
  FIRST_DAY_OF_WEEK_KEY,
  FirstDayOfWeek,
  MeasurementSystem,
  MEASUREMENT_SYSTEM_KEY,
  UserSetting,
  SupportedLanguage,
  PREFERRED_LANGUAGE_KEY,
  TRANSLATE_IMPORTED_RECIPES_KEY
} from '../models/user-setting.model';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private apiUrl = '/api/settings';

  /** Current measurement system; metric is the default by absence */
  readonly measurementSystem = signal<MeasurementSystem>('metric');

  /** Current first day of the week; Monday is the default by absence */
  readonly firstDayOfWeek = signal<FirstDayOfWeek>('monday');
  readonly preferredLanguage = signal<SupportedLanguage>(this.browserLanguage());
  readonly translateImportedRecipes = signal(false);

  constructor(private http: HttpClient) {}

  /** Loads the measurement preference from the backend into the signal */
  loadMeasurementSystem(): Observable<MeasurementSystem> {
    return this.http.get<UserSetting>(`${this.apiUrl}/${MEASUREMENT_SYSTEM_KEY}`).pipe(
      map((setting): MeasurementSystem => setting.value === 'imperial' ? 'imperial' : 'metric'),
      // 404 means the setting was never changed: metric default
      catchError(() => of<MeasurementSystem>('metric')),
      tap(system => this.measurementSystem.set(system))
    );
  }

  setMeasurementSystem(system: MeasurementSystem): Observable<UserSetting> {
    return this.http.put<UserSetting>(`${this.apiUrl}/${MEASUREMENT_SYSTEM_KEY}`, { value: system }).pipe(
      tap(() => this.measurementSystem.set(system))
    );
  }

  /** Loads the first-day-of-week preference from the backend into the signal */
  loadFirstDayOfWeek(): Observable<FirstDayOfWeek> {
    return this.http.get<UserSetting>(`${this.apiUrl}/${FIRST_DAY_OF_WEEK_KEY}`).pipe(
      map((setting): FirstDayOfWeek => setting.value === 'sunday' ? 'sunday' : 'monday'),
      // 404 means the setting was never changed: Monday default
      catchError(() => of<FirstDayOfWeek>('monday')),
      tap(day => this.firstDayOfWeek.set(day))
    );
  }

  setFirstDayOfWeek(day: FirstDayOfWeek): Observable<UserSetting> {
    return this.http.put<UserSetting>(`${this.apiUrl}/${FIRST_DAY_OF_WEEK_KEY}`, { value: day }).pipe(
      tap(() => this.firstDayOfWeek.set(day))
    );
  }

  loadPreferredLanguage(): Observable<SupportedLanguage> {
    return this.http.get<UserSetting>(`${this.apiUrl}/${PREFERRED_LANGUAGE_KEY}`).pipe(
      map((setting): SupportedLanguage => setting.value === 'nl' ? 'nl' : 'en'),
      catchError(() => of<SupportedLanguage>(this.browserLanguage())),
      tap(language => this.preferredLanguage.set(language))
    );
  }

  setPreferredLanguage(language: SupportedLanguage): Observable<UserSetting> {
    return this.http.put<UserSetting>(`${this.apiUrl}/${PREFERRED_LANGUAGE_KEY}`, { value: language }).pipe(
      tap(() => this.preferredLanguage.set(language))
    );
  }

  loadTranslateImportedRecipes(): Observable<boolean> {
    return this.http.get<UserSetting>(`${this.apiUrl}/${TRANSLATE_IMPORTED_RECIPES_KEY}`).pipe(
      map(setting => setting.value === 'true'),
      catchError(() => of(false)),
      tap(enabled => this.translateImportedRecipes.set(enabled))
    );
  }

  setTranslateImportedRecipes(enabled: boolean): Observable<UserSetting> {
    return this.http.put<UserSetting>(`${this.apiUrl}/${TRANSLATE_IMPORTED_RECIPES_KEY}`, { value: String(enabled) }).pipe(
      tap(() => this.translateImportedRecipes.set(enabled))
    );
  }

  private browserLanguage(): SupportedLanguage {
    return typeof navigator !== 'undefined' && navigator.language.toLowerCase().startsWith('nl') ? 'nl' : 'en';
  }

  /** Local midnight of the start of the week containing `date`, per the configured first day */
  startOfWeek(date: Date): Date {
    const start = new Date(date);
    start.setHours(0, 0, 0, 0);
    const firstDay = this.firstDayOfWeek() === 'sunday' ? 0 : 1;
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
