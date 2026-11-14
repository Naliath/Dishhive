import { Course, MealType } from './planned-meal.model';

export type MealSuggestion = import('../api/generated/dishhive-api.client').MealSuggestionDto;

export type MealSuggestions = import('../api/generated/dishhive-api.client').MealSuggestionsDto;

export interface SuggestionReviewResult {
  applied?: boolean;
  batchId: string;
  suggestions: MealSuggestion[];
  rejectedSuggestionIds: string[];
}

export type AcceptSuggestionStatus =
  | 'created'
  | 'alreadyApplied'
  | 'importFailed'
  | 'planningFailed'
  | 'stockUnavailable'
  | 'needsReview';

export type AcceptSuggestionResult = import('../api/generated/dishhive-api.client').AcceptMealSuggestionResultDto;

export type AcceptSuggestionsResponse = import('../api/generated/dishhive-api.client').AcceptMealSuggestionsResponseDto;

export type SuggestionStatus = import('../api/generated/dishhive-api.client').SuggestionStatusDto;
