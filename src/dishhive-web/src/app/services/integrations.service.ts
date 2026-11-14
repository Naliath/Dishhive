import { IntegrationsClient } from '../api/generated/dishhive-api.client';
import { apiResponse } from '../api/api-response';
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
  private readonly api = new IntegrationsClient(inject(HttpClient), "");

  getStatus(): Observable<IntegrationStatusResponse | null> {
    return this.api.getStatus().pipe(
      catchError(() => of(null))
    );
  }

  getAiModelTest(): Observable<AiModelTestStatus | null> {
    return this.api.getAiModelTest().pipe(
      catchError(() => of(null))
    );
  }

  /** Starts a fresh AI model capability test; poll getAiModelTest until completed */
  runAiModelTest(): Observable<AiModelTestStatus | null> {
    return this.api.runAiModelTest().pipe(
      catchError(() => of(null))
    );
  }

  getAiPlanningMetrics(count = 30, filters: {
    from?: string;
    task?: string;
    model?: string;
    fingerprint?: string;
    outcome?: string;
    operationId?: string;
  } = {}): Observable<AiPlanningMetricsResponse> {
    return apiResponse(this.api.getAiPlanningRuns(count, filters.from, filters.task, filters.model,
      filters.fingerprint, filters.outcome, filters.operationId));
  }

  checkScraperVersion(): Observable<ScraperVersionCheck | null> {
    return this.api.getScraperVersion().pipe(
      catchError(() => of(null))
    );
  }

  /** Installs the latest (or given) recipe-scrapers version; the sidecar restarts afterwards */
  updateScraper(version?: string): Observable<ScraperUpdateResponse | null> {
    return this.api.updateScraper({ version: version ?? null }).pipe(
      catchError(() => of(null))
    );
  }
}
