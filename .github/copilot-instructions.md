# Freezer Inventory AI Agent Instructions

## Project Guidelines

### Code Style
- **.NET (C#):**
  - Use async/await for all database and API calls ([example](src/Dishhive.Api/Controllers/ItemsController.cs)).
  - Use FluentAssertions for test assertions ([example](src/Dishhive.Api.Tests/Controllers/)).
  - Naming: PascalCase for classes/methods, camelCase for variables, [see conventions](docs/PROJECT_GUIDELINES.md#naming-conventions).
- **Angular (TypeScript):**
  - 2-space indentation, single quotes ([.editorconfig](src/freezer-inventory-web/.editorconfig)).
  - Use OnPush change detection for components.
  - No `any` types; always define interfaces/models ([example](src/freezer-inventory-web/src/app/models/)).
  - Use builder pattern for test data ([example](src/freezer-inventory-web/src/app/testing/test-builders.ts)).

### Architecture
- **Single .NET API** serves Angular static files and REST endpoints ([docs/PROJECT_GUIDELINES.md](docs/PROJECT_GUIDELINES.md#architecture-decisions)).
- **PostgreSQL** via Entity Framework Core for persistence.
- **PWA**: Offline support, push notifications.
- **Docker Compose** for local/dev/prod parity.
- **Material Design** for UI ([docs/PROJECT_GUIDELINES.md#tech-stack](docs/PROJECT_GUIDELINES.md#tech-stack)).

### Build and Test
- **Frontend:**
  - `cd src/freezer-inventory-web && npm install && npm start` (dev)
  - `npm run test` (unit tests)
- **Backend:**
  - `cd src/Dishhive.Api && dotnet build`
  - `dotnet test src/Dishhive.Api.Tests` (unit/integration)
- **Full stack:**
  - `docker-compose up --build`
- **Pre-commit:** Runs both backend and frontend tests ([see hooks](docs/TESTING_STRATEGY.md#pre-commit-hooks)).

### Project Conventions
- **Test Data:** Use builder pattern for both backend ([Builders/](src/Dishhive.Api.Tests/Builders/)) and frontend ([test-builders.ts](src/freezer-inventory-web/src/app/testing/test-builders.ts)).
- **Test Naming:** `[Method/Feature]_[Scenario]_[ExpectedResult]` ([see](docs/TESTING_STRATEGY.md#naming-conventions)).
- **Test Pattern:** Arrange-Act-Assert (AAA) ([see](docs/TESTING_STRATEGY.md#aaa-pattern-arrange-act-assert)).
- **Coverage Targets:** 80%+ line coverage, 70%+ branch ([see](docs/TESTING_STRATEGY.md#test-coverage-targets)).
- **Error Handling:** Use global exception middleware, return consistent error format ([see](docs/dotnet/api-standards.md#error-handling)).
- **DTOs:** Use explicit DTOs for API boundaries ([see](docs/PROJECT_GUIDELINES.md#api-conventions)).

**Entity Framework Core Model Changes:**
- Always update the C# model classes first, then run `dotnet ef migrations add <migration-name>` to generate migration files.
- Never generate or edit raw SQL migration scripts directly.
- Implement proper indexes in migrations for query performance.
- Use async methods for all queries and updates (e.g., `ToListAsync`, `SaveChangesAsync`).
- Use `Include` for eager loading and avoid N+1 queries.
- Use `AsNoTracking` for read-only queries.
- Use transactions for multi-step operations.

### Integration Points
- **Database:** PostgreSQL, migrations via EF Core.
- **Service boundaries:** API controllers in `Controllers/`, services in `Services/`.

### Security
- **Input validation:** DataAnnotations for .NET, Angular forms for frontend.
- **SQL injection:** Use parameterized queries (EF Core default).
- **XSS/CSRF:** Prevent via Angular sanitization and .NET anti-forgery.
- **JWT:** Secure configuration for authentication (if enabled).
- **Do not log sensitive data.**

---

For more details, see [docs/PROJECT_GUIDELINES.md](docs/PROJECT_GUIDELINES.md), [docs/TESTING_STRATEGY.md](docs/TESTING_STRATEGY.md), and [docs/dotnet/api-standards.md](docs/dotnet/api-standards.md).
