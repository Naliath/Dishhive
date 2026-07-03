/**
 * Canonical ingredient classes, mirroring the API's IngredientClass enum: the
 * EU-14 allergen list plus diet-relevant classes (meat split three ways, gelatin,
 * alcohol, honey). Recipes record which classes they CONTAIN as facts; family
 * members record which classes their tags EXCLUDE — "vegetarian" stays a
 * person-side policy, never a recipe-side verdict. Values travel as these exact
 * names in DTOs.
 */
export const INGREDIENT_CLASS_GROUPS: ReadonlyArray<{
  label: string;
  classes: ReadonlyArray<string>;
}> = [
  {
    label: 'Allergens (EU-14)',
    classes: [
      'Gluten', 'Crustaceans', 'Eggs', 'Fish', 'Peanuts', 'Soybeans', 'Milk',
      'TreeNuts', 'Celery', 'Mustard', 'Sesame', 'Sulphites', 'Lupin', 'Molluscs'
    ]
  },
  {
    label: 'Meat',
    classes: ['RedMeat', 'Poultry', 'Pork']
  },
  {
    label: 'Other',
    classes: ['Gelatin', 'Alcohol', 'Honey']
  }
];

const LABELS: Record<string, string> = {
  Gluten: 'Gluten',
  Crustaceans: 'Crustaceans',
  Eggs: 'Eggs',
  Fish: 'Fish',
  Peanuts: 'Peanuts',
  Soybeans: 'Soy',
  Milk: 'Milk',
  TreeNuts: 'Tree nuts',
  Celery: 'Celery',
  Mustard: 'Mustard',
  Sesame: 'Sesame',
  Sulphites: 'Sulphites',
  Lupin: 'Lupin',
  Molluscs: 'Molluscs',
  RedMeat: 'Red meat',
  Poultry: 'Poultry',
  Pork: 'Pork',
  Gelatin: 'Gelatin',
  Alcohol: 'Alcohol',
  Honey: 'Honey'
};

/** Display label for a class name; falls back to the raw name */
export function ingredientClassLabel(name: string): string {
  return LABELS[name] ?? name;
}

/** Short comma-joined label list, e.g. for tooltips and chips */
export function ingredientClassLabels(names: string[]): string {
  return names.map(ingredientClassLabel).join(', ');
}
