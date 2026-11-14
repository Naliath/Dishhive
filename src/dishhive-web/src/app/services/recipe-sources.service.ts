import { RecipesClient } from '../api/generated/dishhive-api.client';
import { apiResponse } from '../api/api-response';
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { RecipeSource } from '../models/recipe-source.model';

/** Known recipe sources for the week-planner's @[Source] autocomplete */
@Injectable({ providedIn: 'root' })
export class RecipeSourcesService {
  private readonly api: RecipesClient;

  constructor(http: HttpClient) {
    this.api = new RecipesClient(http, "");
  }

  /** Errors read as an empty list — mentions degrade to plain text */
  getSources(): Observable<RecipeSource[]> {
    return this.api.getSources().pipe(catchError(() => of([])));
  }
}
