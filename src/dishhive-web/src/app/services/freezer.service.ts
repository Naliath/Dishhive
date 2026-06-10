import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { FreezerSuggestions } from '../models/frozen-item.model';

@Injectable({ providedIn: 'root' })
export class FreezerService {
  private apiUrl = '/api/freezer';

  constructor(private http: HttpClient) {}

  /** Frozen items from Freezy for planning; disabled result when unconfigured/unreachable */
  getSuggestions(): Observable<FreezerSuggestions> {
    return this.http.get<FreezerSuggestions>(`${this.apiUrl}/suggestions`).pipe(
      catchError(() => of({ enabled: false, items: [] }))
    );
  }
}
