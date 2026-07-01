import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { RecipeSource } from '../models/recipe-source.model';

/** Known recipe sources for the week-planner's @[Source] autocomplete */
@Injectable({ providedIn: 'root' })
export class RecipeSourcesService {
  constructor(private http: HttpClient) {}

  /** Errors read as an empty list — mentions degrade to plain text */
  getSources(): Observable<RecipeSource[]> {
    return this.http.get<RecipeSource[]>('/api/recipes/sources')
      .pipe(catchError(() => of([])));
  }
}
