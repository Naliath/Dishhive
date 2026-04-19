export interface FamilyMember {
  id: string;
  name: string;
  isGuest: boolean;
  guestFrom?: string;
  guestUntil?: string;
  preferences: MemberPreference[];
  favoriteDishes: FavoriteDish[];
  createdAt: string;
  updatedAt: string;
}

export interface FamilyMemberSummary {
  id: string;
  name: string;
  isGuest: boolean;
  guestUntil?: string;
}

export interface MemberPreference {
  id: string;
  preferenceType: PreferenceType;
  value: string;
  notes?: string;
  createdAt: string;
}

export interface FavoriteDish {
  id: string;
  recipeId?: string;
  dishName: string;
  createdAt: string;
}

export type PreferenceType = 'Allergy' | 'Intolerance' | 'DietaryConstraint' | 'Dislike' | 'Preference';

export interface CreateFamilyMember {
  name: string;
  isGuest?: boolean;
  guestFrom?: string;
  guestUntil?: string;
}
