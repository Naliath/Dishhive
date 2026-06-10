import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { CreateRecipe, Recipe, RecipeListItem } from '../models/recipe.model';

@Injectable({ providedIn: 'root' })
export class RecipesService {
  private apiUrl = '/api/recipes';

  constructor(private http: HttpClient) {}

  getRecipes(search?: string): Observable<RecipeListItem[]> {
    return this.http.get<RecipeListItem[]>(this.apiUrl, {
      params: search ? { search } : {}
    }).pipe(catchError(this.handleError));
  }

  getRecipe(id: string): Observable<Recipe> {
    return this.http.get<Recipe>(`${this.apiUrl}/${id}`)
      .pipe(catchError(this.handleError));
  }

  createRecipe(recipe: CreateRecipe): Observable<Recipe> {
    return this.http.post<Recipe>(this.apiUrl, recipe);
  }

  updateRecipe(id: string, recipe: CreateRecipe): Observable<Recipe> {
    return this.http.put<Recipe>(`${this.apiUrl}/${id}`, recipe);
  }

  deleteRecipe(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`)
      .pipe(catchError(this.handleError));
  }

  /** Import a recipe from a supported external source URL (see docs/features/recipe-import.md) */
  importRecipe(url: string): Observable<Recipe> {
    return this.http.post<Recipe>(`${this.apiUrl}/import`, { url });
  }

  private handleError(error: HttpErrorResponse): Observable<never> {
    console.error('Recipes API error:', error);
    return throwError(() => new Error('Could not reach the recipes API'));
  }
}
