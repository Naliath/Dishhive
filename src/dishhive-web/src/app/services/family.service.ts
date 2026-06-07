import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  CreateFamilyMember,
  CreatePreference,
  FamilyMember,
  FamilyMemberPreference,
} from '../models';

@Injectable({ providedIn: 'root' })
export class FamilyService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/family-members';

  list(): Observable<FamilyMember[]> {
    return this.http.get<FamilyMember[]>(this.base);
  }

  get(id: string): Observable<FamilyMember> {
    return this.http.get<FamilyMember>(`${this.base}/${id}`);
  }

  create(dto: CreateFamilyMember): Observable<FamilyMember> {
    return this.http.post<FamilyMember>(this.base, dto);
  }

  update(id: string, dto: CreateFamilyMember): Observable<FamilyMember> {
    return this.http.put<FamilyMember>(`${this.base}/${id}`, dto);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  listPreferences(memberId: string): Observable<FamilyMemberPreference[]> {
    return this.http.get<FamilyMemberPreference[]>(`${this.base}/${memberId}/preferences`);
  }

  addPreference(memberId: string, dto: CreatePreference): Observable<FamilyMemberPreference> {
    return this.http.post<FamilyMemberPreference>(`${this.base}/${memberId}/preferences`, dto);
  }

  removePreference(memberId: string, preferenceId: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${memberId}/preferences/${preferenceId}`);
  }
}
