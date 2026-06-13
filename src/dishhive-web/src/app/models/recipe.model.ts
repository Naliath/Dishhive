export interface RecipeListItem {
  id: string;
  title: string;
  servings: number;
  totalTimeMinutes?: number;
  category?: string;
  /** Local image endpoint when stored in Dishhive, otherwise the remote source URL */
  imageUrl?: string;
  /** True when the image bytes are stored locally in Dishhive */
  hasLocalImage: boolean;
  sourceProvider?: string;
  /** Organization tag names (user-curated) */
  tags: string[];
}

export interface RecipeTag {
  id: string;
  name: string;
}

/**
 * A collection: a named set of explicitly curated recipes. Manual collections are
 * user-managed; auto collections are computed read-only sets with slug ids
 * (e.g. "auto-quick") instead of a Guid.
 */
export interface Cookbook {
  id: string;
  name: string;
  kind: 'manual' | 'auto';
  recipeCount: number;
}

/** A computed auto collection with its enabled state (settings management) */
export interface AutoCollectionInfo {
  id: string;
  name: string;
  recipeCount: number;
  enabled: boolean;
}

/** Recipe library filter */
export interface RecipeFilter {
  search?: string;
  category?: string;
  tags: string[];
  cookbookId?: string;
}

export interface RecipeIngredient {
  id: string;
  sortOrder: number;
  name: string;
  quantity?: number;
  unit?: string;
  originalText: string;
  originalQuantity?: number;
  originalUnit?: string;
}

export interface RecipeStep {
  id: string;
  stepNumber: number;
  instruction: string;
}

export interface CreateRecipeIngredient {
  name: string;
  quantity?: number;
  unit?: string;
  originalText?: string;
}

export interface CreateRecipe {
  title: string;
  description?: string;
  servings: number;
  prepTimeMinutes?: number;
  cookTimeMinutes?: number;
  totalTimeMinutes?: number;
  category?: string;
  keywords?: string;
  imageUrl?: string;
  videoUrl?: string;
  ingredients: CreateRecipeIngredient[];
  steps: { instruction: string }[];
  /** Organization tag names; tags are created when new, synced on update */
  tags: string[];
}

/** Outcome of importing a recipe file (schema.org Recipe JSON) */
export interface RecipeFileImportResult {
  created: number;
  updated: number;
  skipped: number;
  total: number;
  skippedRecipes: { title: string; reason: string }[];
}

export interface Recipe {
  id: string;
  title: string;
  description?: string;
  servings: number;
  prepTimeMinutes?: number;
  cookTimeMinutes?: number;
  totalTimeMinutes?: number;
  category?: string;
  keywords?: string;
  /** Local image endpoint when stored in Dishhive, otherwise the remote source URL */
  imageUrl?: string;
  /** True when the image bytes are stored locally in Dishhive */
  hasLocalImage: boolean;
  videoUrl?: string;
  sourceUrl?: string;
  sourceProvider?: string;
  createdAt: string;
  updatedAt: string;
  ingredients: RecipeIngredient[];
  steps: RecipeStep[];
  /** Organization tag names (user-curated) */
  tags: string[];
  /** Ids of the manual collections this recipe belongs to */
  cookbookIds: string[];
}
