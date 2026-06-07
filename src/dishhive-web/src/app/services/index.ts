import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ImportedRecipe, Recipe } from '../models';

@Injectable({ providedIn: 'root' })
export class RecipeImportService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/recipe-import';

  preview(url: string): Observable<ImportPreviewResult> {
    return this.http.post<ImportPreviewResult>(`${this.base}/preview`, { url });
  }

  save(dto: ImportedRecipe): Observable<Recipe> {
    return this.http.post<Recipe>(`${this.base}/save`, dto);
  }

  providers(): Observable<string[]> {
    return this.http.get<string[]>(`${this.base}/providers`);
  }
}

/**
 * Wraps an imported recipe with metadata about how it was obtained — whether the AI
 * recipe-import agent ran, and whether it was able to teach the system a reusable
 * static blueprint for next time.
 */
export interface ImportPreviewResult {
  recipe: ImportedRecipe;
  usedAgent: boolean;
  blueprintLearned: boolean;
  agentNote: string | null;
}

@Injectable({ providedIn: 'root' })
export class ShoppingListsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/shopping-lists';

  list() {
    return this.http.get<import('../models').ShoppingList[]>(this.base);
  }

  get(id: string) {
    return this.http.get<import('../models').ShoppingList>(`${this.base}/${id}`);
  }

  generate(weekPlanId: string) {
    return this.http.post<import('../models').ShoppingList>(`${this.base}/from-week-plan/${weekPlanId}`, null);
  }

  toggleItem(listId: string, itemId: string, item: import('../models').ShoppingListItem) {
    return this.http.put<import('../models').ShoppingListItem>(`${this.base}/${listId}/items/${itemId}`, item);
  }

  deleteList(id: string) {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  exportMarkdown(id: string): Observable<string> {
    return this.http.get(`${this.base}/${id}/export?format=markdown`, {
      responseType: 'text',
    }) as unknown as Observable<string>;
  }
}

@Injectable({ providedIn: 'root' })
export class HistoryService {
  private readonly http = inject(HttpClient);

  list(from?: string, to?: string) {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<import('../models').DishHistoryEntry[]>('/api/history', { params });
  }

  frequency(top = 50, from?: string, to?: string) {
    let params = new HttpParams().set('top', top);
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<import('../models').DishFrequency[]>('/api/statistics/dish-frequency', { params });
  }

  favorites(memberId: string) {
    return this.http.get<import('../models').DishFavorite[]>(`/api/family-members/${memberId}/favorites`);
  }

  addFavorite(memberId: string, dishLabel: string, recipeId?: string | null) {
    return this.http.post<import('../models').DishFavorite>(`/api/family-members/${memberId}/favorites`, {
      dishLabel,
      recipeId,
    });
  }

  removeFavorite(memberId: string, favoriteId: string) {
    return this.http.delete<void>(`/api/family-members/${memberId}/favorites/${favoriteId}`);
  }
}

export * from './agents.service';
