# Dishhive

Family week menu planning application.

Dishhive helps you plan weekly family meals, manage recipes, track preferences, integrate with frozen meal inventory (Freezy), and generate shopping lists.

## Quick Start

### Prerequisites

- Docker Desktop
- .NET 8 SDK (for local development without Docker)
- Node.js 18+ (for local Angular development)

### Running with Docker

```bash
docker compose up -d
```

Services will be available at:
- Web UI: http://localhost:8281
- API: http://localhost:8281/api
- Swagger: http://localhost:8281/swagger

### Local Development

See [Infrastructure Setup Plan](docs/infrastructure-setup-plan.md) for detailed development setup instructions.

## Documentation

- [Infrastructure Setup Plan](docs/infrastructure-setup-plan.md)
- [Architecture Overview](docs/architecture.md)
- [Feature Plans](docs/features/)
- [Future Features](docs/future-features.md)

## Features

| Feature | Status |
|---------|--------|
| Family Composition | Planned |
| Week Planner | Planned |
| Recipe Store | Planned |
| Recipe Import | Planned |
| Past Dishes & Statistics | Planned |
| Freezy Integration | Planned |
| Shopping List Export | Planned |
| Measurement Preferences | Planned |

## Tech Stack

- **Frontend**: Angular 17 + Angular Material
- **Backend**: .NET 8 Web API
- **Database**: PostgreSQL
- **Containerization**: Docker Compose

## License

MIT License. See [LICENSE](LICENSE) for details.
