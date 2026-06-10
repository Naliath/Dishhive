import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { DishStatistics, MemberStatistics } from '../models/statistics.model';

@Injectable({ providedIn: 'root' })
export class StatisticsService {
  private apiUrl = '/api/statistics';

  constructor(private http: HttpClient) {}

  getDishStatistics(from?: string, to?: string): Observable<DishStatistics> {
    const params: Record<string, string> = {};
    if (from) params['from'] = from;
    if (to) params['to'] = to;
    return this.http.get<DishStatistics>(`${this.apiUrl}/dishes`, { params })
      .pipe(catchError(this.handleError));
  }

  getMemberStatistics(memberId: string): Observable<MemberStatistics> {
    return this.http.get<MemberStatistics>(`${this.apiUrl}/members/${memberId}`)
      .pipe(catchError(this.handleError));
  }

  private handleError(error: HttpErrorResponse): Observable<never> {
    console.error('Statistics API error:', error);
    return throwError(() => new Error('Could not reach the statistics API'));
  }
}
