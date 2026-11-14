import { ShoppingListClient } from '../api/generated/dishhive-api.client';
import { apiResponse } from '../api/api-response';
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { ShoppingList } from '../models/shopping-list.model';

@Injectable({ providedIn: 'root' })
export class ShoppingListService {
  private readonly api: ShoppingListClient;

  private apiUrl = '/api/shoppinglist';

  constructor(http: HttpClient) {
    this.api = new ShoppingListClient(http, "");
  }

  getShoppingList(from: string, to: string): Observable<ShoppingList> {
    return apiResponse(this.api.getShoppingList(from, to));
  }

}
