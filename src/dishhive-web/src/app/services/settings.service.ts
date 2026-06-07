import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AppSettings, MeasurementSystem } from '../models';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/settings';

  get(): Observable<AppSettings> {
    return this.http.get<AppSettings>(this.base);
  }

  setMeasurementSystem(system: MeasurementSystem): Observable<AppSettings> {
    return this.http.put<AppSettings>(`${this.base}/measurement-system`, { measurementSystem: system });
  }

  setFreezyEnabled(enabled: boolean): Observable<AppSettings> {
    return this.http.put<AppSettings>(`${this.base}/freezy-enabled`, { enabled });
  }
}
