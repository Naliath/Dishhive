import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  AiPlanningMetricsResponse,
  AiModelTestStatus,
  IntegrationStatusResponse,
  ScraperUpdateResponse,
  ScraperVersionCheck
} from '../models/integration-status.model';

@Injectable({ providedIn: 'root' })
export class IntegrationsService {
  private readonly http = inject(HttpClient);

  getStatus(): Observable<IntegrationStatusResponse | null> {
    return this.http.get<IntegrationStatusResponse>('/api/integrations/status').pipe(
      catchError(() => of(null))
    );
  }

  getAiModelTest(): Observable<AiModelTestStatus | null> {
    return this.http.get<AiModelTestStatus>('/api/integrations/ai/test').pipe(
      catchError(() => of(null))
    );
  }

  /** Starts a fresh AI model capability test; poll getAiModelTest until completed */
  runAiModelTest(): Observable<AiModelTestStatus | null> {
    return this.http.post<AiModelTestStatus>('/api/integrations/ai/test', {}).pipe(
      catchError(() => of(null))
    );
  }

  getAiPlanningMetrics(count = 30): Observable<AiPlanningMetricsResponse> {
    return this.http.get<AiPlanningMetricsResponse>('/api/integrations/ai/planning-runs', {
      params: { count }
    });
  }

  checkScraperVersion(): Observable<ScraperVersionCheck | null> {
    return this.http.get<ScraperVersionCheck>('/api/integrations/scraper/version').pipe(
      catchError(() => of(null))
    );
  }

  /** Installs the latest (or given) recipe-scrapers version; the sidecar restarts afterwards */
  updateScraper(version?: string): Observable<ScraperUpdateResponse | null> {
    return this.http.post<ScraperUpdateResponse>('/api/integrations/scraper/update', {
      version: version ?? null
    }).pipe(
      catchError(() => of(null))
    );
  }
}
