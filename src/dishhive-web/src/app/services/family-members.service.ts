import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  FamilyMember,
  CreateFamilyMember,
  UpdateFamilyMember,
  FamilyMemberFavorite,
  CreateFamilyMemberFavorite,
  DietaryTag
} from '../models/family-member.model';

@Injectable({ providedIn: 'root' })
export class FamilyMembersService {
  private apiUrl = '/api/familymembers';

  constructor(private http: HttpClient) {}

  getMembers(includeInactive = false): Observable<FamilyMember[]> {
    return this.http.get<FamilyMember[]>(this.apiUrl, {
      params: { includeInactive }
    }).pipe(catchError(this.handleError));
  }

  getMember(id: string): Observable<FamilyMember> {
    return this.http.get<FamilyMember>(`${this.apiUrl}/${id}`)
      .pipe(catchError(this.handleError));
  }

  createMember(member: CreateFamilyMember): Observable<FamilyMember> {
    return this.http.post<FamilyMember>(this.apiUrl, member)
      .pipe(catchError(this.handleError));
  }

  updateMember(id: string, member: UpdateFamilyMember): Observable<FamilyMember> {
    return this.http.put<FamilyMember>(`${this.apiUrl}/${id}`, member)
      .pipe(catchError(this.handleError));
  }

  deleteMember(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`)
      .pipe(catchError(this.handleError));
  }

  /** The pool of allergy/diet tags in use, for tag autocomplete */
  getDietaryTags(): Observable<DietaryTag[]> {
    return this.http.get<DietaryTag[]>('/api/dietarytags')
      .pipe(catchError(this.handleError));
  }

  getFavorites(memberId: string): Observable<FamilyMemberFavorite[]> {
    return this.http.get<FamilyMemberFavorite[]>(`${this.apiUrl}/${memberId}/favorites`)
      .pipe(catchError(this.handleError));
  }

  addFavorite(memberId: string, favorite: CreateFamilyMemberFavorite): Observable<FamilyMemberFavorite> {
    return this.http.post<FamilyMemberFavorite>(`${this.apiUrl}/${memberId}/favorites`, favorite)
      .pipe(catchError(this.handleError));
  }

  deleteFavorite(memberId: string, favoriteId: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${memberId}/favorites/${favoriteId}`)
      .pipe(catchError(this.handleError));
  }

  private handleError(error: HttpErrorResponse): Observable<never> {
    console.error('FamilyMembers API error:', error);
    return throwError(() => new Error('Could not reach the family members API'));
  }
}
