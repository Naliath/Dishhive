# Dishhive — Infrastructure & Setup Plan

> Created before any feature implementation. This document is the authoritative description of how Dishhive is wired up. Update it whenever the setup changes.

## 1. Goals
- Keep Dishhive logically separate from Freezy while reusing its proven conventions.
- Enable both apps to run side-by-side on a single developer machine.
- Provide clean extension seams for: recipe sources, Freezy integration, AI-assisted planning, and unit conversion.
- Stay simple: single .NET container + Postgres sidecar, no auth in v1.

## 2. Repository / folder structure

```
Dishhive/
├── Dishhive.sln
├── README.md
├── docker-compose.yml
├── docker/
│   └── app.Dockerfile
├── docs/
│   ├── PROJECT_GUIDELINES.md
│   ├── TESTING_STRATEGY.md
│   ├── plans/INFRASTRUCTURE_PLAN.md   ← this file
│   ├── research/RECIPE_IMPORT_RESEARCH.md
│   └── features/                       ← one .md per feature
├── possible-features.md
└── src/
    ├── Dishhive.Api/
    ├── Dishhive.Api.Tests/
    └── dishhive-web/
```

This mirrors Freezy 1:1 (`FreezerInventory.sln` ↔ `Dishhive.sln`, `src/<App>.Api/` etc.).

## 3. Backend setup

- **Framework**: .NET 10 Web API
- **Project**: `src/Dishhive.Api/Dishhive.Api.csproj`
- **Namespaces**: root `Dishhive.Api`, sub-namespaces by module (`Dishhive.Api.Models.Recipes`, etc.)
- **DbContext**: `DishhiveDbContext` — one DbSet per aggregate
- **Migrations**: applied automatically on startup via `app.MigrateDatabaseAsync()` (same retry/backoff helper as Freezy)
- **Configuration**:
  - `ConnectionStrings:DefaultConnection` (env override `ConnectionStrings__DefaultConnection`)
  - `Dishhive:Freezy:BaseUrl` for Freezy integration (default: `http://localhost:5000`)
  - `Dishhive:Defaults:MeasurementSystem` (`metric`)
- **Static SPA**: Angular build output is copied to `wwwroot/` and served as the SPA fallback (same pattern as Freezy).

## 4. Frontend setup

- **Framework**: Angular 21 standalone + Material 21.1.x.
- **Project**: `src/dishhive-web/`.
- **Routing**: Standalone component routes:
  - `/planner` (default) — week planner
  - `/family` — household composition
  - `/recipes` — recipe store
  - `/recipes/import` — recipe import
  - `/shopping-list` — shopping list export
  - `/history` — past dishes & favorites
  - `/settings` — measurement system, Freezy connection
- **Proxy**: `proxy.conf.json` proxies `/api` to `http://localhost:5100`.
- **PWA**: Not required v1 (Freezy is the field-use PWA). Architecture stays compatible — `@angular/service-worker` can be added later without restructuring.

## 5. Datastore choice

- **PostgreSQL 16** for parity with Freezy.
- Same image (`postgres:16-alpine`).
- Separate database/container per app (`dishhive-db` vs `freezer-db`) on port **5433** to avoid clashing with Freezy's 5432.
- Rationale: shared ops experience, JSONB support for storing source-specific raw recipe data, full-text search potential for recipes.

## 6. Docker setup

`docker-compose.yml` defines two services:

| Service | Image / Build | Port mapping |
|---|---|---|
| `db` | `postgres:16-alpine` | `5433:5432` |
| `app` | `docker/app.Dockerfile` (multi-stage) | `5100:5100` |

Multi-stage Dockerfile follows Freezy's: node build → dotnet build/publish → aspnet:10.0 runtime, with a non-root `appuser` and curl-based healthcheck on `/health`.

## 7. Local run strategy

Same three-mode strategy as Freezy:

1. **Full Docker** — `docker-compose up -d`
2. **Database only + local API/SPA** — `docker-compose up -d db`, then `dotnet watch run` and `npm start`
3. **All local** — Postgres on the host, env-override the connection string

## 8. Local port allocation

| Purpose | Freezy | Dishhive |
|---|---|---|
| App container HTTP | 5000 | **5100** |
| App container HTTPS (dev) | 5001 | **5101** |
| Angular dev server | 4200 | **4300** |
| Postgres (host) | 5432 | **5433** |

