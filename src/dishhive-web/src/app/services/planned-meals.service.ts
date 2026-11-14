import { PlannedMealsClient } from '../api/generated/dishhive-api.client';
import { apiResponse } from '../api/api-response';
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { PlannedMeal, CreatePlannedMeal, EatenStatus } from '../models/planned-meal.model';

@Injectable({ providedIn: 'root' })
export class PlannedMealsService {
  getMeal(id: string): Observable<PlannedMeal> { return apiResponse(this.api.getMeal(id)); }
  private readonly api: PlannedMealsClient;

  private apiUrl = '/api/plannedmeals';

  constructor(http: HttpClient) {
    this.api = new PlannedMealsClient(http, "");
  }

  getMeals(from: string, to: string): Observable<PlannedMeal[]> {
    return apiResponse(this.api.getMeals(from, to));
  }

  createMeal(meal: CreatePlannedMeal): Observable<PlannedMeal> {
    return apiResponse(this.api.createMeal({ ...meal, freezyItemQuantity: meal.freezyItemQuantity ?? 1 }));
  }

  updateMeal(id: string, meal: CreatePlannedMeal): Observable<PlannedMeal> {
    return apiResponse(this.api.updateMeal(id, { ...meal, freezyItemQuantity: meal.freezyItemQuantity ?? 1 }));
  }

  deleteMeal(id: string): Observable<void> {
    return apiResponse(this.api.deleteMeal(id));
  }

  /** Mark a past meal as eaten/skipped; null clears the mark */
  setEaten(id: string, status: EatenStatus | null): Observable<PlannedMeal> {
    return apiResponse(this.api.setEaten(id, { status: status ?? undefined }));
  }

  /** Attach a recipe to a meal (resolves a "still to decide" shopping list entry) */
  setRecipe(id: string, recipeId: string): Observable<PlannedMeal> {
    return apiResponse(this.api.setRecipe(id, { recipeId }));
  }

  /** Set a member's 1-5 rating; re-rating overwrites */
  setRating(id: string, memberId: string, rating: number): Observable<PlannedMeal> {
    return apiResponse(this.api.setRating(id, memberId, { rating }));
  }

  deleteRating(id: string, memberId: string): Observable<void> {
    return apiResponse(this.api.deleteRating(id, memberId));
  }

}
