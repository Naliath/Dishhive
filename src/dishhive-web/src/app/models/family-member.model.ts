export { DietaryTagKind } from '../api/generated/dishhive-api.client';

export type DietaryTag = import('../api/generated/dishhive-api.client').DietaryTagDto;

/**
 * One dietary tag on one member: the shared tag name plus THIS member's reading
 * of it as excluded ingredient-class names (see ingredient-class.model.ts).
 * `excludedClasses: null` in a request means "no explicit definition" — the API
 * seeds a new link from its presets and keeps an existing link's stored set.
 * Responses always carry the resolved set (empty = not machine-checkable).
 */
export type DietaryTagEntry = import('../api/generated/dishhive-api.client').DietaryTagEntryDto;

export type FamilyMember = import('../api/generated/dishhive-api.client').FamilyMemberDto;

export type CreateFamilyMember = import('../api/generated/dishhive-api.client').CreateFamilyMemberDto;

export type UpdateFamilyMember = import('../api/generated/dishhive-api.client').UpdateFamilyMemberDto;

export type FamilyMemberFavorite = import('../api/generated/dishhive-api.client').FamilyMemberFavoriteDto;

export type CreateFamilyMemberFavorite = import('../api/generated/dishhive-api.client').CreateFamilyMemberFavoriteDto;
