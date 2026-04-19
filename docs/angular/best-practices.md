# Angular Best Practices — Dishhive

**Created:** April 19, 2026

These guidelines align with Freezy's Angular conventions. When in doubt, consult Freezy's `FreezerInventory/docs/angular/best-practices.md`.

---

## Component Design

- Use **standalone components** — no NgModules
- Prefer **OnPush change detection** for performance-sensitive components
- Keep components focused on presentation; move business logic to services
- Use **signals** for component-local reactive state
- Use **RxJS observables** for async operations and streams

## File Naming

| Type | Convention | Example |
|------|-----------|---------|
| Component | `feature-name.component.ts` | `recipe-card.component.ts` |
| Page component | `feature-name.page.ts` | `week-planner.page.ts` |
| Service | `feature-name.service.ts` | `recipes.service.ts` |
| Model | `feature-name.model.ts` | `recipe.model.ts` |
| Guard | `feature-name.guard.ts` | `auth.guard.ts` |

## Routing

- All routes use **lazy loading** via `loadComponent`
- Route paths use **kebab-case**
- Default route (`''`) points to the main view (week planner)

```typescript
export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/week-planner/week-planner.page').then(m => m.WeekPlannerPage) },
  { path: 'recipes', loadComponent: () => import('./pages/recipes/recipes.page').then(m => m.RecipesPage) },
  { path: '**', redirectTo: '' }
];
```

## HTTP Services

- Inject `HttpClient` via constructor
- Return `Observable<T>` from service methods
- Use `catchError` for error handling
- Never subscribe inside services; let consumers subscribe

## SCSS / Styling

- Use `theme.scss` for Material Design 3 theme configuration
- Use CSS custom properties (`var(--mat-sys-*)`) for all colors
- No hardcoded hex or RGB values
- Prefer `gap` over margins for spacing in flex/grid layouts

## Material Components

- Import only the specific Material modules each component needs
- Use Material's built-in form field components for all inputs
- Use `mat-icon` with ligatures or registered SVG icons

## Testing

- Test framework: **Vitest** (via `@angular/build:unit-test`)
- Test files: `*.spec.ts` co-located with source
- Use `HttpClientTestingModule` for HTTP service tests
- Use **builder pattern** for test data (see `src/app/testing/test-builders.ts`)
- Run tests: `npm test`

## Imports

Order imports as:
1. Angular core (`@angular/core`, `@angular/common`, etc.)
2. Angular Material (`@angular/material/*`)
3. Third-party libraries
4. Application imports (services, models, components)
