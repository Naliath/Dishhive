import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AutoCollectionInfo, Cookbook, RecipeListItem } from '../models/recipe.model';

@Injectable({ providedIn: 'root' })
export class CookbooksService {
  private apiUrl = '/api/cookbooks';

  constructor(private http: HttpClient) {}

  /** Manual collections followed by the computed auto collections */
  getCookbooks(): Observable<Cookbook[]> {
    return this.http.get<Cookbook[]>(this.apiUrl)
      .pipe(catchError(this.handleError));
  }

  createCookbook(name: string): Observable<Cookbook> {
    return this.http.post<Cookbook>(this.apiUrl, { name });
  }

  renameCookbook(id: string, name: string): Observable<Cookbook> {
    return this.http.put<Cookbook>(`${this.apiUrl}/${id}`, { name });
  }

  deleteCookbook(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`)
      .pipe(catchError(this.handleError));
  }

  /** The recipes in a collection; id is a manual Guid or an auto slug */
  getCookbookRecipes(id: string): Observable<RecipeListItem[]> {
    return this.http.get<RecipeListItem[]>(`${this.apiUrl}/${id}/recipes`)
      .pipe(catchError(this.handleError));
  }

  /** Adds recipes to a manual collection (already-present ones are skipped) */
  addRecipes(id: string, recipeIds: string[]): Observable<Cookbook> {
    return this.http.post<Cookbook>(`${this.apiUrl}/${id}/recipes`, { recipeIds });
  }

  removeRecipe(id: string, recipeId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}/recipes/${recipeId}`)
      .pipe(catchError(this.handleError));
  }

  /** Full sync of one recipe's manual collection memberships */
  setRecipeCookbooks(recipeId: string, cookbookIds: string[]): Observable<string[]> {
    return this.http.put<string[]>(`/api/recipes/${recipeId}/cookbooks`, { cookbookIds });
  }

  /** All auto collections with their enabled state (settings management) */
  getAutoCollections(): Observable<AutoCollectionInfo[]> {
    return this.http.get<AutoCollectionInfo[]>(`${this.apiUrl}/auto-collections`)
      .pipe(catchError(this.handleError));
  }

  setAutoCollectionEnabled(id: string, enabled: boolean): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/auto-collections/${id}`, { enabled });
  }

  private handleError(error: HttpErrorResponse): Observable<never> {
    console.error('Collections API error:', error);
    return throwError(() => new Error('Could not reach the collections API'));
  }
}
