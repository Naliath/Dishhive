# Dishhive Testing Strategy

Mirrors Freezy's testing approach so engineers move between projects without surprises.

## Philosophy
- Tests are a contract. Implementation changes must not silently break tests.
- Prefer integration tests for HTTP-shaped behaviour, unit tests for parsers, conversion logic and provider plug-ins.
- Always provide an automated test for **any** external-source integration (recipe import, Freezy integration) using a captured HTML / JSON fixture so the test does not hit the live web.

## Backend (.NET)

### Project
`src/Dishhive.Api.Tests/` — xUnit + FluentAssertions + NSubstitute.

| Concern | Library |
|---|---|
| Test framework | xUnit 2.9.x |
| Assertions | FluentAssertions 8.x |
| Mocking | NSubstitute 5.x |
| Integration | `Microsoft.AspNetCore.Mvc.Testing` 10.x |
| In-memory DB | `Microsoft.EntityFrameworkCore.InMemory` 10.x |
| Coverage | coverlet.collector |

### Layout
```
Dishhive.Api.Tests/
├── Builders/                # Fluent test data builders
├── Fixtures/                # Captured HTML/JSON for provider tests
├── Integration/             # WebApplicationFactory-based tests
├── Services/                # Pure service unit tests
├── TestBase.cs
└── TestWebApplicationFactory.cs
```

### Patterns
- `TestWebApplicationFactory` swaps `DishhiveDbContext` to a uniquely named EF Core InMemory database per factory instance.
- `TestBase` provides `Factory`, `Client`, `Scope`, `DbContext`, `ClearDatabase()`, `CreateFreshContext()`.
- Builders expose fluent helpers (e.g. `RecipeBuilder.Quick()`, `.WithIngredient(...)`).
- Provider tests load HTML/JSON from `Fixtures/` (as embedded resource or relative path) — never make outbound HTTP in CI.

### Required tests
- At least one **recipe-import provider test** validating that for a known fixture, the parser produces a recipe with: title, description, ingredients, steps, serving count, picture, video link (when present), source link, and preserved source-specific raw data.
- A health-check integration test (`GET /health` returns 200).

## Frontend (Angular)
- Vitest (Angular's default with `@angular/build:unit-test`) + jsdom.
- Service tests use `provideHttpClient()` + `provideHttpClientTesting()`.
- Builders in `src/app/testing/` for shared test data (e.g. `RecipeBuilder`, `FamilyMemberBuilder`).

## Running

```bash
# Backend
dotnet test

# Frontend
cd src/dishhive-web
npm test
```
