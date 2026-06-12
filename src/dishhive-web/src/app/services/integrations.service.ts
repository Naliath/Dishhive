import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
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
