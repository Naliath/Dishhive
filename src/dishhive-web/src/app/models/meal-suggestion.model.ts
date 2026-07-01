export interface MealSuggestion {
  /** ISO date (yyyy-MM-dd) */
  date: string;
  recipeId?: string;
  recipeTitle?: string;
  dishName: string;
  reason?: string;
  /** Filled by the deterministic fallback rather than the LLM */
  fromFallback?: boolean;
  /** Heuristic warning that the linked recipe may conflict with a household allergy */
  allergyWarning?: string;
  /** Freezy item id when this dish comes from the freezer (reserves stock once accepted) */
  freezyItemRef?: string;
  /** Units of the freezer item this dish reserves */
  freezyItemQuantity?: number;
}

export interface MealSuggestions {
  enabled: boolean;
  suggestions: MealSuggestion[];
}

export interface SuggestionStatus {
  enabled: boolean;
}
