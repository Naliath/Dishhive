# Dishhive — Architecture Overview

> **Status**: Planning
> **Last Updated**: 2026-06-07
> **Version**: 1.0

## 1. System Overview

Dishhive is a full-stack web application for family meal planning. It consists of three main components:

1. **Angular Frontend** — Single-page application with Angular Material
2. **.NET Backend API** — RESTful service with Entity Framework Core
3. **PostgreSQL Database** — Relational data store

These components communicate through well-defined boundaries and can run independently during development.

## 2. High-Level Architecture

```
┌─────────────────────────────────────────────────────┐
│                   Browser (Port 81)                  │
│                                                      │
│  ┌────────────────────────────────────────────────┐  │
│  │           Angular SPA (Dishhive.Web)           │  │
│  │  ┌─────────┐ ┌──────────┐ ┌────────────────┐  │  │
│  │  │  Core   │ │ Features │ │    Shared       │  │  │
│  │  │         │ │          │ │                 │  │  │
│  │  │Services │ │- Family  │ │ - Components    │  │  │
│  │  │Intercep │ │- Planner │ │ - Pipes         │  │  │
│  │  │tors    │ │- Recipes  │ │ - Utilities     │  │  │
│  │  │Models  │ │- Shopping │ │                 │  │  │
│  │  │       │ │- Settings │ │                 │  │  │
│  │  └─────────┘ └──────────┘ └────────────────┘  │  │
│  └──────────────────┬─────────────────────────────┘  │
│                     │ HTTP                           │
└─────────────────────┼────────────────────────────────┘
                      │
┌─────────────────────┼────────────────────────────────┐
│                     ▼   Port 5051                     │
│  ┌────────────────────────────────────────────────┐  │
│  │          .NET API (Dishhive.Api)               │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────────┐ │  │
│  │  │Controllers│ │Services  │  │   Data        │ │  │
│  │  │          │ │          │  │               │ │  │
│  │  │ Family   │ │ Family   │ │ DbContext      │ │  │
│  │  │ Recipe   │ │ Recipe   │ │ Migrations     │ │  │
│  │  │ Planner  │ │ Planner  │ │               │ │  │
│  │  │ Shopping │ │ Import   │ │               │ │  │
│  │  │ Settings │ │ Freezy   │ │               │ │  │
│  │  │ Stats   │ │          │ │               │ │  │
│  │  └──────────┘  └──────────┘  └──────────────┘ │  │
│  └──────────────────┬─────────────────────────────┘  │
│                     │ EF Core                        │
└─────────────────────┼────────────────────────────────┘
                      │
┌─────────────────────┼────────────────────────────────┐
│                     ▼   Port 5433                     │
│  ┌────────────────────────────────────────────────┐  │
│  │         PostgreSQL 17 (dishhive db)            │  │
│  │                                                │  │
│  │  - family_members                              │  │
│  │  - recipes                                     │  │
│  │  - week_plans                                  │  │
│  │  - plan_entries                                │  │
│  │  - shopping_lists                              │  │
│  │  - settings                                    │  │
│  │  - ...                                         │  │
│  └────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────┘

External Integration:
┌──────────────────────────────────────────────────────┐
│  Dishhive API ──HTTP──▶ Freezy API (external)        │
│                       Port 5050                       │
│                    (read-only)                        │
└──────────────────────────────────────────────────────┘
```

## 3. Layer Responsibilities

### Frontend Layer (Dishhive.Web)

| Concern | Detail |
|---------|--------|
| Framework | Angular 21+ with standalone components |
| UI Library | Angular Material |
| State | Signal-based reactive patterns |
| HTTP | HttpClient with interceptors |
| Routing | Feature-based lazy loading |
| Environments | Dev/prod API URL configuration |

### API Layer (Dishhive.Api)

| Concern | Detail |
|---------|--------|
| Framework | ASP.NET Core Minimal/Controllers |
| Serialization | System.Text.Json |
| Validation | FluentValidation |
| Documentation | Swagger/OpenAPI |
| CORS | Configured for Angular dev server |
| Error Handling | Global exception middleware |

### Data Layer

