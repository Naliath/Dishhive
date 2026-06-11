import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { PlannedMeal, CreatePlannedMeal, EatenStatus } from '../models/planned-meal.model';

@Injectable({ providedIn: 'root' })
export class PlannedMealsService {
  private apiUrl = '/api/plannedmeals';

  constructor(private http: HttpClient) {}

  getMeals(from: string, to: string): Observable<PlannedMeal[]> {
    return this.http.get<PlannedMeal[]>(this.apiUrl, { params: { from, to } })
      .pipe(catchError(this.handleError));
  }

  createMeal(meal: CreatePlannedMeal): Observable<PlannedMeal> {
    return this.http.post<PlannedMeal>(this.apiUrl, meal);
  }

  updateMeal(id: string, meal: CreatePlannedMeal): Observable<PlannedMeal> {
    return this.http.put<PlannedMeal>(`${this.apiUrl}/${id}`, meal);
  }

  deleteMeal(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`)
      .pipe(catchError(this.handleError));
  }

  /** Mark a past meal as eaten/skipped; null clears the mark */
  setEaten(id: string, status: EatenStatus | null): Observable<PlannedMeal> {
    return this.http.put<PlannedMeal>(`${this.apiUrl}/${id}/eaten`, { status });
  }

  /** Set a member's 1-5 rating; re-rating overwrites */
  setRating(id: string, memberId: string, rating: number): Observable<PlannedMeal> {
    return this.http.put<PlannedMeal>(`${this.apiUrl}/${id}/ratings/${memberId}`, { rating });
  }

  deleteRating(id: string, memberId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}/ratings/${memberId}`);
  }

  private handleError(error: HttpErrorResponse): Observable<never> {
    console.error('PlannedMeals API error:', error);
    return throwError(() => new Error('Could not reach the planner API'));
  }
}
