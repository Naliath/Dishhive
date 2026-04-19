import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/week-planner/week-planner.page').then(m => m.WeekPlannerPage)
  },
  {
    path: 'family',
    loadComponent: () => import('./pages/family/family.page').then(m => m.FamilyPage)
  },
  {
    path: 'family/:id',
    loadComponent: () => import('./pages/family/family-member-detail.page').then(m => m.FamilyMemberDetailPage)
  },
  {
    path: 'recipes',
    loadComponent: () => import('./pages/recipes/recipes.page').then(m => m.RecipesPage)
  },
  {
    path: 'recipes/new',
    loadComponent: () => import('./pages/recipe-edit/recipe-edit.page').then(m => m.RecipeEditPage)
  },
  {
    path: 'recipes/:id/edit',
    loadComponent: () => import('./pages/recipe-edit/recipe-edit.page').then(m => m.RecipeEditPage)
  },
  {
    path: 'recipes/:id',
    loadComponent: () => import('./pages/recipe-detail/recipe-detail.page').then(m => m.RecipeDetailPage)
  },
  {
    path: 'shopping-list',
    loadComponent: () => import('./pages/shopping-list/shopping-list.page').then(m => m.ShoppingListPage)
  },
  {
    path: 'statistics',
    loadComponent: () => import('./pages/statistics/statistics.page').then(m => m.StatisticsPage)
  },
  {
    path: 'settings',
    loadComponent: () => import('./pages/settings/settings.page').then(m => m.SettingsPage)
  },
  {
    path: '**',
    redirectTo: ''
  }
];

