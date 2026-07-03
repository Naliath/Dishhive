export enum DietaryTagKind {
  Allergy = 0,
  Diet = 1
}

export interface DietaryTag {
  id: string;
  name: string;
  kind: DietaryTagKind;
}

/**
 * One dietary tag on one member: the shared tag name plus THIS member's reading
 * of it as excluded ingredient-class names (see ingredient-class.model.ts).
 * `excludedClasses: null` in a request means "no explicit definition" — the API
 * seeds a new link from its presets and keeps an existing link's stored set.
 * Responses always carry the resolved set (empty = not machine-checkable).
 */
export interface DietaryTagEntry {
  name: string;
  excludedClasses: string[] | null;
}

export interface FamilyMember {
  id: string;
  name: string;
  isGuest: boolean;
  allergyTags: DietaryTagEntry[];
  dietTags: DietaryTagEntry[];
  preferenceNotes?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateFamilyMember {
  name: string;
  isGuest: boolean;
  allergyTags: DietaryTagEntry[];
  dietTags: DietaryTagEntry[];
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
