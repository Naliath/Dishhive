import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { CreateRecipe, Recipe, RecipeFileImportResult, RecipeFilter, RecipeListItem, RecipeTag } from '../models/recipe.model';

@Injectable({ providedIn: 'root' })
export class RecipesService {
  private apiUrl = '/api/recipes';

  constructor(private http: HttpClient) {}

  getRecipes(search?: string, category?: string, tags: string[] = []): Observable<RecipeListItem[]> {
    const params: Record<string, string> = {};
    if (search?.trim()) {
      params['search'] = search.trim();
    }
    if (category?.trim()) {
      params['category'] = category.trim();
    }
    if (tags.length > 0) {
      params['tags'] = tags.join(',');
    }
    return this.http.get<RecipeListItem[]>(this.apiUrl, { params })
      .pipe(catchError(this.handleError));
  }

  /** Convenience overload taking the library filter shape cookbooks store */
  getRecipesFiltered(filter: RecipeFilter): Observable<RecipeListItem[]> {
    return this.getRecipes(filter.search, filter.category, filter.tags);
  }

  /** Distinct categories in use, for the library filter */
  getCategories(): Observable<string[]> {
    return this.http.get<string[]>(`${this.apiUrl}/categories`)
      .pipe(catchError(this.handleError));
  }

  /** Distinct ingredient names in use, for the recipe form autocomplete */
  getIngredientNames(): Observable<string[]> {
    return this.http.get<string[]>(`${this.apiUrl}/ingredients`)
      .pipe(catchError(this.handleError));
  }

  /** The pool of recipe tags in use, for filter chips and autocomplete */
  getRecipeTags(): Observable<RecipeTag[]> {
    return this.http.get<RecipeTag[]>('/api/recipetags')
      .pipe(catchError(this.handleError));
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

  /** Where the library export (schema.org Recipe JSON) downloads from */
  readonly exportUrl = `${this.apiUrl}/export`;

  /** Import recipes from a schema.org Recipe JSON file, including Dishhive's own export */
  importRecipesFile(file: File): Observable<RecipeFileImportResult> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<RecipeFileImportResult>(`${this.apiUrl}/import/file`, form);
  }

  private handleError(error: HttpErrorResponse): Observable<never> {
    console.error('Recipes API error:', error);
    return throwError(() => new Error('Could not reach the recipes API'));
  }
}
