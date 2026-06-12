# Dishhive Project Guidelines

Dishhive follows the same ecosystem conventions as Freezy
(`FreezerInventory/docs/PROJECT_GUIDELINES.md`). This document records Dishhive-specific
decisions; the shared Angular and .NET standards are mirrored in [angular/](angular) and
[dotnet/](dotnet).

## Architecture Decisions

- Single .NET container serves both API and Angular static files (Freezy pattern)
- PostgreSQL for persistence with Entity Framework Core; own database instance (port 5433),
  never shared with Freezy
- Angular Material 21 with Material Design 3 theming — Green/Yellow palettes
  (Freezy uses Azure/Cyan; distinct identity, same theming system)
- PWA following Freezy's service-worker setup: installable, offline **read** access to the
  week plan and shopping list; no offline write queue (see `features/pwa-support.md`;
  added later — the initial setup deliberately skipped it, see
  `plans/INFRASTRUCTURE_SETUP_PLAN.md` §2)
- Recipe imports go through pluggable `IRecipeSourceProvider` implementations
  (see `features/recipe-import.md`)
- Freezy integration only through `IFreezyClient` over Freezy's REST API; optional and
  read-only (see `features/freezy-integration.md`)
- AI-assisted planning goes exclusively through the `IMealSuggestionService` seam:
  LLM-backed (Microsoft.Extensions.AI, configurable provider) with a deterministic rules
  fallback when `Ai:Provider` is set, no-op otherwise — AI is never bolted on anywhere
  else (see `features/ai-week-planning.md`)
- Ingredient storage is always metric; original source values preserved verbatim; measurement
  preference is a display concern (see `features/measurement-preferences.md`)

## Domain Language

| Term | Meaning |
|------|---------|
| Family member | Person in the household; `IsGuest = true` for temporary attendees |
| Recipe | Stored cooking instructions with ingredients and metadata |
| Dish | A name for what is eaten; may exist without a recipe |
| Planned meal | A slot (date + meal type) on the week plan; recipe, dish name, or vague instruction |
| Vague instruction | Planning intention like "something with fish" or "leftovers" |

## Naming Conventions

### Frontend (Angular)
- Pages: folder under `src/app/pages/feature-name/`, files `feature-name.page.ts|html|scss`
- Services: `feature-name.service.ts` (e.g. `family-members.service.ts`)
- Models: `feature-name.model.ts`
- Shared components: folder under `src/app/components/`

### Backend (.NET)
- Controllers: `FeatureNameController.cs` (e.g. `FamilyMembersController.cs`)
- Models/Entities: `FeatureName.cs` (e.g. `FamilyMember.cs`)
- DTOs: `FeatureNameDtos.cs` with multiple classes per file (e.g. `RecipeDtos.cs`)
- Services: `IFeatureNameService.cs` + implementation; grouped in subfolders by concern
  (`Services/Import/`, `Services/Freezy/`, `Services/Suggestions/`)
- Database context: `DishhiveDbContext.cs`

### Database
- Tables: PascalCase singular (e.g. `FamilyMember`, `Recipe`, `PlannedMeal`)
- Columns: PascalCase; UTC timestamps `CreatedAt`/`UpdatedAt` maintained by the DbContext

## File Organization

### Backend Structure
```
Dishhive.Api/
├── Controllers/        # API endpoints
├── Models/             # Domain entities
│   └── DTOs/           # Data transfer objects
├── Data/               # DishhiveDbContext + Migrations/
├── Services/
│   ├── Import/         # Recipe import (providers, extractor, parser)
│   ├── Freezy/         # Freezy integration boundary
│   └── Suggestions/    # AI-assistance seam
├── Extensions/         # Startup helpers (database migration retry)
└── wwwroot/            # Static files (Angular build output, Docker only)
```

### Frontend Structure
```
src/app/
├── components/         # Shared components
├── models/             # TypeScript interfaces
├── pages/              # Feature pages (lazy routed)
│   ├── week-planner/
│   ├── family/
│   ├── recipes/
│   ├── recipe-detail/
│   └── settings/
└── services/           # Angular services
```

## When to Use What

Identical to Freezy:

- **Signals** for component-local reactive state; **RxJS** for API calls/streams
- **Services** for business logic and data fetching; components present UI only
- **OnPush** change detection on all components
- **EF Core** for all database access (never raw SQL); **DTOs** in/out (never entities)
- **Manual mapping**, no AutoMapper
- Migrations via `dotnet ef migrations add {name}`; applied automatically at startup

## API Conventions

- REST under `/api/*`: `GET/POST /api/{resource}`, `GET/PUT/DELETE /api/{resource}/{id}`
- Import endpoint: `POST /api/recipes/import { url }` → 201, 400 (unsupported source),
  422 (no recipe data on page)
- Standard status codes per `dotnet/api-standards.md`
- OpenAPI document at `/openapi/v1.json`, Scalar UI at `/scalar/v1`

## Testing

Follows Freezy's `TESTING_STRATEGY.md` approach:

- Backend: xUnit + FluentAssertions + NSubstitute; integration tests via
  `TestWebApplicationFactory` (EF InMemory, `Testing` environment)
- Recipe extraction: offline fixture tests against stored HTML
  (`Fixtures/dagelijkse-kost-recipe.html`) — never network-dependent
- Frontend: Vitest via `@angular/build:unit-test`
- Naming: `[Method/Feature]_[Scenario]_[ExpectedResult]`, AAA pattern
- Tests are contracts: when changing production code, fix the code, not the tests

## Feature Documentation Workflow

Every functional feature has a living document in `docs/features/` containing scope, domain
model, requirements, risks, a phased plan, and an implementation checklist using:

- `[ ]` = new
- `[~]` = in progress
- `[x]` = done

**Update the checklist in the same change that implements the work.** Future ideas go to
`possible-features.md` first and graduate into `docs/features/` when planned.
