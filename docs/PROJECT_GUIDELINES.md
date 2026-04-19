# Dishhive Project Guidelines

**Created:** April 19, 2026

## Architecture Decisions
- Single .NET container serves both API and Angular static files
- PostgreSQL for persistence with Entity Framework Core
- Angular PWA for responsive household use (mobile + desktop)
- Material Design 3 (Angular Material 21) for UI consistency
- Docker Compose for development and deployment
- Extensible service architecture for recipe sources and future AI planning

## Styling Architecture

### SCSS File Organization
```
src/
├── theme.scss              # Material Design 3 theme configuration
├── styles.scss             # Global styles and utility classes
└── app/
    ├── app.scss            # Minimal app-level styles
    └── pages/
        └── */*.scss        # Page-specific styles only
```

### Styling Principles
1. **Material First**: Use Material components and their built-in styling
2. **Theme Variables Only**: Never use hardcoded colors (#hex, rgb())
3. **Centralize Common Patterns**: Loading states, empty states in `styles.scss`
4. **Modern CSS**: Use `gap`, logical properties, `clamp()`, responsive grids
5. **Minimal Overrides**: Only use overrides when strictly necessary

### Color Palette
- **Light Theme**: Warm green/teal palette (food & kitchen feel)
  - Primary: Teal/green shades
  - Surface: Light warm backgrounds
  - Tertiary: Amber/orange for highlights

- **Dark Theme**: Deep teal/slate
  - Primary: Teal shades
  - Surface: Dark slate backgrounds
  - Colors automatically adapt via CSS custom properties

### Common Patterns
```scss
// Container max-width
.page-container {
  max-width: 600px;      // Forms
  max-width: 800px;      // Lists
  max-width: 1200px;     // Grids / week planner
  margin-inline: auto;
}

// Responsive grid (no media queries)
.recipe-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(280px, 100%), 1fr));
  gap: 16px;
}

// Week planner grid
.week-grid {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  gap: 8px;
}
```

### Material CSS Variables Reference
```scss
// Surfaces
var(--mat-sys-surface)                 // Base surface
var(--mat-sys-surface-container)       // Card backgrounds
var(--mat-sys-surface-bright)          // Elevated surfaces

// Text colors
var(--mat-sys-on-surface)             // Primary text
var(--mat-sys-on-surface-variant)     // Secondary text

// Status colors
var(--mat-sys-primary)                // Primary actions
var(--mat-sys-error)                  // Errors
var(--mat-sys-tertiary)               // Highlights

// Borders & outlines
var(--mat-sys-outline)                // Standard borders
var(--mat-sys-outline-variant)        // Subtle borders
```

## Tech Stack
- **Frontend**: Angular 21 standalone components
- **Backend**: .NET 10 Web API
- **Database**: PostgreSQL 16
- **UI Framework**: Angular Material 21
- **Containerization**: Docker & Docker Compose

## Naming Conventions

### Frontend (Angular)
- Components: `feature-name.component.ts` (e.g., `recipe-card.component.ts`)
- Services: `feature-name.service.ts` (e.g., `recipes.service.ts`)
- Models: `feature-name.model.ts` (e.g., `recipe.model.ts`)
- Pages: Folder under `src/app/pages/feature-name/`
- Shared components: Folder under `src/app/components/`

### Backend (.NET)
- Controllers: `FeatureNameController.cs` (e.g., `RecipesController.cs`)
- Models/Entities: `FeatureName.cs` (e.g., `Recipe.cs`)
- DTOs: `FeatureNameDtos.cs` with nested classes (e.g., `RecipeDtos.cs`)
- Services: `IFeatureNameService.cs` and `FeatureNameService.cs`
- Database context: `DishhiveDbContext.cs`

### Database
- Tables: PascalCase singular (e.g., `Recipe`, `FamilyMember`)
- Columns: PascalCase (e.g., `Title`, `CreatedAt`)
- Foreign keys: Follow EF Core conventions

## File Organization

### Frontend Structure
```
src/app/
├── components/          # Shared components
│   ├── offline-indicator/
│   └── install-prompt/
├── models/             # TypeScript interfaces
│   ├── recipe.model.ts
│   ├── family-member.model.ts
│   └── week-plan.model.ts
├── pages/              # Feature pages (routed)
│   ├── week-planner/
│   ├── family/
│   ├── recipes/
│   └── settings/
├── services/           # Angular services
│   ├── recipes.service.ts
│   ├── family.service.ts
│   └── week-planner.service.ts
├── testing/            # Test utilities
│   ├── test-utils.ts
│   └── test-builders.ts
└── environments/       # Environment configs
```

### Backend Structure
```
Dishhive.Api/
├── Controllers/        # API endpoints
│   ├── RecipesController.cs
│   ├── FamilyController.cs
│   └── WeekPlannerController.cs
├── Models/            # Domain entities
│   ├── Recipe.cs
│   ├── FamilyMember.cs
│   └── DTOs/          # Data transfer objects
├── Data/              # Database context
│   ├── DishhiveDbContext.cs
│   └── Migrations/    # EF migrations
├── Services/          # Business logic
│   ├── IRecipeImportService.cs
│   ├── IRecipeSourceProvider.cs
│   └── Sources/
│       └── DagelijksekostSourceProvider.cs
└── wwwroot/           # Static files (Angular build output)
```

## When to Use What

### Frontend
- **Signals**: Component-local reactive state (loading flags, UI state)
- **RxJS Observables**: API calls, async operations, streams
- **Services**: Business logic, data fetching, state management
- **Components**: UI presentation only, minimal logic
- **Material Components**: All UI elements for consistency

### Backend
- **Controllers**: HTTP endpoint handlers, routing, validation
- **Services**: Business logic, external API/scraping calls
- **Entity Framework**: All database access (never raw SQL)
- **DTOs**: Request/response models (never expose entities)
- **Migrations**: All database schema changes

## API Conventions

### Endpoints
- `GET /api/recipes` - List recipes
- `GET /api/recipes/{id}` - Get recipe
- `POST /api/recipes` - Create recipe
- `PUT /api/recipes/{id}` - Update recipe
- `DELETE /api/recipes/{id}` - Delete recipe
- `POST /api/recipes/import` - Import from external source

### Response Codes
- `200 OK` - Successful GET
- `201 Created` - Successful POST
- `204 No Content` - Successful PUT/DELETE
- `400 Bad Request` - Validation error
- `404 Not Found` - Resource not found

## Recipe Source Provider Architecture

Recipe import is designed around a pluggable provider pattern:

```csharp
public interface IRecipeSourceProvider
{
    string SourceName { get; }
    bool CanHandle(string url);
    Task<ImportedRecipeDto?> ImportFromUrlAsync(string url);
}
```

This allows new recipe sources to be added without modifying existing code. Register providers via DI:

```csharp
services.AddScoped<IRecipeSourceProvider, DagelijksekostSourceProvider>();
// Add more providers here as needed
```

The `RecipeImportService` selects the correct provider by calling `CanHandle(url)` on each registered provider.

## Freezy Integration Architecture

Dishhive reads frozen item data from Freezy via HTTP API. The integration is:
- One-directional (Dishhive reads Freezy, never writes)
- Isolated behind `IFreezyIntegrationService`
- Configurable via `FreezyIntegration:BaseUrl` in `appsettings.json`
- Gracefully disabled when `FreezyIntegration:Enabled = false`

## AI Planning Extension Points

The week planner architecture includes extension points for future AI assistance:
- `IWeekPlanSuggestionProvider` interface (not yet implemented)
- Suggestion input model captures: family composition, preferences, history, available ingredients
- The week planner UI has a "Suggest" action that can call this service when implemented

Do not implement AI logic now. Only keep the seam clean.
