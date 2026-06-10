export enum MealType {
  Breakfast = 0,
  Lunch = 1,
  Dinner = 2,
  Snack = 3
}

export interface PlannedMeal {
  id: string;
  /** ISO date (yyyy-MM-dd) */
  date: string;
  mealType: MealType;
  recipeId?: string;
  recipeTitle?: string;
  dishName?: string;
  vagueInstruction?: string;
  freezyItemRef?: string;
  notes?: string;
  attendeeIds: string[];
}

export interface CreatePlannedMeal {
  date: string;
  mealType: MealType;
  recipeId?: string;
  dishName?: string;
  vagueInstruction?: string;
  freezyItemRef?: string;
  notes?: string;
  familyMemberIds: string[];
}
