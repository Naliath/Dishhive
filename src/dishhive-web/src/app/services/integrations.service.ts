import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { IntegrationStatusResponse } from '../models/integration-status.model';

@Injectable({ providedIn: 'root' })
export class IntegrationsService {
  private readonly http = inject(HttpClient);

  getStatus(): Observable<IntegrationStatusResponse | null> {
    return this.http.get<IntegrationStatusResponse>('/api/integrations/status').pipe(
      catchError(() => of(null))
    );
  }
}
