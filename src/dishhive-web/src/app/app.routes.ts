import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/home/home.component').then(m => m.HomeComponent),
  },
  {
    path: 'recipes',
    loadComponent: () => import('./pages/recipes/recipes.component').then(m => m.RecipesComponent),
  },
  {
    path: 'planner',
    loadComponent: () => import('./pages/planner/planner.component').then(m => m.PlannerComponent),
  },
  {
    path: 'family',
    loadComponent: () => import('./pages/family/family.component').then(m => m.FamilyComponent),
  },
  {
    path: 'shopping-list',
    loadComponent: () => import('./pages/shopping-list/shopping-list.component').then(m => m.ShoppingListComponent),
  },
  {
    path: 'settings',
    loadComponent: () => import('./pages/settings/settings.component').then(m => m.SettingsComponent),
  },
  {
    path: '**',
    redirectTo: '',
  },
];
