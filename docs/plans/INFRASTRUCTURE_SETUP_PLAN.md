# Dishhive Infrastructure & Setup Plan

**Created:** June 10, 2026
**Status:** ✅ Implemented — see checklist in §15; feature work tracked in `docs/features/`
**Reference implementation:** Freezy (`C:\Source\FreezerInventory`)

## Executive Summary

Dishhive is a household meal planning application: plan a family week menu, track family
preferences and constraints, manage recipes, reuse frozen leftovers from Freezy, and generate
shopping lists — with room for future AI-assisted planning.

Dishhive is a **separate application** in the same ecosystem as Freezy. It mirrors Freezy's
platform approach (Angular + Material frontend, .NET Web API backend, PostgreSQL, single-container
Docker deployment) and follows the conventions documented in `FreezerInventory/docs`. It does not
share code or a database with Freezy; integration happens over Freezy's public REST API only.

---

## Table of Contents

1. [Repository / Folder Structure](#1-repository--folder-structure)
2. [Frontend Setup](#2-frontend-setup)
3. [Backend Setup](#3-backend-setup)
4. [Datastore Choice & Rationale](#4-datastore-choice--rationale)
5. [Docker Setup](#5-docker-setup)
6. [Local Run Strategy](#6-local-run-strategy)
7. [Port Allocation](#7-port-allocation)
8. [Configuration Strategy](#8-configuration-strategy)
9. [API / Contracts Structure](#9-api--contracts-structure)
10. [Documentation Skeleton](#10-documentation-skeleton)
11. [Freezy Integration Points](#11-freezy-integration-points)
12. [Assumptions](#12-assumptions)
13. [Risks](#13-risks)
14. [Open Questions](#14-open-questions)
15. [Implementation Checklist](#15-implementation-checklist)

---

## 1. Repository / Folder Structure

Mirrors Freezy's layout one-to-one (sibling folder under `C:\Source`):

```
Dishhive/
├── src/
│   ├── Dishhive.Api/             # .NET 10 Web API (serves Angular static files too)
│   ├── Dishhive.Api.Tests/       # xUnit test project (unit + integration)
│   └── dishhive-web/             # Angular 21 + Angular Material frontend
├── docker/
│   └── app.Dockerfile            # Multi-stage build: Angular → .NET → runtime
├── docs/
│   ├── PROJECT_GUIDELINES.md     # Architecture decisions and conventions
│   ├── angular/                  # Frontend standards (inherited from Freezy)
│   ├── dotnet/                   # Backend standards (inherited from Freezy)
│   ├── plans/                    # Large implementation plans (this document)
│   └── features/                 # One living document per functional feature
├── docker-compose.yml            # app + db orchestration
├── Dishhive.sln                  # Solution file
├── dotnet-tools.json             # Local dotnet tools manifest (nswag)
├── possible-features.md          # Future feature ideas (Mealie-inspired)
├── .gitignore / .dockerignore
├── LICENSE                       # MIT (already present)
└── README.md
```

Naming conventions (from `FreezerInventory/docs/PROJECT_GUIDELINES.md`):

- Backend project: `Dishhive.Api`, PascalCase C# files, controllers `FeatureNameController.cs`,
  DTOs `FeatureNameDtos.cs`, DbContext `DishhiveDbContext.cs`.
- Frontend project: `dishhive-web` (kebab-case), pages under `src/app/pages/feature-name/`,
  services `feature-name.service.ts`, models `feature-name.model.ts`.
- Database: PascalCase singular table names (`FamilyMember`, `Recipe`, `PlannedMeal`).
- Docs: major plans in `docs/plans/` (SCREAMING_SNAKE_CASE), topic standards kebab-case,
  feature documents in `docs/features/` kebab-case (e.g. `week-planner.md`).

## 2. Frontend Setup

Same stack and structure as `freezer-inventory-web`:

| Aspect | Choice |
|--------|--------|
| Framework | Angular 21 (standalone components, signals, OnPush) |
| UI | Angular Material 21.1.x, Material Design 3 theming, no hardcoded colors |
| Styling | SCSS, `--mat-sys-*` CSS variables only, modern CSS (gap, logical props, auto-fill grids) |
| Testing | Vitest via `@angular/build:unit-test`, jsdom |
| API client | NSwag-generated TypeScript client from the OpenAPI document |
| Dev server | `ng serve` on port **4300** with `/api` proxy to the backend |

Structure under `src/dishhive-web/src/app/`: `pages/`, `components/`, `services/`, `models/`,
`api/generated/`, `testing/`.

Deviation from Freezy: Dishhive is **not** set up as a PWA initially (no service worker,
no offline queue). Meal planning is a sit-down activity, not an in-store scanning flow.
PWA support can be added later following Freezy's pattern; see `possible-features.md`.

## 3. Backend Setup

Same stack and structure as `FreezerInventory.Api`:

| Aspect | Choice |
|--------|--------|
| Runtime | .NET 10 Web API, controllers + DTOs (manual mapping, no AutoMapper) |
| Persistence | EF Core 10 + Npgsql, migrations auto-applied at startup with retry |
| API docs | Native OpenAPI (`AddOpenApi`) + Scalar UI at `/scalar/v1` |
| Health | `/health` endpoint with Npgsql health check |
| Hosting | Serves Angular build output from `wwwroot` with SPA fallback |
| Testing | xUnit + FluentAssertions + NSubstitute + `WebApplicationFactory`, EF InMemory in `Testing` environment |

Structure under `src/Dishhive.Api/`: `Controllers/`, `Models/` (+`Models/DTOs/`), `Data/`
(+`Data/Migrations/`), `Services/`, `Extensions/`.

Service seams created up front (interfaces only, no speculative implementation):

- `IRecipeSourceProvider` — pluggable recipe import sources (see `docs/features/recipe-import.md`)
- `IFreezyClient` — Freezy REST integration boundary (see `docs/features/freezy-integration.md`)
- `IMealSuggestionService` — extension point for future AI-assisted planning
  (see `docs/features/week-planner.md`); only the seam, no implementation now

## 4. Datastore Choice & Rationale

**PostgreSQL 16 (alpine) + EF Core / Npgsql** — identical to Freezy.

Rationale:

- Ecosystem consistency: same operational knowledge, same backup/restore story, same
  `docker-compose` shape, same EF Core conventions (`gen_random_uuid()` defaults,
  `CURRENT_TIMESTAMP`, PascalCase tables).
- Relational fit: meal planning is naturally relational (members ↔ meals ↔ recipes ↔
  ingredients) and benefits from FK integrity and aggregate statistics queries.
- JSONB available for source-specific raw recipe payloads (traceability) without a second store.

Separate database instance and volume — Dishhive runs its **own** `postgres:16-alpine` container
(`dishhive-db`, host port 5433) rather than sharing Freezy's. Keeps the apps independently
deployable/resettable, at the cost of a second container (acceptable for self-hosted use).

## 5. Docker Setup

Same single-container pattern as Freezy (`docker/app.Dockerfile`):

1. **Stage 1** — `node:22-alpine`: `npm ci` + `ng build` production build of `dishhive-web`
2. **Stage 2** — `dotnet/sdk:10.0`: restore + publish `Dishhive.Api`
3. **Stage 3** — `dotnet/aspnet:10.0`: non-root user, Angular output copied to `wwwroot`,
   curl-based `/health` healthcheck, `EXPOSE 5100`

`docker-compose.yml` defines `db` (`dishhive-db`) and `app` (`dishhive-app`) with a named
volume `postgres_data`, healthcheck-gated startup, and `restart: unless-stopped` — structurally
identical to Freezy's compose file but with Dishhive names, ports, and credentials.

## 6. Local Run Strategy

Same two modes as Freezy:

**Full stack in Docker:**
```bash
cd Dishhive
docker-compose up -d --build
# App:    http://localhost:5100
# Scalar: http://localhost:5100/scalar/v1
```

**Fast inner loop (db in Docker, code local):**
```bash
docker-compose up -d db                       # PostgreSQL on localhost:5433
cd src/Dishhive.Api && dotnet watch run       # http://localhost:5100, https://localhost:5101
cd src/dishhive-web && npm start              # http://localhost:4300, /api proxied to 5100
```

## 7. Port Allocation

Chosen to never collide with Freezy so both apps can run side by side:

| Purpose | Freezy | **Dishhive** |
|---------|--------|--------------|
| App container / API HTTP | 5000 | **5100** |
| API HTTPS (local dev) | 5001 | **5101** |
| PostgreSQL (host) | 5432 | **5433** (container-internal stays 5432) |
| Angular dev server | 4200 | **4300** |

## 8. Configuration Strategy

Same as Freezy: `appsettings.json` defaults + environment variable overrides in compose.

| Setting | Default | Notes |
|---------|---------|-------|
| `ConnectionStrings__DefaultConnection` | `Host=localhost;Port=5433;Database=dishhive;Username=dishhive;Password=dishhive_dev_password` | compose overrides Host/Port to `db:5432` |
| `ASPNETCORE_ENVIRONMENT` | `Development` locally, `Production` in compose | `Testing` disables Npgsql registration |
| `Freezy__BaseUrl` | empty (integration disabled) | e.g. `http://localhost:5000` / `http://host.docker.internal:5000` |
| `RecipeImport__UserAgent` | `Dishhive/1.0` | sent on outbound scraping requests |

User-facing settings (e.g. measurement system) are stored in a `UserSetting` key-value table,
exactly like Freezy's `SettingsController` pattern — not in config files.

Version management follows Freezy's `docs/VERSION_MANAGEMENT.md`: `package.json` is the single
source of truth, `scripts/sync-version.js` propagates to the csproj and generated
`AppVersion.cs` / `version.ts`.

## 9. API / Contracts Structure

- REST under `/api/*`, plural resource names, standard verbs/status codes per
  `docs/dotnet/api-standards.md`.
- Planned resource groups: `/api/familymembers`, `/api/recipes`, `/api/recipes/import`,
  `/api/plannedmeals` (week planner), `/api/shoppinglist`, `/api/settings`, `/api/statistics`.
- OpenAPI 3.1 document at `/openapi/v1.json`; the Angular typed client is generated from it
  via NSwag (`npm run generate:api-client`), same tooling versions as Freezy.
- DTOs never expose EF entities; Create/Update/Read DTO split per feature.

## 10. Documentation Skeleton

| Document | Purpose |
|----------|---------|
| `README.md` | Quick start, architecture diagram, ports, dev workflow (mirrors Freezy README) |
| `docs/PROJECT_GUIDELINES.md` | Dishhive-specific architecture decisions + conventions |
| `docs/angular/`, `docs/dotnet/` | Standards inherited from Freezy (copied so Dishhive is self-contained) |
| `docs/plans/INFRASTRUCTURE_SETUP_PLAN.md` | This document |
| `docs/features/*.md` | One living document per feature, each with a phased plan and a `[ ]`/`[~]`/`[x]` implementation checklist |
| `possible-features.md` | Future feature ideas (Mealie-inspired) |

Checklist status convention used in all feature documents:
`[ ]` = new, `[~]` = in progress, `[x]` = done.

## 11. Freezy Integration Points

Documented in detail in `docs/features/freezy-integration.md`. Summary of the boundary:

- **Direction:** Dishhive → Freezy only. Dishhive consumes Freezy's existing REST API
  (`GET /api/items`, …). Freezy is never modified for Dishhive.
- **Boundary:** a single `IFreezyClient` interface in `Dishhive.Api` plus an anti-corruption
  layer mapping Freezy DTOs onto Dishhive's own `FrozenItem` read model. No Freezy types leak
  past the client.
- **Configuration:** `Freezy__BaseUrl`; when unset or unreachable, Dishhive degrades gracefully
  (planner simply shows no freezer suggestions).
- **No shared database, no shared code.** If shared libraries ever become worthwhile, that is
  an explicit future decision (see Open Questions).

## 12. Assumptions

1. Freezy's tech stack versions (Angular 21.1.x, .NET 10, PostgreSQL 16) are the ecosystem
   standard; Dishhive pins the same majors.
2. Single-household, single-user deployment for now (like Freezy's default mode). The
   multi-user direction explored in Freezy's `USER_SYSTEM_PLAN.md` is out of scope.
3. `docs/features/` (one kebab-case file per feature) is a new convention for the ecosystem;
   Freezy keeps large plans in `docs/plans/` and ideas in `possible-features.md`. Dishhive uses
   `docs/plans/` for this infra plan, `docs/features/` for feature tracking, and
   `possible-features.md` for future ideas — closest possible alignment with Freezy.
4. No PWA/offline support initially (deliberate deviation, see §2).
5. Dagelijkse Kost has no formal API; JSON-LD extraction is the sanctioned approach
   (research documented in `docs/features/recipe-import.md`).
6. Metric is the default measurement system; imported values are normalized to metric while
   original source values are preserved (see `docs/features/measurement-preferences.md`).

## 13. Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| Recipe source HTML/JSON-LD changes | Import breaks | Provider isolated behind `IRecipeSourceProvider`; fixture-based tests detect drift; raw payload stored for re-parsing |
| Freezy API contract changes | Integration breaks | Anti-corruption layer; integration optional + degrades gracefully |
| Scraping etiquette/legal | Source blocks requests | On-demand single-page import only (no crawling); honest User-Agent; raw data stored privately per household |
| Port drift (someone changes Freezy ports) | Local collisions | Port table in both READMEs; compose files are the source of truth |
| Scope creep (AI planning, multi-user) | Foundation never ships | Seams only (`IMealSuggestionService`), features tracked in `possible-features.md` |
| Unit conversion ambiguity ("1 bakje", "0,5 citroenen") | Bad shopping lists | Keep original text verbatim; normalization is best-effort and editable |

## 14. Open Questions

1. **Container-to-container Freezy access in Docker** — `host.docker.internal` works on Docker
   Desktop (Windows); a shared external Docker network would be cleaner for Linux servers.
   Decide when first deploying both apps on the same host.
2. **Shared ecosystem libraries** (e.g. a common Angular theme package) — only worth it once a
   third app appears; for now copy-and-adapt.
3. **Authentication** — both apps are LAN/self-hosted and unauthenticated; if either gains auth,
   the Freezy client needs a token strategy.
4. ~~**Recipe images** — hotlink source URLs vs. download-and-store locally.~~
   **Resolved (June 2026):** images are downloaded at import time and stored in the database
   (`Recipe.ImageData` bytea + content type), served via `GET /api/recipes/{id}/image`;
   the source URL is kept as fallback/traceability. See `docs/features/recipe-store.md`.

## 15. Implementation Checklist

Infrastructure implementation tracking (feature work tracked in `docs/features/*.md`):

- [x] Folder skeleton (`src/`, `docker/`, `docs/`)
- [x] Solution + `Dishhive.Api` project (csproj, Program.cs, health, OpenAPI, Scalar)
- [x] `DishhiveDbContext` + initial EF migration
- [x] `Dishhive.Api.Tests` project (xUnit, fixtures, `TestWebApplicationFactory`)
- [x] `dishhive-web` Angular workspace (Material theme, routes, proxy, shell)
- [x] `docker/app.Dockerfile` (multi-stage)
- [x] `docker-compose.yml` (app 5100, db 5433)
- [x] `.gitignore`, `.dockerignore`, `dotnet-tools.json`
- [x] `README.md` with quick start + port table
- [x] `docs/PROJECT_GUIDELINES.md`
- [x] Verified: `dotnet build` + `dotnet test` green (36 backend tests)
- [x] Verified: `ng build` green (+ 2 frontend tests)
- [x] Verified: `docker-compose up` serves app on http://localhost:5100 with healthy `/health`,
      working family member CRUD, settings round-trip and a live recipe import
