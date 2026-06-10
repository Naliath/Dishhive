# Dishhive — Infrastructure & Setup Plan

> **Status**: Planning
> **Last Updated**: 2026-06-07
> **Version**: 1.0

## 1. Overview

Dishhive is a household meal planning application designed for family week menu planning, recipe management, frozen meal integration with Freezy, and shopping list generation.

This document defines the technical infrastructure, repository structure, and development environment setup for the Dishhive application.

## 2. Design Principles

- **Consistency with Freezy**: Reuse patterns, conventions, and architectural decisions from the FreezerInventory application where applicable.
- **Isolation**: Dishhive remains a logically separate application. No modifications to Freezy unless strictly necessary for shared ecosystem support.
- **Extensibility**: Clean seams for recipe source integrations, Freezy integration, and future AI-assisted planning.
- **Incremental delivery**: Foundation first, then vertical feature slices.

## 3. Technology Stack

| Layer | Technology | Version | Rationale |
|-------|-----------|---------|-----------|
| Frontend | Angular + Angular Material | 21+ | Consistent with Freezy |
| Backend | .NET | 10 | Consistent with Freezy |
| Database | PostgreSQL | 17 | Consistent with Freezy, mature EF Core support |
| ORM | Entity Framework Core | 10 | Consistent with Freezy |
| Containerization | Docker Compose | - | Consistent with Freezy |
| Reverse Proxy | Nginx | - | Consistent with Freezy |
| Package Manager | npm | - | Consistent with Freezy |

## 4. Repository & Folder Structure

```
Dishhive/
├── .dockerignore
├── .gitignore
├── docker-compose.yml
├── dotnet-tools.json
├── Dishhive.sln
├── LICENSE
├── README.md
├── docker/
│   └── postgres/
│       └── init-dishhive.sql
├── docs/
│   ├── README.md
│   ├── architecture.md
│   ├── infrastructure-setup-plan.md
│   ├── future-features.md
│   └── features/
│       ├── family-composition.md
│       ├── week-planner.md
│       ├── past-dishes-statistics.md
│       ├── freezy-integration.md
│       ├── recipe-store.md
│       ├── recipe-import.md
│       ├── measurement-preferences.md
│       └── shopping-list-export.md
└── src/
    ├── Dishhive.Api/
    │   ├── Dishhive.Api.csproj
    │   ├── Program.cs
    │   ├── appsettings.json
    │   ├── appsettings.Development.json
    │   ├── Data/
    │   │   └── DishhiveDbContext.cs
    │   ├── Models/
    │   │   ├── (domain entities)
    │   │   └── DTOs/
    │   ├── Controllers/
    │   ├── Extensions/
    │   ├── Services/
    │   └── Migrations/
    ├── Dishhive.Web/
    │   ├── angular.json
    │   ├── package.json
    │   ├── tsconfig.json
    │   ├── Dockerfile
    │   ├── nginx.conf
    │   ├── src/
    │   │   ├── app/
    │   │   │   ├── app.component.*
    │   │   │   ├── app.config.ts
    │   │   │   ├── app.routes.ts
    │   │   │   ├── core/
    │   │   │   ├── features/
    │   │   │   └── shared/
    │   │   ├── assets/
    │   │   ├── environments/
    │   │   ├── index.html
    │   │   └── main.ts
    │   └── proxy.conf.js
    └── Dishhive.Tests/
        ├── Dishhive.Tests.csproj
        ├── Unit/
        └── Integration/
```

### Alignment with Freezy

| Convention | Freezy | Dishhive |
|-----------|--------|----------|
| Solution file | `FreezerInventory.sln` | `Dishhive.sln` |
| API project | `FreezerInventory.Api` | `Dishhive.Api` |
| Web project | `FreezerInventory.Web` | `Dishhive.Web` |
| Test project | `FreezerInventory.Tests` | `Dishhive.Tests` |
| DbContext | `FreezerDbContext` | `DishhiveDbContext` |
| Docs folder | `FreezerInventory/docs/` | `Dishhive/docs/` |
| Feature docs | `docs/features/` | `docs/features/` |
| Docker folder | `docker/postgres/` | `docker/postgres/` |

## 5. Port Allocation

To avoid conflicts with FreezerInventory, Dishhive uses a distinct port range:

| Service | Freezy Port | Dishhive Port |
|---------|-------------|---------------|
| API (development) | 5050 | **5051** |
| Frontend dev server | 4200 | **4201** |
| Nginx (production) | 80 | **81** |
| PostgreSQL | 5432 | **5433** |

