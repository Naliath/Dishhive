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
}
