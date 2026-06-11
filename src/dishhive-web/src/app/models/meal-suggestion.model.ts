export interface MealSuggestion {
  /** ISO date (yyyy-MM-dd) */
  date: string;
  recipeId?: string;
  recipeTitle?: string;
  dishName: string;
  reason?: string;
}

export interface MealSuggestions {
  enabled: boolean;
  suggestions: MealSuggestion[];
}

export interface SuggestionStatus {
  enabled: boolean;
}
