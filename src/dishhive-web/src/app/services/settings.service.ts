import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

export interface UserSetting {
  key: string;
  value: string;
  updatedAt: string;
}

export type WeekStartDay = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday';
export type MeasurementSystem = 'metric' | 'imperial';

const WEEK_START_KEY = 'week_start_day';
const MEASUREMENT_KEY = 'measurement_system';

const DAY_TO_ISO: Record<WeekStartDay, number> = {
  Monday: 1, Tuesday: 2, Wednesday: 3, Thursday: 4, Friday: 5, Saturday: 6, Sunday: 0
};

/** Format a Date as YYYY-MM-DD using local time (avoids UTC shift from toISOString). */
export function toLocalDateString(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly baseUrl = '/api/settings';

  readonly weekStartDay = signal<WeekStartDay>('Monday');
  readonly measurementSystem = signal<MeasurementSystem>('metric');

  constructor(private http: HttpClient) {}

  /** Load all settings from the API and update signals. */
  loadAll(): Observable<Record<string, string>> {
    return this.http.get<Record<string, string>>(this.baseUrl).pipe(
      tap(settings => {
        if (settings[WEEK_START_KEY]) {
          this.weekStartDay.set(settings[WEEK_START_KEY] as WeekStartDay);
        }
        if (settings[MEASUREMENT_KEY]) {
          this.measurementSystem.set(settings[MEASUREMENT_KEY] as MeasurementSystem);
        }
      })
    );
  }

  setWeekStartDay(day: WeekStartDay): Observable<UserSetting> {
    return this.http.put<UserSetting>(`${this.baseUrl}/${WEEK_START_KEY}`, { value: day }).pipe(
      tap(() => this.weekStartDay.set(day))
    );
  }

  setMeasurementSystem(system: MeasurementSystem): Observable<UserSetting> {
    return this.http.put<UserSetting>(`${this.baseUrl}/${MEASUREMENT_KEY}`, { value: system }).pipe(
      tap(() => this.measurementSystem.set(system))
    );
  }

  /** Compute the start of the week containing `date`, based on the configured week start day. */
  getWeekStart(date: Date = new Date()): string {
    const configuredStart = DAY_TO_ISO[this.weekStartDay()];
    const d = new Date(date);
    const currentDay = d.getDay(); // 0=Sun, 1=Mon, ...
    let diff = currentDay - configuredStart;
    if (diff < 0) diff += 7;
    d.setDate(d.getDate() - diff);
    return toLocalDateString(d);
  }
}
