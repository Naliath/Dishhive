import { MealType } from '../api/generated/dishhive-api.client';
export { MealType };

import { Course } from '../api/generated/dishhive-api.client';
export { Course };

/** Serving order within a meal (starter before main before dessert) */
export const COURSE_ORDER: Record<Course, number> = {
  [Course.Appetizer]: 0,
  [Course.Main]: 1,
  [Course.Side]: 2,
  [Course.Dessert]: 3
};

import { EatenStatus } from '../api/generated/dishhive-api.client';
export { EatenStatus };

export type MealRating = import('../api/generated/dishhive-api.client').MealRatingDto;

export type PlannedMeal = import('../api/generated/dishhive-api.client').PlannedMealDto;

export type CreatePlannedMeal = Omit<import('../api/generated/dishhive-api.client').CreatePlannedMealDto, 'freezyItemQuantity'> & { freezyItemQuantity?: number };
