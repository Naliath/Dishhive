export enum DietaryTagKind {
  Allergy = 0,
  Diet = 1
}

export interface DietaryTag {
  id: string;
  name: string;
  kind: DietaryTagKind;
}

export interface FamilyMember {
  id: string;
  name: string;
  isGuest: boolean;
  allergyTags: string[];
  dietTags: string[];
  preferenceNotes?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateFamilyMember {
  name: string;
  isGuest: boolean;
  allergyTags: string[];
  dietTags: string[];
  preferenceNotes?: string;
}

export interface UpdateFamilyMember extends CreateFamilyMember {
  isActive: boolean;
}

export interface FamilyMemberFavorite {
  id: string;
  familyMemberId: string;
  recipeId?: string;
  dishName?: string;
}

export interface CreateFamilyMemberFavorite {
  recipeId?: string;
  dishName?: string;
}
