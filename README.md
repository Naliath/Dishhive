# Dishhive

A household meal planning application built on the same platform as [Freezy (FreezerInventory)](../FreezerInventory/README.md).

## Purpose

Dishhive helps families plan their weekly menu, manage recipes, track preferences and constraints, reuse frozen leftovers from Freezy, and generate shopping lists.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Angular 21 (standalone, PWA) + Angular Material |
| Backend | .NET 10 Web API |
| Database | PostgreSQL 16 |
| Testing | xUnit + FluentAssertions (backend) · Vitest (frontend) |
| Containerization | Docker + Docker Compose |

## Ports

| Service | Port |
|---------|------|
| App (HTTP) | **5100** |
| PostgreSQL | **5433** |
| Angular dev server | **4201** |

*(Different from Freezy's 5000/5432/4200 to allow both apps to run simultaneously.)*

## Quick Start

### With Docker

```bash
docker-compose up -d
# App available at http://localhost:5100
# Swagger at http://localhost:5100/swagger
```

### Local Development (without Docker)

**1. Start the database:**
```bash
docker-compose up -d db
```

**2. Run the API:**
```bash
cd src/Dishhive.Api
dotnet run
# http://localhost:5100
```

**3. Run the frontend:**
```bash
cd src/dishhive-web
npm install
npm start
# http://localhost:4201
```

### Run Tests

```bash
# Backend
dotnet test

# Frontend
cd src/dishhive-web
npm test
```

## Features

- 📅 **Week Planner** — Plan dinner (and other meals) for each day of the week
- 📖 **Recipe Store** — Store and search your recipe collection
- 🌐 **Recipe Import** — Import recipes from Dagelijkse Kost (VRT) via JSON-LD extraction
- 👨‍👩‍👧 **Family Composition** — Track household members, preferences, and allergies
- 🧊 **Freezy Integration** — Include frozen items from Freezy in meal planning
- 🛒 **Shopping List** — Auto-generate a shopping list from the week's planned meals
- ⚖️ **Measurement Preferences** — Metric (default) or imperial

## Documentation

- [Infrastructure Plan](docs/INFRASTRUCTURE_PLAN.md)
- [Project Guidelines](docs/PROJECT_GUIDELINES.md)
- [Testing Strategy](docs/TESTING_STRATEGY.md)
- [Version Management](docs/VERSION_MANAGEMENT.md)
- [Feature Plans](docs/features/)
- [Possible Future Features](possible-features.md)

## Freezy Integration

Dishhive can read frozen item data from Freezy via its HTTP API. Configure the integration in `appsettings.json`:

```json
{
  "FreezyIntegration": {
    "BaseUrl": "http://localhost:5000",
    "Enabled": true
  }
}
```

Set `Enabled: false` to use Dishhive without a running Freezy instance.
