import { PlannedMealsClient } from '../api/generated/dishhive-api.client';
import { apiResponse } from '../api/api-response';
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
  private readonly api: PlannedMealsClient;
  private apiUrl = '/api/plannedmeals/suggestions';

  constructor(http: HttpClient) { this.api = new PlannedMealsClient(http, ""); }

  /** Whether AI suggestions are configured; errors read as disabled (freezer pattern) */
  getStatus(): Observable<SuggestionStatus> {
    return this.api.getSuggestionStatus()
      .pipe(catchError(() => of({ enabled: false })));
  }

  /** Propose meals for the explicitly selected days; nothing is persisted */
  suggestWeek(
    weekStart: string,
    selectedDates: string[],
    instructions?: string,
    attendeeIds: string[] = []
  ): Observable<MealSuggestions> {
    return apiResponse(this.api.suggestWeek({
      weekStart,
      attendeeIds,
      selectedDates,
      instructions: instructions?.trim() || undefined
    }));
  }

  acceptSuggestions(
    batchId: string,
    suggestions: MealSuggestion[],
    rejectedSuggestionIds: string[] = []
  ): Observable<AcceptSuggestionsResponse> {
    return apiResponse(this.api.acceptSuggestions({
      batchId,
      rejectedSuggestionIds,
      suggestions: suggestions.map(suggestion => ({
        id: suggestion.id,
        date: suggestion.date,
        mealType: suggestion.mealType,
        course: suggestion.course,
        attendeeIds: suggestion.attendeeIds,
        recipeId: suggestion.recipeId,
        dishName: suggestion.dishName,
        freezyItemRef: suggestion.freezyItemRef,
        freezyItemQuantity: suggestion.freezyItemQuantity,
        sourceUrl: suggestion.sourceUrl,
        candidateVersion: suggestion.candidateVersion
      }))
    }));
  }
}
