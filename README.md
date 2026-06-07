# Dishhive

A self-hosted family week-menu planning application — sibling app to **Freezy** (FreezerInventory).

## What it does
- Plan a family week menu (specific dishes or vague intents like "something with fish")
- Track family members, guests, preferences, allergies and dietary constraints
- Manage recipes (own + imported from external sources)
- Track planned-dish history, favorites and statistics
- Reuse frozen leftovers/meals from Freezy
- Generate shopping lists from the planned week
- Reserve clean extension points for future AI-assisted planning

## Tech Stack

| Component | Technology |
|-----------|------------|
| Frontend | Angular 21, Angular Material 21.1.x |
| Backend | .NET 10 Web API |
| Database | PostgreSQL 16 |
| Container | Docker & Docker Compose |

Architecture mirrors Freezy: a single .NET container serves both `/api/*` and the Angular SPA, backed by a PostgreSQL sidecar. Default local ports differ from Freezy so both apps can run side-by-side.

| Service | Freezy | Dishhive |
|---|---|---|
| App (HTTP) | 5000 | **5100** |
| App (HTTPS dev) | 5001 | **5101** |
| Angular dev | 4200 | **4300** |
| Postgres | 5432 | **5433** |

## Quick Start (Docker)

```bash
docker-compose up -d
# App:     http://localhost:5100
# Swagger: http://localhost:5100/swagger
```

## Local Development

```bash
# 1) Start the database only
docker-compose up -d db

# 2) Run the API
cd src/Dishhive.Api
dotnet watch run

# 3) Run the Angular app (proxies /api to the .NET API)
cd src/dishhive-web
npm install
npm start
```

## Repository layout

```
Dishhive/
├── src/
│   ├── Dishhive.Api/         # .NET 10 Web API (also serves Angular)
│   ├── Dishhive.Api.Tests/   # xUnit tests
│   └── dishhive-web/         # Angular 21 SPA
├── docker/
│   └── app.Dockerfile
├── docker-compose.yml
├── docs/
│   ├── PROJECT_GUIDELINES.md
│   ├── TESTING_STRATEGY.md
│   ├── plans/INFRASTRUCTURE_PLAN.md
│   ├── research/RECIPE_IMPORT_RESEARCH.md
│   └── features/*.md
├── possible-features.md
└── Dishhive.sln
```

See [docs/plans/INFRASTRUCTURE_PLAN.md](docs/plans/INFRASTRUCTURE_PLAN.md) for the full setup plan and
[docs/features/](docs/features/) for per-feature plans.