### Environment Variables

```yaml
# docker-compose.yml
services:
  api:
    ports:
      - "5051:8080"
  web:
    ports:
      - "81:80"
  postgres:
    ports:
      - "5433:5432"
```

## 6. Database Configuration

### Connection String

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5433;Database=dishhive;Username=dishhive;Password=dishhive_dev_password"
  }
}
```

### Database Initialization

Following Freezy conventions, an init SQL script will create the database and user:

```sql
-- docker/postgres/init-dishhive.sql
CREATE USER dishhive WITH PASSWORD 'dishhive_dev_password';
CREATE DATABASE dishhive OWNER dishhive;
GRANT ALL PRIVILEGES ON DATABASE dishhive TO dishhive;
```

### EF Core Strategy

- Same migration approach as Freezy
- UUID primary keys with `gen_random_uuid()` default
- Timestamp columns with automatic update on save
- Database retry logic in startup extensions

## 7. Docker Setup

### docker-compose.yml Structure

Aligned with Freezy conventions:

```yaml
services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5433:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./docker/postgres:/docker-entrypoint-initdb.d

  api:
    build:
      context: .
      dockerfile: src/Dishhive.Api/Dockerfile
    ports:
      - "5051:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=dishhive;Username=dishhive;Password=dishhive_dev_password
    depends_on:
      - postgres

  web:
    build:
      context: .
      dockerfile: src/Dishhive.Web/Dockerfile
    ports:
      - "81:80"
    depends_on:
      - api

volumes:
  postgres_data:
```

### Dockerfiles

- `src/Dishhive.Api/Dockerfile`: Multi-stage .NET build (aligned with Freezy)
- `src/Dishhive.Web/Dockerfile`: Multi-stage Angular + Nginx build (aligned with Freezy)

## 8. Frontend Setup

### Angular Configuration

- Angular 21+ with standalone components
- Angular Material for UI components
- Standalone `provideRouter`, `provideHttpClient`, `provideAnimations`
- Environment-based API URL configuration

### Proxy Configuration

Development proxy (`proxy.conf.js`) to route API calls:

```javascript
const PROXY_CONFIG = [
  {
    context: '/api',
    target: 'http://localhost:5051',
    secure: false,
  },
];
module.exports = PROXY_CONFIG;
```

### App Structure

```
src/app/
├── app.component.*          # Root component
├── app.config.ts            # Application configuration
├── app.routes.ts            # Route definitions
├── core/                    # Core services, interceptors, guards
│   ├── services/
│   ├── interceptors/
│   └── models/
├── features/                # Feature modules
│   ├── family/
│   ├── planner/
│   ├── recipes/
│   ├── shopping-list/
│   └── settings/
└── shared/                  # Shared components, pipes, utilities
    ├── components/
    ├── pipes/
    └── utilities/
