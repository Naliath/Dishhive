import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { ShoppingList } from '../models/shopping-list.model';

@Injectable({ providedIn: 'root' })
export class ShoppingListService {
  private apiUrl = '/api/shoppinglist';

  constructor(private http: HttpClient) {}

  getShoppingList(from: string, to: string): Observable<ShoppingList> {
    return this.http.get<ShoppingList>(this.apiUrl, { params: { from, to } })
      .pipe(catchError(this.handleError));
  }

  private handleError(error: HttpErrorResponse): Observable<never> {
    console.error('ShoppingList API error:', error);
    return throwError(() => new Error('Could not reach the shopping list API'));
  }
}