`launchSettings.json` and `proxy.conf.json` use these.

## 9. Configuration strategy

`appsettings.json` ships defaults; `appsettings.Development.json` overrides for local dev; container deployment relies on environment variables (`ConnectionStrings__DefaultConnection`, `Dishhive__Freezy__BaseUrl`, `Dishhive__Defaults__MeasurementSystem`).

Secrets are out of scope in v1 — no auth, no API keys (recipe import uses public web).

## 10. API / contracts structure

- All HTTP under `/api/<resource>`
- DTOs are records under `Models/DTOs/<Module>Dtos.cs`
- OpenAPI / Swagger always enabled (`/swagger`)
- Health probe at `/health`

Initial controller surface:
- `FamilyMembersController`, `GuestsController`
- `RecipesController`
- `RecipeImportController` — `POST /api/recipe-import/preview` (URL → parsed recipe), `POST /api/recipe-import/save` (preview → persisted recipe)
- `WeekPlansController` — week-by-week CRUD, slot operations
- `ShoppingListsController` — generate from a week plan
- `HistoryController` — past dishes & favorites
- `FreezyIntegrationController` — proxy/cached Freezy items available for planning
- `SettingsController` — measurement system, Freezy connection
- `StatisticsController` — dish frequency

## 11. Documentation skeleton

| File | Purpose |
|---|---|
| `docs/PROJECT_GUIDELINES.md` | Conventions |
| `docs/TESTING_STRATEGY.md` | Testing approach |
| `docs/plans/INFRASTRUCTURE_PLAN.md` | This document |
| `docs/research/RECIPE_IMPORT_RESEARCH.md` | DKE / scraping investigation |
| `docs/features/<feature>.md` | One file per feature with checklist |
| `possible-features.md` | Future / inspirational features |

## 12. Extension points

| Seam | Interface | Notes |
|---|---|---|
| Recipe import | `IRecipeSourceProvider` | One implementation per source; `RecipeSourceRegistry` resolves by URL host |
| Freezy integration | `IFreezyClient` | Hides whether data comes from HTTP or a future shared module |
| AI suggestions | `IMealSuggestionStrategy` | Placeholder; default returns `null` |
| Unit conversion | `IUnitConversionService` | Preserves original source values |

## 13. Assumptions

- Single-household, no auth, single timezone (server local).
- Dishhive lives in its own git repo (`c:\Source\Dishhive`), separate from `c:\Source\FreezerInventory`. Cross-repo coupling only happens via HTTP.
- Postgres is acceptable for recipe text storage in v1; if full-text search becomes a bottleneck later, we'll add `pg_trgm` indexes (no architectural change required).
- Recipe scraping only targets sources that allow it. Robots.txt and terms-of-use must be checked per provider; see [recipe import research](../research/RECIPE_IMPORT_RESEARCH.md).
- Frozen meals from Freezy participate in planning by **reference only** — Dishhive does not duplicate Freezy's inventory.

## 14. Risks

- **Recipe-source fragility**: scraping breaks when sites change layout. Mitigation: provider abstraction + per-provider fixture-based tests + clear error handling.
- **Cross-app coupling**: pulling Freezy data via HTTP requires Freezy to be running and reachable. Mitigation: `IFreezyClient` returns an empty list when Freezy is unreachable, with a UI notice.
- **Unit conversion accuracy**: imprecise conversions can ruin recipes. Mitigation: store original values + units alongside normalized values; never overwrite source data.
- **AI overreach**: building AI now would balloon scope. Mitigation: only seam, no implementation.

## 15. Open questions

1. Should Freezy expose a richer API (categories/tags) for planning, or do we keep using its existing `/api/items`? Tracked in [freezy-integration.md](../features/freezy-integration.md).
2. Do we want recipe ratings/reviews now, or defer? Currently deferred (see `possible-features.md`).
3. Scaling beyond one household — when (if ever)? Defer until requested.

## 16. Intended integration points with Freezy

- **Read-only HTTP** to Freezy's `GET /api/items` to surface frozen items for planning.
- A small adapter (`FreezyHttpClient` implementing `IFreezyClient`) keeps Freezy's response shape from leaking past the integration boundary.
- Marking a frozen item as "planned" updates only Dishhive's planning data; no callback to Freezy in v1 (a future enhancement could mark items as consumed in Freezy).
- A configuration flag `Dishhive:Freezy:Enabled` lets users disable the integration entirely.
