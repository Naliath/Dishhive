import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { FamilyMember, FamilyMemberSummary, CreateFamilyMember, MemberPreference, FavoriteDish } from '../models/family-member.model';

@Injectable({ providedIn: 'root' })
export class FamilyService {
  private readonly baseUrl = '/api/family';

  constructor(private http: HttpClient) {}

  getAll(includeGuests = true): Observable<FamilyMemberSummary[]> {
    return this.http.get<FamilyMemberSummary[]>(this.baseUrl, {
      params: { includeGuests: includeGuests.toString() }
    });
  }

  getById(id: string): Observable<FamilyMember> {
    return this.http.get<FamilyMember>(`${this.baseUrl}/${id}`);
  }

  create(member: CreateFamilyMember): Observable<FamilyMember> {
    return this.http.post<FamilyMember>(this.baseUrl, member);
  }

  update(id: string, member: CreateFamilyMember): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}`, member);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  addPreference(memberId: string, preferenceType: string, value: string, notes?: string): Observable<MemberPreference> {
    return this.http.post<MemberPreference>(`${this.baseUrl}/${memberId}/preferences`, {
      preferenceType, value, notes
    });
  }

  deletePreference(memberId: string, prefId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${memberId}/preferences/${prefId}`);
  }

  addFavorite(memberId: string, dishName: string, recipeId?: string): Observable<FavoriteDish> {
    return this.http.post<FavoriteDish>(`${this.baseUrl}/${memberId}/favorites`, {
      dishName, recipeId
    });
  }

  deleteFavorite(memberId: string, favId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${memberId}/favorites/${favId}`);
  }
}
