# Dishhive

A household meal planning application: plan your family week menu, track family preferences
and constraints, manage recipes, reuse frozen leftovers from [Freezy](../FreezerInventory),
and generate shopping lists.

## Features

- 👨‍👩‍👧 **Family Composition** - Household members, guests, allergies, constraints and favorite dishes
- 📖 **Recipe Store** - Recipes with ingredients, steps and planning metadata; manual entry and editing
- ⬇️ **Recipe Import** - Import recipes from Dagelijkse Kost by URL (pluggable sources, locally stored images)
- 📅 **Week Planner** - Plan recipes, dishes or vague intentions per day with per-meal attendance; multiple dishes per day (e.g. lunch + dinner with appetizer and dessert)
- 🧊 **Freezy Integration** - Reuse frozen leftovers/meals tracked in Freezy *(optional)*
- 🛒 **Shopping Lists** - Generated from the planned week, scaled by attendance, copy-as-text
- 📊 **History & Statistics** - Past dishes, frequency stats, favorites from history
- ⭐ **Meal Feedback** - Mark meals eaten/skipped and rate them per member (1–5 stars); feeds statistics and AI suggestions
- ✨ **AI Week Suggestions** - Propose dinners for unplanned days from your household's constraints, favorites, ratings and freezer *(optional; Ollama, LM Studio, OpenAI, Mistral or Anthropic)*
- 📏 **Measurement Preferences** - Metric (default) or imperial display
- 🎬 **Demo Mode** - Seeds an empty database with 20 Dagelijkse Kost recipes and a demo household (on by default in Docker)
- 🐳 **Self-Hosted** - Run everything in Docker containers

## Tech Stack

| Component | Technology |
|-----------|------------|
| Frontend | Angular 21, Angular Material 21.1.x |
| Backend | .NET 10 Web API |
| Database | PostgreSQL 16 |
| Container | Docker & Docker Compose |

## Architecture

The application runs as a **single container** with the .NET API serving both the REST
endpoints and the Angular static files — the same platform approach as Freezy.

```
┌─────────────────────────────────────────┐
│           Docker Environment            │
│  ┌───────────────────────────────────┐  │
│  │  dishhive-app (Port 5100)         │  │
│  │  ┌─────────────────────────────┐  │  │
│  │  │  .NET 10 API                │  │  │      ┌──────────────────┐
│  │  │  - REST API (/api/*)        │──┼──┼─────►│ Freezy (optional)│
│  │  │  - Static Files (Angular)   │  │  │ HTTP │ Port 5000        │
│  │  └─────────────────────────────┘  │  │      └──────────────────┘
│  └───────────────────────────────────┘  │
│                    │                    │
│  ┌───────────────────────────────────┐  │
│  │  dishhive-db (PostgreSQL)         │  │
│  │  Host port 5433                   │  │
│  └───────────────────────────────────┘  │
└─────────────────────────────────────────┘
```

## Port Allocation

Dishhive deliberately uses different default ports than Freezy so both apps run side by side:

