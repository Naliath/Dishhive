export interface WeekPlan {
  id: string;
  weekStartDate: string;  // ISO date: YYYY-MM-DD (always Monday)
  meals: PlannedMeal[];
  createdAt: string;
  updatedAt: string;
}

export interface PlannedMeal {
  id: string;
  dayOfWeek: DayOfWeek;
  mealType: MealType;
  recipeId?: string;
  recipeTitle?: string;
  vagueInstruction?: string;
  isFromFreezer: boolean;
  freezerItemId?: string;
  notes?: string;
  attendeeIds: string[];
  createdAt: string;
  updatedAt: string;
}

export type DayOfWeek = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday';
export type MealType = 'Breakfast' | 'Lunch' | 'Dinner' | 'Snack';

export const DAYS_OF_WEEK: DayOfWeek[] = [
  'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'
];

export interface UpsertPlannedMeal {
  dayOfWeek: DayOfWeek;
  mealType?: MealType;
  recipeId?: string;
  vagueInstruction?: string;
  isFromFreezer?: boolean;
  freezerItemId?: string;
  notes?: string;
  attendeeIds?: string[];
}
