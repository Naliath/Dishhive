import { FreezerClient, FreezerReconciliationDto, ResolveFreezerReservationDto } from '../api/generated/dishhive-api.client';
import { apiResponse } from '../api/api-response';
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { FreezerSuggestions } from '../models/frozen-item.model';

@Injectable({ providedIn: 'root' })
export class FreezerService {
  getReservations(): Observable<FreezerReconciliationDto> { return apiResponse(this.api.getReservations()); }
  resolveReservation(id: string, request: ResolveFreezerReservationDto): Observable<void> {
    return apiResponse(this.api.resolveReservation(id, request));
  }
  private readonly api: FreezerClient;

  private apiUrl = '/api/freezer';

  constructor(http: HttpClient) {
    this.api = new FreezerClient(http, "");
  }

  /** Frozen items from Freezy for planning; disabled result when unconfigured/unreachable */
  getSuggestions(): Observable<FreezerSuggestions> {
    return this.api.getSuggestions().pipe(catchError(() => of({ enabled: false, items: [] })));
  }
}