| Concern | Detail |
|---------|--------|
| ORM | Entity Framework Core 10 |
| Database | PostgreSQL 17 |
| Migrations | Code-first with EF Migrations |
| Keys | UUID with database-generated defaults |
| Auditing | CreatedAt/UpdatedAt timestamps |

## 4. Key Architectural Decisions

### 4.1. Full Independence from Freezy

Dishhive is a separate application with its own database, API, and frontend. Integration with Freezy happens only through HTTP API calls, never through shared code or direct database access.

**Rationale**: Clean separation allows independent deployment, versioning, and evolution.

### 4.2. Pluggable Recipe Source Architecture

Recipe import uses a provider pattern where each source (e.g., Dagelijkse Kost, future sources) implements a common interface. New sources can be added without modifying existing import logic.

**Rationale**: Recipe import is inherently fragile (external site structure changes). Isolation per source limits blast radius.

### 4.3. Extension Points for AI Planning

The week planner architecture includes a strategy interface for meal suggestion generation. The default implementation uses rule-based logic. AI-assisted planning can be plugged in later by providing a new strategy implementation.

**Rationale**: Avoids premature AI dependency while keeping the door open.

### 4.4. Measurement Normalization

Recipes store both normalized (metric) values and original source values. This supports:
- Consistent internal calculations
- Manual correction reference
- User preference display (metric/imperial toggle)

**Rationale**: Measurement conversion is lossy. Preserving originals enables user correction.

### 4.5. Single Household Scope (v1)

The initial version targets a single household without user authentication. Multi-user/multi-household support is a future consideration.

**Rationale**: Reduces initial complexity; the target use case is a family sharing one device or local network.

## 5. Integration Architecture

### 5.1. Freezy Integration

```
┌───────────────────┐
│  Dishhive API     │
│                   │
│  ┌─────────────┐  │
│  │FreezyClient │  │──── HTTP ────▶ Freezy API
│  │ (IService)  │  │   (read frozen)
│  └─────┬───────┘  │   items)
│        │          │
│  FrozenItemDto    │
└───────────────────┘
```

- `IFreezyClient` interface defines the contract
- Default implementation calls Freezy REST API
- Configuration via `appsettings.json` (endpoint URL)
- Circuit breaker pattern for resilience
- Cached results to reduce external dependency

### 5.2. Recipe Source Providers

```
┌──────────────────────────┐
│   RecipeImportService    │
│                          │
│  ┌────────┐ ┌─────────┐ │
│  │IRecipe │ │IRecipe  │ │
│  │Source- │ │Source-  │ │
│  │Provider│ │Provider │ │
│  │        │ │        │ │
│  │Dagelik │ │(Future)│ │
│  │seKost  │ │       │ │
│  └────────┘ └─────────┘ │
└──────────────────────────┘
```

- `IRecipeSourceProvider` interface: `CanHandle(Uri)`, `ExtractRecipeAsync(Uri)`
- Providers registered via DI
- Factory selects appropriate provider by URL pattern
- Each provider isolated — failure in one doesn't affect others

## 6. Security Considerations

| Concern | Status | Approach |
|---------|--------|----------|
| Authentication | Not in v1 | Single-user household app |
| CORS | Done | Configured for dev/prod origins |
| Data at rest | N/A | Local Docker, no sensitive data in v1 |
| API keys | N/A | No external API keys needed in v1 |
| Input validation | Planned | FluentValidation on all DTOs |

## 7. Deployment Model

### Development

```
Developer Machine
├── Docker Compose
│   ├── PostgreSQL (port 5433)
│   ├── .NET API (port 5051)
│   └── Nginx + Angular (port 81)
├── Dotnet CLI (direct API debugging)
└── npm (direct Angular debugging)
```

### Production (Future)

Same Docker Compose configuration with:
- Persistent volumes
- Environment-specific configuration
- HTTPS termination at Nginx

## 8. Cross-Cutting Concerns

### Logging
- Microsoft.Extensions.Logging
- Structured logging with Serilog (future)
- Log levels configurable per environment

### Error Handling
- Global exception middleware in API
- User-friendly error pages in Angular
- Consistent error response format

### Configuration
- Hierarchical: appsettings → environment vars → Docker env
- Strongly-typed configuration objects
- Validation at startup

### Testing
- Unit tests for services and business logic
- Integration tests for API endpoints
- Recipe import tests with sample data
