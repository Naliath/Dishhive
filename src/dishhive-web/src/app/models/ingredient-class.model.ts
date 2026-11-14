/**
 * Canonical ingredient classes, mirroring the API's IngredientClass enum: the
 * EU-14 allergen list plus diet-relevant classes (meat split three ways, gelatin,
 * alcohol, honey). Recipes record which classes they CONTAIN as facts; family
 * members record which classes their tags EXCLUDE — "vegetarian" stays a
 * person-side policy, never a recipe-side verdict. Values travel as these exact
 * names in DTOs.
 */
export const INGREDIENT_CLASS_GROUPS: ReadonlyArray<{
  key: 'allergens' | 'meat' | 'other';
  classes: ReadonlyArray<string>;
}> = [
  {
    key: 'allergens',
    classes: [
      'Gluten', 'Crustaceans', 'Eggs', 'Fish', 'Peanuts', 'Soybeans', 'Milk',
      'TreeNuts', 'Celery', 'Mustard', 'Sesame', 'Sulphites', 'Lupin', 'Molluscs'
    ]
  },
  {
    key: 'meat',
    classes: ['RedMeat', 'Poultry', 'Pork']
  },
  {
    key: 'other',
    classes: ['Gelatin', 'Alcohol', 'Honey']
  }
];
