import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Cookbook, CreateCookbook } from '../models/recipe.model';

@Injectable({ providedIn: 'root' })
export class CookbooksService {
  private apiUrl = '/api/cookbooks';

  constructor(private http: HttpClient) {}

  getCookbooks(): Observable<Cookbook[]> {
    return this.http.get<Cookbook[]>(this.apiUrl)
      .pipe(catchError(this.handleError));
  }

  createCookbook(cookbook: CreateCookbook): Observable<Cookbook> {
    return this.http.post<Cookbook>(this.apiUrl, cookbook);
  }

  updateCookbook(id: string, cookbook: CreateCookbook): Observable<Cookbook> {
    return this.http.put<Cookbook>(`${this.apiUrl}/${id}`, cookbook);
  }

  deleteCookbook(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`)
      .pipe(catchError(this.handleError));
  }

  private handleError(error: HttpErrorResponse): Observable<never> {
    console.error('Cookbooks API error:', error);
    return throwError(() => new Error('Could not reach the cookbooks API'));
  }
}
