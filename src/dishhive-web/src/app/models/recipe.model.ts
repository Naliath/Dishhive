export interface Recipe {
  id: string;
  title: string;
  description?: string;
  servings: number;
  prepTimeMinutes?: number;
  cookTimeMinutes?: number;
  pictureUrl?: string;
  videoUrl?: string;
  sourceUrl?: string;
  sourceName?: string;
  tags: string[];
  ingredients: RecipeIngredient[];
  steps: RecipeStep[];
  createdAt: string;
  updatedAt: string;
}

export interface RecipeSummary {
  id: string;
  title: string;
  description?: string;
  servings: number;
  pictureUrl?: string;
  tags: string[];
  createdAt: string;
}

export interface RecipeIngredient {
  id: string;
  name: string;
  quantity?: number;
  unit?: string;
  originalQuantity?: number;
  originalUnit?: string;
  notes?: string;
  sortOrder: number;
}

export interface RecipeStep {
  id: string;
  stepNumber: number;
  instruction: string;
}

export interface ImportedRecipe {
  title: string;
  description?: string;
  ingredients: ImportedIngredient[];
  steps: string[];
  servings?: number;
  pictureUrl?: string;
  videoUrl?: string;
  sourceUrl: string;
  sourceName: string;
  sourceRawData?: string;
}

export interface ImportedIngredient {
  rawText: string;
  name: string;
  originalQuantity?: number;
  originalUnit?: string;
}

export interface CreateRecipe {
  title: string;
  description?: string;
  servings: number;
  prepTimeMinutes?: number;
  cookTimeMinutes?: number;
  pictureUrl?: string;
  videoUrl?: string;
  tags?: string[];
  ingredients?: CreateRecipeIngredient[];
  steps?: CreateRecipeStep[];
}

export interface CreateRecipeIngredient {
  name: string;
  quantity?: number;
  unit?: string;
  originalQuantity?: number;
  originalUnit?: string;
  notes?: string;
  sortOrder: number;
}

export interface CreateRecipeStep {
  stepNumber: number;
  instruction: string;
}
