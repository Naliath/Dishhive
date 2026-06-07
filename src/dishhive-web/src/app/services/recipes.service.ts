import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateRecipe, Recipe, RecipeSummary } from '../models';

@Injectable({ providedIn: 'root' })
export class RecipesService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/recipes';

  list(search?: string, tag?: string): Observable<RecipeSummary[]> {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    if (tag) params = params.set('tag', tag);
    return this.http.get<RecipeSummary[]>(this.base, { params });
  }

  get(id: string): Observable<Recipe> {
    return this.http.get<Recipe>(`${this.base}/${id}`);
  }

  create(dto: CreateRecipe): Observable<Recipe> {
    return this.http.post<Recipe>(this.base, dto);
  }

  update(id: string, dto: CreateRecipe): Observable<Recipe> {
    return this.http.put<Recipe>(`${this.base}/${id}`, dto);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  tags(): Observable<string[]> {
    return this.http.get<string[]>(`${this.base}/tags`);
  }
}
