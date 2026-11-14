import { CookbooksClient, RecipesClient } from '../api/generated/dishhive-api.client';
import { apiResponse } from '../api/api-response';
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AutoCollectionInfo, Cookbook, RecipeListItem } from '../models/recipe.model';

@Injectable({ providedIn: 'root' })
export class CookbooksService {
  private readonly api: CookbooksClient;
  private readonly recipesApi: RecipesClient;

  private apiUrl = '/api/cookbooks';

  constructor(http: HttpClient) {
    this.api = new CookbooksClient(http, "");
    this.recipesApi = new RecipesClient(http, "");
  }

  /** Manual collections followed by the computed auto collections */
  getCookbooks(): Observable<Cookbook[]> {
    return apiResponse(this.api.getCookbooks());
  }

  createCookbook(name: string): Observable<Cookbook> {
    return apiResponse(this.api.createCookbook({ name }));
  }

  renameCookbook(id: string, name: string): Observable<Cookbook> {
    return apiResponse(this.api.updateCookbook(id, { name }));
  }

  deleteCookbook(id: string): Observable<void> {
    return apiResponse(this.api.deleteCookbook(id));
  }

  /** The recipes in a collection; id is a manual Guid or an auto slug */
  getCookbookRecipes(id: string): Observable<RecipeListItem[]> {
    return apiResponse(this.api.getCookbookRecipes(id));
  }

  /** Adds recipes to a manual collection (already-present ones are skipped) */
  addRecipes(id: string, recipeIds: string[]): Observable<Cookbook> {
    return apiResponse(this.api.addRecipes(id, { recipeIds }));
  }

  removeRecipe(id: string, recipeId: string): Observable<void> {
    return apiResponse(this.api.removeRecipe(id, recipeId));
  }

  /** Full sync of one recipe's manual collection memberships */
  setRecipeCookbooks(recipeId: string, cookbookIds: string[]): Observable<string[]> {
    return apiResponse(this.recipesApi.setRecipeCookbooks(recipeId, { cookbookIds }));
  }

  /** All auto collections with their enabled state (settings management) */
  getAutoCollections(): Observable<AutoCollectionInfo[]> {
    return apiResponse(this.api.getAutoCollections());
  }

  setAutoCollectionEnabled(id: string, enabled: boolean): Observable<void> {
    return apiResponse(this.api.setAutoCollectionEnabled(id, { enabled }));
  }

}
