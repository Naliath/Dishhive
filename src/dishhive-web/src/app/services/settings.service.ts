import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import { MeasurementSystem, MEASUREMENT_SYSTEM_KEY, UserSetting } from '../models/user-setting.model';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private apiUrl = '/api/settings';

  /** Current measurement system; metric is the default by absence */
  readonly measurementSystem = signal<MeasurementSystem>('metric');

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
}
