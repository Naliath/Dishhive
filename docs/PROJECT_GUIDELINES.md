# Dishhive Project Guidelines

> Sibling project to Freezy (FreezerInventory). Conventions deliberately mirror that project so both share a common operational and architectural style.

## Architecture Decisions
- Single .NET container serves both API (`/api/*`) and the Angular SPA static files.
- PostgreSQL 16 for persistence, accessed via Entity Framework Core 10.
- Angular 21 standalone components with Angular Material 21.1.x for UI.
- Docker Compose for local development and deployment.
- No multi-user/auth in v1 — the application is a single-household app. A pluggable seam exists in the data model (a `HouseholdId` defaulting to a system value) so a future multi-user mode can be added the same way Freezy plans to.
- Pluggable provider pattern for any external integration that is likely to grow: recipe sources (`IRecipeSourceProvider`), measurement-unit conversion, and Freezy integration.
- AI-assisted planning is **not** implemented — only extension points (`IMealSuggestionStrategy`) exist.

## Tech Stack
- **Frontend**: Angular 21 standalone, Angular Material 21.1.x, RxJS, signals
- **Backend**: .NET 10 Web API, EF Core 10, Npgsql 10, Swashbuckle
- **Database**: PostgreSQL 16
- **Containerization**: Docker & Docker Compose

## Naming Conventions

### Frontend (Angular)
- Components: `feature-name.component.ts`
- Services: `feature-name.service.ts`
- Models: `feature-name.model.ts`
- Pages under `src/app/pages/<feature>/`
- Shared components under `src/app/components/<name>/`

### Backend (.NET)
- Controllers: `<Feature>Controller.cs` (e.g. `RecipesController`, `WeekPlansController`)
- Models/Entities: `<Entity>.cs` (singular, e.g. `Recipe`, `MealSlot`)
- DTOs: `<Feature>Dtos.cs` containing nested DTO records/classes
- Services: `I<Feature>Service.cs` + `<Feature>Service.cs`
- DbContext: `DishhiveDbContext.cs`
- Provider plug-ins: `I<Capability>Provider.cs` + concrete implementations under `Services/<Capability>/`

### Database
- Tables: PascalCase singular (`Recipe`, `MealSlot`)
- Columns: PascalCase
- Foreign keys: EF Core conventions (`<Nav>Id`)

## File Organization

### Backend
```
src/Dishhive.Api/
├── Controllers/
├── Data/
│   └── DishhiveDbContext.cs
├── Extensions/
│   └── DatabaseExtensions.cs
├── Models/
│   ├── Family/
│   ├── Planning/
│   ├── Recipes/
│   ├── Shopping/
│   ├── History/
│   ├── Settings/
│   └── DTOs/
├── Services/
│   ├── RecipeImport/
│   ├── Units/
│   └── FreezyIntegration/
├── Program.cs
├── AppVersion.cs
└── appsettings.json
```

### Frontend
```
src/dishhive-web/src/app/
├── components/
├── models/
├── pages/
│   ├── week-planner/
│   ├── family/
│   ├── recipes/
│   ├── recipe-import/
│   ├── shopping-list/
│   ├── history/
│   └── settings/
├── services/
├── environments/
└── app.{ts,html,scss,routes.ts,config.ts}
```

## API Conventions

- Routing: `[Route("api/[controller]")]`, `[ApiController]`, `[Produces("application/json")]`
- Versioning lives in `AppVersion.cs` (mirrors Freezy)
- Status codes:
  - `200` for GET / successful POST returning data
  - `201 Created` for POSTs that produce a new resource (with `CreatedAtAction`)
  - `204` for PUT/DELETE that don't return a body
  - `400` on validation, `404` on missing resources

## Domain Modules

| Module | Primary entities |
|---|---|
| Family | `FamilyMember`, `FamilyMemberPreference`, `Guest` |
| Planning | `WeekPlan`, `MealSlot`, `MealSlotAttendee` |
| Recipes | `Recipe`, `RecipeIngredient`, `RecipeStep`, `RecipeTag`, `RecipeMedia` |
| History | `DishHistoryEntry`, `DishFavorite` |
| Shopping | `ShoppingList`, `ShoppingListItem` |
| Settings | `UserSetting` |
| Integration | Freezy frozen-item references via `FrozenItemReference` (lightweight DTO + adapter) |

## Styling
- Angular Material first; never hardcode colors — only use Material design tokens (`var(--mat-sys-*)`).
- One `theme.scss` for the Material theme, `styles.scss` for global utilities.
- Page-specific styles stay in their page folder.

## Extension Seams
- `IRecipeSourceProvider` — recipe import sources (DagelijkseKost, future sites).
- `IMealSuggestionStrategy` — placeholder seam for future AI suggestions; current implementation just returns `null` (manual planning).
- `IFreezyClient` — abstraction for Freezy integration; default implementation calls the Freezy HTTP API. Tests can substitute a stub.
- `IUnitConversionService` — metric/imperial conversion with original-value preservation.

## Code Review Checklist
- [ ] Follows naming conventions
- [ ] DTOs used for all request/response payloads (entities never exposed)
- [ ] Async EF queries throughout (no blocking)
- [ ] No `any` in TypeScript; all observables/signals strongly typed
- [ ] Material components used for all UI
- [ ] Migrations created for schema changes (`dotnet ef migrations add ...`)
- [ ] Feature checklist updated under `docs/features/`
