export interface ShoppingListItem {
  name: string;
  /** Metric quantity; display conversion happens in the frontend */
  quantity?: number;
  unit?: string;
  sourceRecipes: string[];
}

export interface ShoppingListReminder {
  /** The underlying planned meal, so a recipe can be attached to it */
  plannedMealId: string;
  /** ISO date (yyyy-MM-dd) */
  date: string;
  text: string;
}

export interface ShoppingList {
  from: string;
  to: string;
  items: ShoppingListItem[];
  reminders: ShoppingListReminder[];
}