| Purpose | Freezy | Dishhive |
|---------|--------|----------|
| App container / API HTTP | 5000 | **5100** |
| API HTTPS (local dev) | 5001 | **5101** |
| PostgreSQL (host) | 5432 | **5433** |
| Angular dev server | 4200 | **4300** |

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (includes Docker Compose)
- [Node.js 22+](https://nodejs.org/) (for local frontend development)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (for local backend development)
- [Git](https://git-scm.com/)

## Quick Start (Docker)

```bash
cd Dishhive

# Start all services
docker-compose up -d --build

# The app will be available at:
# - App: http://localhost:5100
# - API Scalar UI: http://localhost:5100/scalar/v1
# - OpenAPI document: http://localhost:5100/openapi/v1.json
```

Demo mode is **on by default** in `docker-compose.yml` (`Demo__Enabled: "true"`): an empty
database is seeded in the background with 20 recipes scraped from Dagelijkse Kost and a demo
household (the Rocinante crew, including dietary constraints and favorite dishes). Set
`Demo__Enabled: "false"` for real use; see
[docs/features/demo-mode.md](docs/features/demo-mode.md).

To stop all services:
```bash
docker-compose down       # Stop and keep data
docker-compose down -v    # Stop and remove all data (including database)
```

## Local Development

For a faster inner loop, run the database in Docker and the frontend/backend locally.

**1. Start PostgreSQL:**
```bash
docker-compose up -d db
```

**2. Run the .NET API (Terminal 1):**
```bash
cd src/Dishhive.Api
dotnet watch run
```
- Runs at `https://localhost:5101` and `http://localhost:5100`
- Scalar UI: `https://localhost:5101/scalar/v1`

**3. Run the Angular app (Terminal 2):**
```bash
cd src/dishhive-web
npm install   # first time only
npm start
```
- Runs at `http://localhost:4300`
- API calls proxied to the .NET backend automatically

### Generate Angular API Client (NSwag)

```bash
# Terminal 1 - run API (OpenAPI endpoint must be reachable)
cd src/Dishhive.Api
dotnet watch run

# Terminal 2 - generate TypeScript clients
cd src/dishhive-web
npm run generate:api-client        # https
npm run generate:api-client:http   # http only
```

### Database Management

**Connect to PostgreSQL:**
- Host: `localhost`, Port: `5433`
- Database/Username: `dishhive`, Password: `dishhive_dev_password`

**Migrations:**
```bash
cd src/Dishhive.Api
dotnet ef migrations add MigrationName
dotnet ef database update
```
Migrations are applied automatically at application startup (with retry).

### Running Tests

```bash
# Backend (xUnit) - from repo root
dotnet test

# Frontend (Vitest)
cd src/dishhive-web
npm test -- --no-watch
```

The recipe import extraction is covered by offline fixture tests
(`src/Dishhive.Api.Tests/Services/DagelijkseKostProviderTests.cs`).

## Freezy Integration

Dishhive can suggest frozen leftovers/meals from a running Freezy instance in the week
planner. The integration is **optional and read-only**; see
[docs/features/freezy-integration.md](docs/features/freezy-integration.md).

Enable it by setting the Freezy base URL:

| Context | Setting |
|---------|---------|
| Local dev | `Freezy:BaseUrl` in `appsettings.Development.json`, e.g. `http://localhost:5000` |
| Docker | `Freezy__BaseUrl: "http://host.docker.internal:5000"` in `docker-compose.yml` |

When unset, the integration is disabled and Dishhive works standalone.

## AI Week Suggestions

The week planner can propose dinners for unplanned days using a configurable LLM provider
(via Microsoft.Extensions.AI), with a deterministic rules fallback when the model is
unreachable. The feature is **optional**: while `Ai__Provider` is empty the "Suggest week"
button is hidden and nothing AI-related runs. See
[docs/features/ai-week-planning.md](docs/features/ai-week-planning.md).

| Provider | Example configuration |
|----------|----------------------|
| Ollama (local) | `Ai__Provider=ollama` `Ai__Model=llama3.1` `Ai__BaseUrl=http://host.docker.internal:11434/v1` |
| LM Studio (local) | `Ai__Provider=lmstudio` `Ai__Model=<loaded model>` `Ai__BaseUrl=http://host.docker.internal:1234/v1` |
| OpenAI | `Ai__Provider=openai` `Ai__Model=gpt-4o-mini` `Ai__ApiKey=sk-...` |
| Anthropic | `Ai__Provider=anthropic` `Ai__Model=claude-opus-4-8` `Ai__ApiKey=sk-ant-...` |
| Mistral | `Ai__Provider=mistral` `Ai__Model=mistral-small-latest` `Ai__ApiKey=...` |
| Other OpenAI-compatible | `Ai__Provider=openai-compatible` `Ai__Model=...` `Ai__BaseUrl=https://.../v1` |

API keys also resolve from the standard `OPENAI_API_KEY` / `ANTHROPIC_API_KEY` /
`MISTRAL_API_KEY` environment variables when `Ai__ApiKey` is not set.

> **Local reasoning models** (Qwen3 family etc.): load them with a context window of at
> least 16k (e.g. `lms load <model> --context-length 16384` in LM Studio). With the default
> 4k context the model's thinking exhausts the window before the answer appears and every
> request falls back to the deterministic rules suggestions.

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ConnectionStrings__DefaultConnection` | See docker-compose | PostgreSQL connection string |
| `ASPNETCORE_ENVIRONMENT` | Production | Environment (Development/Production) |
| `Freezy__BaseUrl` | empty (disabled) | Base URL of a Freezy instance |
| `RecipeImport__UserAgent` | `Dishhive/0.1` | User-Agent for outbound recipe fetches |
| `Demo__Enabled` | `false` (`true` in docker-compose) | Seed an empty database with demo recipes and household |
| `Ai__Provider` | empty (disabled) | AI suggestion provider: `openai` \| `anthropic` \| `mistral` \| `ollama` \| `lmstudio` \| `openai-compatible` |
| `Ai__ApiKey` | empty | API key (falls back to `OPENAI_API_KEY` / `ANTHROPIC_API_KEY` / `MISTRAL_API_KEY`) |
| `Ai__BaseUrl` | per-provider default | Endpoint override (required for `openai-compatible`) |
| `Ai__Model` | empty | Model name, e.g. `llama3.1`, `gpt-4o-mini`, `claude-opus-4-8` |

## Documentation

| Document | Purpose |
|----------|---------|
| [docs/plans/INFRASTRUCTURE_SETUP_PLAN.md](docs/plans/INFRASTRUCTURE_SETUP_PLAN.md) | Infrastructure decisions, ports, assumptions, risks |
| [docs/features/](docs/features/README.md) | One living document per feature with implementation checklists |
| [docs/PROJECT_GUIDELINES.md](docs/PROJECT_GUIDELINES.md) | Architecture and coding conventions |
| [possible-features.md](possible-features.md) | Future feature ideas (Mealie-inspired) |

## License

MIT License - See [LICENSE](LICENSE) file
