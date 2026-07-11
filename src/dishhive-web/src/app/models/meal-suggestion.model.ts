export interface MealSuggestion {
  id: string;
  /** ISO date (yyyy-MM-dd) */
  date: string;
  recipeId?: string;
  recipeTitle?: string;
  dishName: string;
  reason?: string;
  /** Filled by the deterministic fallback rather than the LLM */
  fromFallback?: boolean;
  /** Warning that the linked recipe conflicts with a household allergy (exact facts
   *  match on assessed recipes, ingredient-substring heuristic otherwise) */
  allergyWarning?: string;
  /** Warning that the linked recipe's facts conflict with an attendee's diet tag */
  dietWarning?: string;
  /** Freezy item id when this dish comes from the freezer (reserves stock once accepted) */
  freezyItemRef?: string;
  /** Units of the freezer item this dish reserves */
  freezyItemQuantity?: number;
  /** URL of an external recipe the AI found; imported from here when the suggestion is accepted */
  sourceUrl?: string;
  /** Friendly source name for an external suggestion (e.g. "Dagelijkse Kost") */
  sourceName?: string;
}

export interface MealSuggestions {
  batchId: string;
  enabled: boolean;
  suggestions: MealSuggestion[];
  /** Known recipes kept out of planning because their facts conflict with a household allergy */
  excludedForAllergies?: number;
}

export interface SuggestionReviewResult {
  batchId: string;
  suggestions: MealSuggestion[];
}

export type AcceptSuggestionStatus =
  | 'created'
  | 'alreadyApplied'
  | 'importFailed'
  | 'planningFailed'
  | 'stockUnavailable';

export interface AcceptSuggestionResult {
  suggestionId: string;
  status: AcceptSuggestionStatus;
  dishName: string;
  plannedMealId?: string;
  recipeId?: string;
  error?: string;
}

export interface AcceptSuggestionsResponse {
  results: AcceptSuggestionResult[];
}

export interface SuggestionStatus {
  enabled: boolean;
}
