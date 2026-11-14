import { FamilyMembersClient, DietaryTagsClient } from '../api/generated/dishhive-api.client';
import { apiResponse } from '../api/api-response';
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  FamilyMember,
  CreateFamilyMember,
  UpdateFamilyMember,
  FamilyMemberFavorite,
  CreateFamilyMemberFavorite,
  DietaryTag,
  DietaryTagKind
} from '../models/family-member.model';

@Injectable({ providedIn: 'root' })
export class FamilyMembersService {
  private readonly api: FamilyMembersClient;
  private readonly tagsApi: DietaryTagsClient;

  private apiUrl = '/api/familymembers';

  constructor(http: HttpClient) {
    this.api = new FamilyMembersClient(http, "");
    this.tagsApi = new DietaryTagsClient(http, "");
  }

  getMembers(includeInactive = false): Observable<FamilyMember[]> {
    return apiResponse(this.api.getMembers(includeInactive));
  }

  getMember(id: string): Observable<FamilyMember> {
    return apiResponse(this.api.getMember(id));
  }

  createMember(member: CreateFamilyMember): Observable<FamilyMember> {
    return apiResponse(this.api.createMember(member));
  }

  updateMember(id: string, member: UpdateFamilyMember): Observable<FamilyMember> {
    return apiResponse(this.api.updateMember(id, member));
  }

  deleteMember(id: string): Observable<void> {
    return apiResponse(this.api.deleteMember(id));
  }

  /** The pool of allergy/diet tags in use, for tag autocomplete */
  getDietaryTags(): Observable<DietaryTag[]> {
    return apiResponse(this.tagsApi.getTags());
  }

  /**
   * The built-in preset excluded classes for a tag name, so the form can show
   * what a not-yet-saved tag will mean (empty = not machine-checkable)
   */
  getTagPreset(name: string, kind: DietaryTagKind): Observable<{ excludedClasses: string[] }> {
    return apiResponse(this.tagsApi.getPreset(name, kind));
  }

  getFavorites(memberId: string): Observable<FamilyMemberFavorite[]> {
    return apiResponse(this.api.getFavorites(memberId));
  }

  addFavorite(memberId: string, favorite: CreateFamilyMemberFavorite): Observable<FamilyMemberFavorite> {
    return apiResponse(this.api.addFavorite(memberId, favorite));
  }

  deleteFavorite(memberId: string, favoriteId: string): Observable<void> {
    return apiResponse(this.api.deleteFavorite(memberId, favoriteId));
  }

}
