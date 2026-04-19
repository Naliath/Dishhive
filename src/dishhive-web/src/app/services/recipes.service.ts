import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Recipe, RecipeSummary, CreateRecipe, ImportedRecipe } from '../models/recipe.model';

@Injectable({ providedIn: 'root' })
export class RecipesService {
  private readonly baseUrl = '/api/recipes';

  constructor(private http: HttpClient) {}

  getAll(search?: string, tag?: string): Observable<RecipeSummary[]> {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    if (tag) params = params.set('tag', tag);
    return this.http.get<RecipeSummary[]>(this.baseUrl, { params });
  }

  getById(id: string, units?: string): Observable<Recipe> {
    const params = units ? new HttpParams().set('units', units) : undefined;
    return this.http.get<Recipe>(`${this.baseUrl}/${id}`, { params });
  }

  create(recipe: CreateRecipe): Observable<Recipe> {
    return this.http.post<Recipe>(this.baseUrl, recipe);
  }

  update(id: string, recipe: CreateRecipe): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}`, recipe);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  importFromUrl(url: string): Observable<ImportedRecipe> {
    return this.http.post<ImportedRecipe>(`${this.baseUrl}/import`, { url });
  }

  importAndSave(imported: ImportedRecipe): Observable<Recipe> {
    return this.http.post<Recipe>(`${this.baseUrl}/import/save`, imported);
  }
}
