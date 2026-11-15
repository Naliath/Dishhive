import { RecipesClient, RecipeTagsClient, RecipeImportCapabilitiesDto } from '../api/generated/dishhive-api.client';
import { apiResponse } from '../api/api-response';
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import {
  CreateRecipe, Recipe, RecipeDietaryFacts, RecipeFactsStatus, RecipeFileImportResult,
  RecipeFilter, RecipeListItem, RecipeTag
} from '../models/recipe.model';

@Injectable({ providedIn: 'root' })
export class RecipesService {
  private readonly api: RecipesClient;
  private readonly tagApi: RecipeTagsClient;

  private apiUrl = '/api/recipes';

  constructor(http: HttpClient) {
    this.api = new RecipesClient(http, "");
    this.tagApi = new RecipeTagsClient(http, "");
  }

  getRecipes(
    search?: string, category?: string, tags: string[] = [], cookbookId?: string
  ): Observable<RecipeListItem[]> {
    return apiResponse(this.api.getRecipes(search?.trim() || undefined, category?.trim() || undefined,
      tags.length ? tags.join(',') : undefined, cookbookId, undefined, 500));
  }

  getRecipesPage(
    search: string | undefined, category: string | undefined, tags: string[],
    cookbookId: string | undefined, page: number, pageSize: number
  ): Observable<{ items: RecipeListItem[]; totalCount: number }> {
    return apiResponse(this.api.getRecipesPage(search?.trim() || undefined, category?.trim() || undefined,
      tags.length ? tags.join(',') : undefined, cookbookId, page, pageSize));
  }

  /** Convenience overload taking the library filter shape */
  getRecipesFiltered(filter: RecipeFilter): Observable<RecipeListItem[]> {
    return this.getRecipes(filter.search, filter.category, filter.tags, filter.cookbookId);
  }

  /** Distinct categories in use, for the library filter */
  getCategories(): Observable<string[]> {
    return apiResponse(this.api.getCategories());
  }

  /** Distinct ingredient names in use, for the recipe form autocomplete */
  getIngredientNames(): Observable<string[]> {
    return apiResponse(this.api.getIngredientNames());
  }

  /** The pool of recipe tags in use, for filter chips and autocomplete */
  getRecipeTags(): Observable<RecipeTag[]> {
    return apiResponse(this.tagApi.getTags());
  }

  getRecipe(id: string): Observable<Recipe> {
    return apiResponse(this.api.getRecipe(id));
  }

  createRecipe(recipe: CreateRecipe): Observable<Recipe> {
    return apiResponse(this.api.createRecipe({ ...recipe, containsClasses: recipe.containsClasses ?? undefined }));
  }

  updateRecipe(id: string, recipe: CreateRecipe): Observable<Recipe> {
    return apiResponse(this.api.updateRecipe(id, { ...recipe, containsClasses: recipe.containsClasses ?? undefined }));
  }

  /** Replaces the stored image from a local file or browser camera capture */
  setRecipeImage(id: string, file: File): Observable<void> {
    return apiResponse(this.api.setRecipeImage(id, { data: file, fileName: file.name || 'recipe-photo' }));
  }

  /** Removes the stored image and any retained remote source URL */
  deleteRecipeImage(id: string): Observable<void> {
    return apiResponse(this.api.deleteRecipeImage(id));
  }

  deleteRecipe(id: string): Observable<void> {
    return apiResponse(this.api.deleteRecipe(id));
  }

  /** Import a recipe from a supported external source URL (see docs/features/recipe-import.md) */
  importRecipe(url: string): Observable<Recipe> {
    return apiResponse(this.api.importRecipe({ url }));
  }

  getImportCapabilities(): Observable<RecipeImportCapabilitiesDto> { return apiResponse(this.api.getImportCapabilities()); }
  checkImportImageCapability(): Observable<RecipeImportCapabilitiesDto> { return apiResponse(this.api.checkImportImageCapability()); }
  importRecipeText(text: string): Observable<Recipe> { return apiResponse(this.api.importRecipeText({ text })); }
  importRecipeImage(file: File): Observable<Recipe> {
    return apiResponse(this.api.importRecipeImage({ data: file, fileName: file.name || 'recipe-photo' }));
  }

  /** Sets a recipe's dietary facts explicitly; they become user-confirmed */
  setRecipeFacts(id: string, contains: string[]): Observable<RecipeDietaryFacts> {
    return apiResponse(this.api.setRecipeFacts(id, { contains }));
  }

  /** Library-wide dietary-facts progress (settings page) */
  getFactsStatus(): Observable<RecipeFactsStatus> {
    return apiResponse(this.api.getFactsStatus());
  }

  /** Queues all unassessed recipes for AI facts assessment */
  backfillFacts(): Observable<{ enqueued: number }> {
    return apiResponse(this.api.backfillFacts());
  }

  /** Where the library export (schema.org Recipe JSON) downloads from */
  readonly exportUrl = `${this.apiUrl}/export`;

  /** Import recipes from a schema.org Recipe JSON file, including Dishhive's own export */
  importRecipesFile(file: File): Observable<RecipeFileImportResult> {
    return apiResponse(this.api.importRecipesFile({ data: file, fileName: file.name }));
  }

}