```

## 9. Backend Setup

### API Project Structure

```
Dishhive.Api/
├── Program.cs               # Application entry point + DI
├── appsettings.json         # Base configuration
├── appsettings.Development.json  # Dev overrides
├── Data/
│   └── DishhiveDbContext.cs # EF Core context
├── Models/
│   ├── (domain entities)
│   └── DTOs/
├── Controllers/
├── Extensions/
│   └── DatabaseExtensions.cs
├── Services/
└── Migrations/
```

### API Conventions

- RESTful endpoints under `/api/` prefix
- Controller naming: `{Resource}Controller`
- DTOs for request/response separation
- Swagger/OpenAPI enabled in Development
- CORS configured for Angular dev server

## 10. Configuration Strategy

### Backend

- `appsettings.json` for base configuration
- `appsettings.Development.json` for local overrides
- Environment variables for sensitive values (connection strings)
- Docker Compose environment variables for containerized runtime

### Frontend

- `environments/environment.ts` for production defaults
- `environments/environment.development.ts` for dev overrides
- API base URL configurable per environment

## 11. API & Contract Structure

### RESTful Design

Following Freezy conventions:

| Resource | Base Path | Methods |
|----------|-----------|---------|
| Family Members | `/api/family-members` | CRUD |
| Recipes | `/api/recipes` | CRUD + import |
| Week Plans | `/api/week-plans` | CRUD |
| Shopping Lists | `/api/shopping-lists` | GET, POST (generate) |
| Settings | `/api/settings` | CRUD |
| Statistics | `/api/statistics` | GET |
| Freezy Integration | `/api/freezy` | GET (frozen items) |

### DTOs Pattern

Request and response DTOs will be used to separate API contracts from domain models, following the same pattern as Freezy.

## 12. Documentation Structure

| Document | Path | Purpose |
|----------|------|---------|
| Infrastructure Plan | `docs/infrastructure-setup-plan.md` | This document |
| Architecture | `docs/architecture.md` | System architecture overview |
| Docs README | `docs/README.md` | Documentation index |
| Future Features | `docs/future-features.md` | Future capability roadmap |
| Feature Plans | `docs/features/*.md` | Per-feature implementation plans |

### Documentation Conventions

- Same markdown style as Freezy
- Tables for structured data
- Code blocks with language hints
- Checklist format: `[ ]` new, `[~]` in progress, `[x]` done
- Living documents updated as implementation progresses

## 13. Local Development Strategy

### Prerequisites

- Docker Desktop
- .NET 10 SDK
- Node.js 20+
- npm

### Development Commands

```bash
# Start infrastructure
docker compose up -d postgres

# Run API
cd src/Dishhive.Api
dotnet run

# Run frontend
cd src/Dishhive.Web
npm start

# Run tests
cd src/Dishhive.Tests
dotnet test
```

### Full Docker Development

```bash
docker compose up --build
```

## 14. Integration Points with Freezy

### Architecture

Dishhive integrates with Freezy through a clean boundary:

1. **Freezy API Client Service**: An abstraction in Dishhive that communicates with the Freezy API
2. **Configuration-driven**: Freezy API endpoint and credentials configurable via appsettings
3. **Resilient**: Integration failures do not block core Dishhive functionality
4. **Decoupled**: Freezy data is cached/mapped to Dishhive domain models

### Data Flow

```
Dishhive UI -> Dishhive API -> FreezyApiClient -> Freezy API
                                        |
                               FrozenItemDto -> Dishhive models
```

### Future Considerations

- Shared event bus for real-time sync (future)
- Shared authentication (future)
- Direct database access NOT used (API-only integration)

## 15. Assumptions

1. PostgreSQL 17 is available and suitable for Dishhive data needs
2. Freezy API will remain stable or provide backward-compatible changes
3. Single-household usage initially (multi-tenant not required for v1)
4. Local-first development with Docker; cloud deployment is future work
5. Angular 21+ standalone component model is the target
6. .NET 10 LTS is the target framework
7. No authentication required for initial version (single-user household app)

## 16. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Freezy API changes break integration | High | Abstraction layer, versioned contracts, retry logic |
| Recipe scraping blocked by target sites | Medium | Pluggable source-provider architecture, multiple sources |
| Port conflicts in shared dev environment | Low | Distinct port allocation documented |
| Scope creep from Mealie inspiration | Medium | Clear feature boundaries, future-features doc for backlog |
| Measurement conversion accuracy | Medium | Well-tested conversion library, preserve original values |

## 17. Open Questions

1. Should Dishhive share any common libraries with Freezy, or remain fully independent?
2. What is the expected scale of the recipe store (hundreds vs thousands of recipes)?
3. Should recipe images be stored locally or referenced by URL?
4. Is user authentication needed, or is single-user household sufficient for v1?
5. Should the Freezy integration use the existing Freezy API or a shared database?

## 18. Implementation Order

1. [ ] Infrastructure scaffold (solution, projects, Docker)
2. [ ] Documentation skeleton
3. [ ] Database context and first migration
4. [ ] Basic API health endpoint
5. [ ] Frontend scaffold with routing
6. [ ] Feature: Family composition (foundation for all planning)
7. [ ] Feature: Recipe store
8. [ ] Feature: Recipe import
9. [ ] Feature: Week planner
10. [ ] Feature: Shopping list export
11. [ ] Feature: Past dishes and statistics
12. [ ] Feature: Freezy integration
13. [ ] Feature: Measurement preferences
14. [ ] Polish, testing, documentation updates

## 19. Checklist

### Infrastructure
- [ ] Solution file created
- [ ] API project scaffolded
- [ ] Web project scaffolded
- [ ] Test project scaffolded
- [ ] Docker Compose configured
- [ ] Dockerfiles created
- [ ] Database initialization script created
- [ ] .gitignore configured
- [ ] .dockerignore configured
- [ ] Documentation skeleton created
- [ ] First EF migration created
- [ ] Health check endpoint working
- [ ] Frontend serves correctly
- [ ] Docker Compose starts all services

### Documentation
- [ ] Infrastructure plan (this document)
- [ ] Architecture document
- [ ] Docs README
- [ ] Future features document
- [ ] All feature planning documents
