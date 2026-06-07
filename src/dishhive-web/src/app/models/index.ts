export type PreferenceKind = 'Allergy' | 'Dislike' | 'Like' | 'Diet';
export type IntentTag = 'None' | 'Meat' | 'Vegetarian' | 'Fish' | 'Pasta' | 'Soup' | 'Salad' | 'Other';
export type MealType = 'Breakfast' | 'Lunch' | 'Dinner';
export type DayOfWeek = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday';
export type MeasurementSystem = 'Metric' | 'Imperial';

export interface FamilyMember {
  id: string;
  displayName: string;
  notes?: string | null;
  createdAt: string;
  updatedAt: string;
  preferences: FamilyMemberPreference[];
}

export interface FamilyMemberPreference {
  id: string;
  familyMemberId: string;
  kind: PreferenceKind;
  value: string;
  recipeId?: string | null;
}

export interface CreateFamilyMember {
  displayName: string;
  notes?: string | null;
}

export interface CreatePreference {
  kind: PreferenceKind;
  value: string;
  recipeId?: string | null;
}

export interface Guest {
  id: string;
  displayName: string;
  notes?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateGuest {
  displayName: string;
  notes?: string | null;
}

export interface Recipe {
  id: string;
  title: string;
  description?: string | null;
  servings: number;
  imageUrl?: string | null;
  videoUrl?: string | null;
  sourceUrl?: string | null;
  sourceProviderKey?: string | null;
  notes?: string | null;
  createdAt: string;
  updatedAt: string;
  ingredients: RecipeIngredient[];
  steps: RecipeStep[];
  tags: string[];
}

export interface RecipeSummary {
  id: string;
  title: string;
  description?: string | null;
  servings: number;
  imageUrl?: string | null;
  tags: string[];
}

export interface RecipeIngredient {
  id: string;
  order: number;
  name: string;
  quantity?: number | null;
  unit?: string | null;
  originalQuantity?: number | null;
  originalUnit?: string | null;
  section?: string | null;
  note?: string | null;
}

export interface RecipeStep {
  id: string;
  order: number;
  text: string;
}

export interface CreateRecipe {
  title: string;
  description?: string | null;
  servings: number;
  imageUrl?: string | null;
  videoUrl?: string | null;
  sourceUrl?: string | null;
  sourceProviderKey?: string | null;
  sourceRawPayload?: string | null;
  notes?: string | null;
  ingredients: CreateRecipeIngredient[];
  steps: CreateRecipeStep[];
  tags: string[];
}

export interface CreateRecipeIngredient {
  order: number;
  name: string;
  quantity?: number | null;
  unit?: string | null;
  originalQuantity?: number | null;
  originalUnit?: string | null;
  section?: string | null;
  note?: string | null;
}

export interface CreateRecipeStep {
  order: number;
  text: string;
}

export interface WeekPlan {
  id: string;
  weekStart: string; // yyyy-MM-dd
  notes?: string | null;
  createdAt: string;
  updatedAt: string;
  slots: MealSlot[];
}

export interface MealSlot {
  id: string;
  weekPlanId: string;
  dayOfWeek: DayOfWeek;
  mealType: MealType;
  recipeId?: string | null;
  recipeTitle?: string | null;
  vagueIntent?: string | null;
  intentTag: IntentTag;
  frozenItemRef?: string | null;
  notes?: string | null;
  attendees: MealSlotAttendee[];
}

export interface MealSlotAttendee {
  id: string;
  familyMemberId?: string | null;
  guestId?: string | null;
}

export interface UpdateMealSlot {
  recipeId?: string | null;
  vagueIntent?: string | null;
  intentTag?: IntentTag;
  frozenItemRef?: string | null;
  notes?: string | null;
}

export interface UpdateAttendees {
  familyMemberIds: string[];
  guestIds: string[];
}

export interface ShoppingList {
  id: string;
  weekPlanId?: string | null;
  title: string;
  createdAt: string;
  updatedAt: string;
  items: ShoppingListItem[];
}

export interface ShoppingListItem {
  id: string;
  order: number;
  name: string;
  quantity?: number | null;
  unit?: string | null;
  section?: string | null;
  checked: boolean;
  note?: string | null;
}

export interface DishHistoryEntry {
  id: string;
  date: string;
  mealType: MealType;
  recipeId?: string | null;
  dishLabel: string;
  createdAt: string;
}

export interface DishFavorite {
  id: string;
  familyMemberId: string;
  recipeId?: string | null;
  dishLabel: string;
}

export interface DishFrequency {
  dishLabel: string;
  recipeId?: string | null;
  timesPlanned: number;
  lastPlanned: string;
}

export interface AppSettings {
  measurementSystem: MeasurementSystem;
  freezyEnabled: boolean;
}

export interface ImportedRecipe {
  title: string;
  description?: string | null;
  servings: number;
  imageUrl?: string | null;
  videoUrl?: string | null;
  sourceUrl: string;
  providerKey: string;
  sourceRawPayload: string;
  ingredients: ImportedIngredient[];
  steps: ImportedStep[];
  tags: string[];
}

export interface ImportedIngredient {
  order: number;
  name: string;
  quantity?: number | null;
  unit?: string | null;
  originalQuantity?: number | null;
  originalUnit?: string | null;
  section?: string | null;
  note?: string | null;
}

export interface ImportedStep {
  order: number;
  text: string;
}
