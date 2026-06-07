import { Routes } from '@angular/router';

/**
 * Routes match the feature plans under docs/features/.
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'planner' },
  {
    path: 'planner',
    loadComponent: () => import('./pages/planner/week-planner.component').then((m) => m.WeekPlannerComponent),
  },
  {
    path: 'family',
    loadComponent: () => import('./pages/family/family.component').then((m) => m.FamilyComponent),
  },
  {
    path: 'recipes',
    loadComponent: () => import('./pages/recipes/recipes-list.component').then((m) => m.RecipesListComponent),
  },
  {
    path: 'recipes/import',
    loadComponent: () => import('./pages/recipe-import/recipe-import.component').then((m) => m.RecipeImportComponent),
  },
  {
    path: 'recipes/new',
    loadComponent: () => import('./pages/recipes/recipe-edit.component').then((m) => m.RecipeEditComponent),
  },
  {
    path: 'recipes/:id',
    loadComponent: () => import('./pages/recipes/recipe-edit.component').then((m) => m.RecipeEditComponent),
  },
  {
    path: 'shopping-list',
    loadComponent: () => import('./pages/shopping/shopping-list.component').then((m) => m.ShoppingListComponent),
  },
  {
    path: 'history',
    loadComponent: () => import('./pages/history/history.component').then((m) => m.HistoryComponent),
  },
  {
    path: 'settings',
    loadComponent: () => import('./pages/settings/settings.component').then((m) => m.SettingsComponent),
  },
  { path: '**', redirectTo: 'planner' },
];
