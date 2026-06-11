import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { MealSuggestions, SuggestionStatus } from '../models/meal-suggestion.model';

@Injectable({ providedIn: 'root' })
export class MealSuggestionsService {
  private apiUrl = '/api/plannedmeals/suggestions';

  constructor(private http: HttpClient) {}

  /** Whether AI suggestions are configured; errors read as disabled (freezer pattern) */
  getStatus(): Observable<SuggestionStatus> {
    return this.http.get<SuggestionStatus>(`${this.apiUrl}/status`)
      .pipe(catchError(() => of({ enabled: false })));
  }

  /** Propose dinners for the unplanned days of a week; nothing is persisted */
  suggestWeek(weekStart: string, attendeeIds: string[] = []): Observable<MealSuggestions> {
    return this.http.post<MealSuggestions>(this.apiUrl, { weekStart, attendeeIds });
  }
}
