export enum MealType {
  Breakfast = 0,
  Lunch = 1,
  Dinner = 2,
  Snack = 3
}

export enum Course {
  Main = 0,
  Appetizer = 1,
  Side = 2,
  Dessert = 3
}

export const MEAL_TYPE_LABELS: Record<MealType, string> = {
  [MealType.Breakfast]: 'Breakfast',
  [MealType.Lunch]: 'Lunch',
  [MealType.Dinner]: 'Dinner',
  [MealType.Snack]: 'Snack'
};

export const COURSE_LABELS: Record<Course, string> = {
  [Course.Main]: 'Main',
  [Course.Appetizer]: 'Starter',
  [Course.Side]: 'Side',
  [Course.Dessert]: 'Dessert'
};

/** Serving order within a meal (starter before main before dessert) */
export const COURSE_ORDER: Record<Course, number> = {
  [Course.Appetizer]: 0,
  [Course.Main]: 1,
  [Course.Side]: 2,
  [Course.Dessert]: 3
};

export enum EatenStatus {
  Eaten = 0,
  Skipped = 1
}

export interface MealRating {
  familyMemberId: string;
  rating: number;
}

export interface PlannedMeal {
  id: string;
  /** ISO date (yyyy-MM-dd) */
  date: string;
  mealType: MealType;
  course: Course;
  recipeId?: string;
  recipeTitle?: string;
  dishName?: string;
  vagueInstruction?: string;
  freezyItemRef?: string;
  notes?: string;
  /** Whether the meal was actually cooked/eaten; null/undefined = not marked */
  eaten?: EatenStatus | null;
  attendeeIds: string[];
  ratings: MealRating[];
}

export interface CreatePlannedMeal {
  date: string;
  mealType: MealType;
  course: Course;
  recipeId?: string;
  dishName?: string;
  vagueInstruction?: string;
  freezyItemRef?: string;
  notes?: string;
  familyMemberIds: string[];
}
