export type RecipeListItem = import('../api/generated/dishhive-api.client').RecipeListItemDto;

export type RecipeTag = import('../api/generated/dishhive-api.client').RecipeTagDto;

/**
 * A collection: a named set of explicitly curated recipes. Manual collections are
 * user-managed; auto collections are computed read-only sets with slug ids
 * (e.g. "auto-quick") instead of a Guid.
 */
export type Cookbook = import('../api/generated/dishhive-api.client').CookbookDto;

/** A computed auto collection with its enabled state (settings management) */
export type AutoCollectionInfo = import('../api/generated/dishhive-api.client').AutoCollectionDto;

/** Recipe library filter */
export interface RecipeFilter {
  search?: string;
  category?: string;
  tags: string[];
  cookbookId?: string;
}

export type RecipeIngredient = import('../api/generated/dishhive-api.client').RecipeIngredientDto;

export type RecipeStep = import('../api/generated/dishhive-api.client').RecipeStepDto;

export type CreateRecipeIngredient = import('../api/generated/dishhive-api.client').CreateRecipeIngredientDto;

export type CreateRecipe = import('../api/generated/dishhive-api.client').CreateRecipeDto;

/** Assessment state of a recipe's dietary facts (mirrors the API enum) */
export { DietaryFactsStatus } from '../api/generated/dishhive-api.client';

export type RecipeDietaryFactsHistory = import('../api/generated/dishhive-api.client').RecipeDietaryFactsHistoryDto;

/**
 * A recipe's dietary facts: the ingredient classes it contains plus how that was
 * established. On an Unassessed recipe the empty contains list means "unknown",
 * never "contains nothing".
 */
export type RecipeDietaryFacts = import('../api/generated/dishhive-api.client').RecipeDietaryFactsDto;

/** Library-wide facts progress (settings page, polled while a backfill runs) */
export type RecipeFactsStatus = import('../api/generated/dishhive-api.client').RecipeFactsStatusDto;

/** Outcome of importing a recipe file (schema.org Recipe JSON) */
export type RecipeFileImportResult = import('../api/generated/dishhive-api.client').RecipeFileImportResultDto;

export type Recipe = import('../api/generated/dishhive-api.client').RecipeDto;
