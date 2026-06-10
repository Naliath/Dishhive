export interface ShoppingListItem {
  name: string;
  /** Metric quantity; display conversion happens in the frontend */
  quantity?: number;
  unit?: string;
  sourceRecipes: string[];
}

export interface ShoppingListReminder {
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
