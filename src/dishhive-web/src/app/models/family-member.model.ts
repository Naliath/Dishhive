export interface FamilyMember {
  id: string;
  name: string;
  isGuest: boolean;
  allergies?: string;
  dietaryConstraints?: string;
  preferenceNotes?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateFamilyMember {
  name: string;
  isGuest: boolean;
  allergies?: string;
  dietaryConstraints?: string;
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
