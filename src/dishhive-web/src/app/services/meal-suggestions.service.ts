import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  AcceptSuggestionsResponse,
  MealSuggestion,
  MealSuggestions,
  SuggestionStatus
} from '../models/meal-suggestion.model';

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
  suggestWeek(weekStart: string, instructions?: string, attendeeIds: string[] = []): Observable<MealSuggestions> {
    return this.http.post<MealSuggestions>(this.apiUrl, {
      weekStart,
      attendeeIds,
      instructions: instructions?.trim() || undefined
    });
  }

  acceptSuggestions(batchId: string, suggestions: MealSuggestion[]): Observable<AcceptSuggestionsResponse> {
    return this.http.post<AcceptSuggestionsResponse>(`${this.apiUrl}/accept`, {
      batchId,
      suggestions: suggestions.map(suggestion => ({
        id: suggestion.id,
        date: suggestion.date,
        recipeId: suggestion.recipeId,
        dishName: suggestion.dishName,
        freezyItemRef: suggestion.freezyItemRef,
        freezyItemQuantity: suggestion.freezyItemQuantity,
        sourceUrl: suggestion.sourceUrl
      }))
    });
  }
}
