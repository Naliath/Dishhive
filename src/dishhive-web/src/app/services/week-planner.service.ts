import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { WeekPlan, PlannedMeal, UpsertPlannedMeal } from '../models/week-plan.model';

@Injectable({ providedIn: 'root' })
export class WeekPlannerService {
  private readonly baseUrl = '/api/weekplanner';

  constructor(private http: HttpClient) {}

  getByWeek(weekStartDate: string): Observable<WeekPlan> {
    return this.http.get<WeekPlan>(`${this.baseUrl}/${weekStartDate}`);
  }

  getOrCreate(weekStartDate: string): Observable<WeekPlan> {
    return this.http.post<WeekPlan>(this.baseUrl, { weekStartDate });
  }

  upsertMeal(weekPlanId: string, meal: UpsertPlannedMeal): Observable<PlannedMeal> {
    return this.http.post<PlannedMeal>(`${this.baseUrl}/${weekPlanId}/meals`, meal);
  }

  deleteMeal(weekPlanId: string, mealId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${weekPlanId}/meals/${mealId}`);
  }

  getShoppingList(weekStartDate: string): Observable<ShoppingListItem[]> {
    return this.http.get<ShoppingListItem[]>(`${this.baseUrl}/${weekStartDate}/shopping-list`);
  }
}

export interface ShoppingListItem {
  name: string;
  amounts: string[];
  recipeNames: string[];
}

